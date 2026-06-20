using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using ECommerPipeline.Api.Hubs;
using ECommerPipeline.Api.Middleware;
using ECommerPipeline.Api.Observability;
using ECommerPipeline.Api.Security;
using ECommerPipeline.Application.Auth;
using ECommerPipeline.Application.Auth.DTOs;
using ECommerPipeline.Application.Auth.Validators;
using ECommerPipeline.Application.Common.Interfaces;
using ECommerPipeline.Application.Customers;
using ECommerPipeline.Application.Import;
using ECommerPipeline.Application.Orders;
using ECommerPipeline.Application.Orders.DTOs;
using ECommerPipeline.Application.Payments;
using ECommerPipeline.Application.Products;
using ECommerPipeline.Application.Products.DTOs;
using ECommerPipeline.Application.Reports;
using ECommerPipeline.Domain.Enums;
using ECommerPipeline.Infrastructure;
using ECommerPipeline.Application.Orders.Validators;
using ECommerPipeline.Api.Health;
using ECommerPipeline.Infrastructure.Initialization;
using ECommerPipeline.Infrastructure.Observability;
using ECommerPipeline.Infrastructure.Payments;
using FluentValidation;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) =>
{
    lc.ReadFrom.Configuration(ctx.Configuration)
      .Enrich.FromLogContext()                          // pulls CorrelationId from LogContext
      .Enrich.WithProperty("Application", "ECommerPipeline.Api");

    // Production: structured JSON (one object per line) for log aggregators.
    // Development: human-readable console template.
    if (ctx.HostingEnvironment.IsProduction())
        lc.WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter());
    else
        lc.WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {CorrelationId}{NewLine}{Exception}");

    var seqUrl = ctx.Configuration["Seq:Url"];
    if (!string.IsNullOrWhiteSpace(seqUrl))
        lc.WriteTo.Seq(seqUrl);
});

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Redis is OPTIONAL: when ConnectionStrings:Redis (or Redis:Configuration) is set,
// it becomes the distributed cache + SignalR backplane, enabling horizontal scale
// (multiple API instances share cache and SignalR groups). Unset → in-memory
// fallbacks, so local/dev runs with zero extra infrastructure.
var redisConn = builder.Configuration.GetConnectionString("Redis")
                ?? builder.Configuration["Redis:Configuration"];

var signalR = builder.Services.AddSignalR();
if (!string.IsNullOrWhiteSpace(redisConn))
    signalR.AddStackExchangeRedis(redisConn);
builder.Services.AddSingleton<IEtlNotifier, SignalREtlNotifier>();
builder.Services.AddSingleton<ICustomerNotifier, SignalRCustomerNotifier>();

// HttpClient to the AI Data Analyst service (NL→SQL on the Gold layer).
// The admin Chat UI calls our /api/ask, which proxies to the analyst — so the
// analyst stays internal (not exposed to the browser) and inherits our auth.
builder.Services.AddHttpClient("analyst", c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Analyst:BaseUrl"] ?? "http://localhost:8090");
    c.Timeout = TimeSpan.FromSeconds(60);
});

// Distributed cache for AI answers (skip repeat LLM calls). Redis when configured,
// else an in-memory IDistributedCache so the same code path works everywhere.
if (!string.IsNullOrWhiteSpace(redisConn))
    builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConn);
else
    builder.Services.AddDistributedMemoryCache();
builder.Services.AddMemoryCache();

// In-memory AI usage metrics (refusal rate, cache-hit rate, latency).
builder.Services.AddSingleton<AiMetrics>();

// Rate-limit the AI endpoint PER authenticated user: LLM calls cost money and are
// abusable. 15 questions / minute / user; excess gets HTTP 429.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // AI endpoint — per user; LLM calls cost money. 15 questions / minute.
    options.AddPolicy("ai-ask", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? httpContext.User.FindFirstValue("sub")
                          ?? httpContext.User.FindFirstValue(ClaimTypes.Email)
                          ?? httpContext.Connection.RemoteIpAddress?.ToString()
                          ?? "anon",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 15,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    // Auth endpoints — per IP; blunt brute-force protection. 10 attempts / minute.
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    // Global fallback — per IP; generous cap to blunt scraping/abuse without
    // hurting normal use. Long-lived (SignalR) + infra paths are exempt.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var path = httpContext.Request.Path;
        if (path.StartsWithSegments("/health") ||
            path.StartsWithSegments("/metrics") ||
            path.StartsWithSegments("/hangfire") ||
            path.StartsWithSegments("/hub"))
            return RateLimitPartition.GetNoLimiter("exempt");

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    });
});

// Fail-fast: never boot in Production with the well-known dev JWT secret or a weak one.
var configuredJwtSecret = builder.Configuration["Jwt:Secret"] ?? "";
if (builder.Environment.IsProduction() &&
    (configuredJwtSecret.Contains("dev-only-secret", StringComparison.Ordinal) || configuredJwtSecret.Length < 32))
{
    throw new InvalidOperationException(
        "Jwt:Secret is missing or insecure for Production. Provide a 32+ char random secret via " +
        "the Jwt__Secret environment variable (do not use the bundled dev default).");
}

// Fail-fast: never boot Production with the bundled dev SA password in a
// connection string (a common copy-paste mistake that ships weak DB creds).
if (builder.Environment.IsProduction())
{
    foreach (var name in new[] { "OltpConnection", "OlapConnection", "HangfireConnection" })
    {
        if ((builder.Configuration.GetConnectionString(name) ?? "")
            .Contains("YourStrong@Passw0rd", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"ConnectionStrings:{name} uses the bundled dev SA password in Production. " +
                "Set a strong password via environment variables.");
    }
}

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddValidatorsFromAssemblyContaining<CreateOrderRequestValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

// ============================================================
//  JWT Bearer authentication
// ============================================================
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is required. Set via env var Jwt__Secret or appsettings.");
var jwtIssuer   = builder.Configuration["Jwt:Issuer"]   ?? "ECommerPipeline";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "ECommerPipeline.Client";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtIssuer,
            ValidAudience            = jwtAudience,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew                = TimeSpan.FromMinutes(1),
        };

        // Allow SignalR to authenticate via query string ?access_token=...
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hub"))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    // Allowed origins can be overridden by env var Cors__AllowedOrigins
    // (comma-separated). Defaults cover Vite dev + preview + Docker frontend.
    var allowedOrigins = builder.Configuration["Cors:AllowedOrigins"]
        ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ?? new[]
        {
            "http://localhost:5173",  // Vite dev
            "http://localhost:4173",  // Vite preview
            "http://localhost",       // Docker nginx (port 80)
            "http://localhost:80",
        };

    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());          // cần cho SignalR
});

// ============================================================
//  OpenTelemetry — distributed tracing
//  Traces flow: HTTP request → controller → EF Core → SQL Server
//                            → ETL pipeline → SignalR push
//  Exports to OTLP endpoint (Jaeger via Docker, see docker-compose)
// ============================================================
var otlpEndpoint = builder.Configuration["Otel:Endpoint"]
    ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService(
            serviceName: "ECommerPipeline.Api",
            serviceVersion: "1.0.0")
        .AddAttributes(new[]
        {
            new KeyValuePair<string, object>("deployment.environment",
                builder.Environment.EnvironmentName),
        }))
    .WithMetrics(metrics =>
    {
        // Standard request/runtime metrics + our custom business meter, exposed
        // for Prometheus to scrape at /metrics (see MapPrometheusScrapingEndpoint).
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter(BusinessMetrics.MeterName)
            .AddPrometheusExporter();
    })
    .WithTracing(tracing =>
    {
        tracing
            .AddSource("ECommerPipeline.Etl")         // ← custom ActivitySource for ETL pipeline
            .AddSource("ECommerPipeline.DataQuality") // ← custom for DQ job
            .AddAspNetCoreInstrumentation(o =>
            {
                // Don't trace noisy endpoints
                o.Filter = ctx =>
                    !ctx.Request.Path.StartsWithSegments("/health") &&
                    !ctx.Request.Path.StartsWithSegments("/metrics") &&
                    !ctx.Request.Path.StartsWithSegments("/hangfire");
                o.RecordException = true;
            })
            .AddHttpClientInstrumentation()
            .AddSqlClientInstrumentation(o =>
            {
                // Include SQL text in spans only in Development — query text can
                // contain PII (emails, names) and must not leak into prod traces.
                o.SetDbStatementForText = builder.Environment.IsDevelopment();
                o.RecordException = true;
            });

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(opt =>
            {
                opt.Endpoint = new Uri(otlpEndpoint);
            });
        }
    });

builder.Services.AddHealthChecks()
    .AddSqlServer(builder.Configuration.GetConnectionString("OltpConnection")!,
                  name: "oltp", tags: new[] { "db", "oltp" })
    .AddSqlServer(builder.Configuration.GetConnectionString("OlapConnection")!,
                  name: "olap", tags: new[] { "db", "olap" })
    // These report Degraded (not Unhealthy) so /health stays 200 for the container
    // probe; job/ETL outages still surface in the health payload.
    .AddCheck<HangfireHealthCheck>("hangfire", tags: new[] { "jobs" })
    .AddCheck<EtlFreshnessHealthCheck>("etl-freshness", tags: new[] { "etl" });

var app = builder.Build();

// ---- Auto-init: ensure DBs + migrate OLTP + apply OLAP schema + seed ----
using (var scope = app.Services.CreateScope())
{
    var init = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await init.InitializeAsync();
}

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    // Dev: open. Prod: HTTP Basic auth (Hangfire:DashboardUser/Password) or deny.
    Authorization = new[] { new HangfireDashboardAuthFilter(app.Environment, app.Configuration) }
});
ECommerPipeline.Infrastructure.DependencyInjection.RegisterRecurringJobs(app.Services);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(opt => opt
        .WithTitle("ECommerPipeline API")
        .WithTheme(ScalarTheme.BluePlanet)
        .WithDefaultHttpClient(ScalarTarget.Http, ScalarClient.Http11));
}

// Correlation id must run early so every later log line carries it.
app.UseMiddleware<CorrelationIdMiddleware>();

// Baseline security headers on every response (clickjacking / MIME-sniff / etc.).
app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.UseExceptionHandler();
app.UseSerilogRequestLogging(opts =>
{
    // Demote 499 (client cancelled) from ERR to Debug — it's not a server fault.
    opts.GetLevel = (httpCtx, _, ex) =>
    {
        if (ex is OperationCanceledException || httpCtx.Response.StatusCode == 499)
            return Serilog.Events.LogEventLevel.Debug;
        return httpCtx.Response.StatusCode >= 500
            ? Serilog.Events.LogEventLevel.Error
            : Serilog.Events.LogEventLevel.Information;
    };

    // Enrich the request-completion log with useful context
    opts.EnrichDiagnosticContext = (diagnosticContext, httpCtx) =>
    {
        diagnosticContext.Set("RequestHost", httpCtx.Request.Host.Value);
        diagnosticContext.Set("UserAgent", httpCtx.Request.Headers.UserAgent.ToString());
        if (httpCtx.Items.TryGetValue("X-Correlation-ID", out var cid))
            diagnosticContext.Set("CorrelationId", cid?.ToString());
    };
});
app.UseCors("Frontend");

// Serve the built React SPA from wwwroot when present (single-deployment mode,
// e.g. Azure App Service serving both API + frontend). Locally / in Docker the
// frontend is served by Vite / nginx and wwwroot is empty, so these are no-ops.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint();   // GET /metrics — Prometheus scrape target
app.MapHub<EtlNotificationHub>("/hub/etl");
app.MapHub<NotificationHub>("/hub/notifications");

// ============================================================
//  Auth — register / login / refresh / me
// ============================================================
app.MapPost("/api/auth/register", async (
        RegisterRequest req,
        IValidator<RegisterRequest> validator,
        IAuthService auth,
        CancellationToken ct) =>
{
    var result = await validator.ValidateAsync(req, ct);
    if (!result.IsValid) return Results.ValidationProblem(result.ToDictionary());
    return Results.Ok(await auth.RegisterAsync(req, ct));
}).RequireRateLimiting("auth").WithTags("Auth");

app.MapPost("/api/auth/login", async (
        LoginRequest req,
        IValidator<LoginRequest> validator,
        IAuthService auth,
        CancellationToken ct) =>
{
    var result = await validator.ValidateAsync(req, ct);
    if (!result.IsValid) return Results.ValidationProblem(result.ToDictionary());
    try { return Results.Ok(await auth.LoginAsync(req, ct)); }
    catch (UnauthorizedAccessException ex) { return Results.Problem(ex.Message, statusCode: 401); }
}).RequireRateLimiting("auth").WithTags("Auth");

app.MapPost("/api/auth/refresh", async (RefreshRequest req, IAuthService auth, CancellationToken ct) =>
{
    try { return Results.Ok(await auth.RefreshAsync(req.RefreshToken, ct)); }
    catch (UnauthorizedAccessException ex) { return Results.Problem(ex.Message, statusCode: 401); }
}).WithTags("Auth");

app.MapPost("/api/auth/logout", async (RefreshRequest req, IAuthService auth, CancellationToken ct) =>
{
    await auth.RevokeAsync(req.RefreshToken, ct);
    return Results.Ok(new { status = "logged-out" });
}).WithTags("Auth");

app.MapGet("/api/auth/me", async (ClaimsPrincipal user, IAuthService auth, CancellationToken ct) =>
{
    var idStr = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!long.TryParse(idStr, out var id)) return Results.Unauthorized();
    var me = await auth.GetCurrentUserAsync(id, ct);
    return me is null ? Results.NotFound() : Results.Ok(me);
}).RequireAuthorization().WithTags("Auth");

// ============================================================
//  OLTP — write path (fast order ingestion)
// ============================================================
app.MapPost("/api/orders", async (
        CreateOrderRequest req,
        ClaimsPrincipal user,
        IValidator<CreateOrderRequest> validator,
        IOrderService svc,
        CancellationToken ct) =>
{
    var result = await validator.ValidateAsync(req, ct);
    if (!result.IsValid)
        return Results.ValidationProblem(result.ToDictionary());

    // Non-staff may only place orders billed to themselves (prevents IDOR where a
    // customer crafts an order against another customer's id). Staff/Admin (manual
    // order entry) keep the requested CustomerId.
    if (!user.IsInRole("Admin") && !user.IsInRole("Staff"))
    {
        var idStr = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idStr, out var uid)) return Results.Unauthorized();
        req = req with { CustomerId = uid };
    }

    return Results.Ok(await svc.CreateAsync(req, ct));
}).RequireAuthorization().WithTags("Orders");

app.MapGet("/api/orders", async (
        int? page,
        int? pageSize,
        OrderStatus? status,
        long? customerId,
        DateTime? from,
        DateTime? to,
        string? search,
        ClaimsPrincipal user,
        IOrderService svc,
        CancellationToken ct) =>
{
    // Non-staff can only ever list their OWN orders — force the filter to their id.
    var effectiveCustomerId = customerId;
    if (!user.IsInRole("Admin") && !user.IsInRole("Staff"))
    {
        var idStr = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idStr, out var uid)) return Results.Unauthorized();
        effectiveCustomerId = uid;
    }

    var query = new OrderQueryParams(
        Page:       page     ?? 1,
        PageSize:   pageSize ?? 20,
        Status:     status,
        CustomerId: effectiveCustomerId,
        From:       from,
        To:         to,
        Search:     search);
    return Results.Ok(await svc.GetPagedAsync(query, ct));
}).RequireAuthorization().WithTags("Orders");

app.MapGet("/api/orders/{id:long}", async (
        long id, ClaimsPrincipal user, IOrderService svc, CancellationToken ct) =>
{
    var detail = await svc.GetByIdAsync(id, ct);
    if (detail is null) return Results.NotFound();

    // Non-staff may only view their own order; return 404 (not 403) so we don't
    // leak whether someone else's order id exists.
    if (!user.IsInRole("Admin") && !user.IsInRole("Staff"))
    {
        var idStr = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(idStr, out var uid) || detail.CustomerId != uid)
            return Results.NotFound();
    }
    return Results.Ok(detail);
}).RequireAuthorization().WithTags("Orders");

// Staff/Admin advance an order through the fulfilment state machine
// (Pending→Confirmed→Shipped→Delivered, or →Cancelled). Invalid jumps are
// rejected by the service (400). Cancelling restocks the items.
app.MapPatch("/api/orders/{id:long}/status", async (
        long id,
        UpdateOrderStatusRequest req,
        ClaimsPrincipal user,
        IOrderService svc,
        CancellationToken ct) =>
{
    var idStr = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    long? actorId = long.TryParse(idStr, out var aid) ? aid : null;
    return Results.Ok(await svc.UpdateStatusAsync(id, req.Status, actorId, req.Reason, ct));
}).RequireAuthorization(p => p.RequireRole("Admin", "Staff")).WithTags("Orders");

// A customer cancels their own order while it is still Pending (restocks items).
app.MapPost("/api/orders/{id:long}/cancel", async (
        long id,
        ClaimsPrincipal user,
        IOrderService svc,
        CancellationToken ct) =>
{
    var idStr = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!long.TryParse(idStr, out var customerId)) return Results.Unauthorized();
    return Results.Ok(await svc.CancelByCustomerAsync(id, customerId, null, ct));
}).RequireAuthorization().WithTags("Orders");

// ============================================================
//  Payments — VNPay / MoMo (sandbox)
//  Flow: POST create → redirect to gateway → gateway returns the browser to our
//  /return (redirects to the SPA result page) AND server-to-server to /ipn (the
//  authoritative settlement). Both run through the same idempotent handler.
// ============================================================
static Dictionary<string, string> QueryToDict(IQueryCollection q) =>
    q.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

string PaymentRedirect(IOptions<PaymentOptions> opt, PaymentResultDto r)
{
    var b = opt.Value.FrontendResultUrl;
    var sep = b.Contains('?') ? "&" : "?";
    return $"{b}{sep}orderId={r.OrderId}&success={r.Success.ToString().ToLowerInvariant()}";
}

// Which online methods are configured — lets the storefront enable/disable
// the VNPay/MoMo radios instead of failing at create time.
app.MapGet("/api/payments/methods", (IOptions<PaymentOptions> opt) =>
    Results.Ok(new { vnpay = opt.Value.VnPay.IsConfigured, momo = opt.Value.Momo.IsConfigured }))
   .WithTags("Payments");

app.MapPost("/api/payments/{orderId:long}/create", async (
        long orderId,
        ClaimsPrincipal user,
        HttpContext http,
        IPaymentService svc,
        CancellationToken ct) =>
{
    var idStr = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!long.TryParse(idStr, out var customerId)) return Results.Unauthorized();
    var ip = http.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
    var res = await svc.CreateAsync(orderId, customerId, ip, ct);
    return Results.Ok(res);
}).RequireAuthorization().WithTags("Payments");

// VNPay browser return → process (idempotent) then bounce to the SPA result page.
app.MapGet("/api/payments/vnpay/return", async (
        HttpContext http, IPaymentService svc, IOptions<PaymentOptions> opt, CancellationToken ct) =>
{
    var result = await svc.HandleCallbackAsync(PaymentMethod.VnPay, QueryToDict(http.Request.Query), ct);
    return Results.Redirect(PaymentRedirect(opt, result));
}).WithTags("Payments");

// VNPay server-to-server IPN (authoritative). Must reply with VNPay's RspCode JSON.
app.MapGet("/api/payments/vnpay/ipn", async (
        HttpContext http, IPaymentService svc, CancellationToken ct) =>
{
    var result = await svc.HandleCallbackAsync(PaymentMethod.VnPay, QueryToDict(http.Request.Query), ct);
    var (rsp, msg) = result switch
    {
        { OrderId: null }                          => ("01", "Order not found"),
        { Message: "Invalid signature." }          => ("97", "Invalid signature"),
        { Message: "Amount mismatch." }            => ("04", "Invalid amount"),
        _                                          => ("00", "Confirm Success"),
    };
    return Results.Json(new { RspCode = rsp, Message = msg });
}).WithTags("Payments");

// MoMo browser return → process then bounce to the SPA result page.
app.MapGet("/api/payments/momo/return", async (
        HttpContext http, IPaymentService svc, IOptions<PaymentOptions> opt, CancellationToken ct) =>
{
    var result = await svc.HandleCallbackAsync(PaymentMethod.Momo, QueryToDict(http.Request.Query), ct);
    return Results.Redirect(PaymentRedirect(opt, result));
}).WithTags("Payments");

// MoMo IPN — JSON POST. Acknowledge with 204 (MoMo retries on non-2xx).
app.MapPost("/api/payments/momo/ipn", async (
        HttpContext http, IPaymentService svc, CancellationToken ct) =>
{
    using var doc = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
    var data = doc.RootElement.EnumerateObject()
        .ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String
            ? p.Value.GetString() ?? ""
            : p.Value.GetRawText());
    await svc.HandleCallbackAsync(PaymentMethod.Momo, data, ct);
    return Results.NoContent();
}).WithTags("Payments");

// ============================================================
//  Customers — lookup for order creation UI
// ============================================================
app.MapGet("/api/customers", async (
        string? search, int? page, int? pageSize,
        ICustomerService svc, CancellationToken ct) =>
    Results.Ok(await svc.SearchAsync(search, page ?? 1, pageSize ?? 50, ct)))
   .WithTags("Customers");

// ============================================================
//  Products — lookup for order creation UI
// ============================================================
app.MapGet("/api/products", async (
        string? search, string? category, int? page, int? pageSize,
        IProductService svc, CancellationToken ct) =>
    Results.Ok(await svc.SearchAsync(search, category, page ?? 1, pageSize ?? 50, ct)))
   .WithTags("Products");

app.MapGet("/api/products/categories", async (IProductService svc, CancellationToken ct) =>
    Results.Ok(await svc.GetCategoriesAsync(ct)))
   .WithTags("Products");

// ---- Admin catalog management (Staff/Admin) ----
app.MapPost("/api/products", async (
        CreateProductRequest req, IValidator<CreateProductRequest> validator,
        IProductService svc, CancellationToken ct) =>
{
    var r = await validator.ValidateAsync(req, ct);
    if (!r.IsValid) return Results.ValidationProblem(r.ToDictionary());
    return Results.Ok(await svc.CreateAsync(req, ct));
}).RequireAuthorization(p => p.RequireRole("Admin", "Staff")).WithTags("Products");

app.MapPut("/api/products/{id:long}", async (
        long id, UpdateProductRequest req, IValidator<UpdateProductRequest> validator,
        IProductService svc, CancellationToken ct) =>
{
    var r = await validator.ValidateAsync(req, ct);
    if (!r.IsValid) return Results.ValidationProblem(r.ToDictionary());
    return Results.Ok(await svc.UpdateAsync(id, req, ct));
}).RequireAuthorization(p => p.RequireRole("Admin", "Staff")).WithTags("Products");

app.MapDelete("/api/products/{id:long}", async (long id, IProductService svc, CancellationToken ct) =>
{
    await svc.DeleteAsync(id, ct);
    return Results.Ok(new { status = "deleted", id });
}).RequireAuthorization(p => p.RequireRole("Admin", "Staff")).WithTags("Products");

// ============================================================
//  OLAP — analytical read path (served from Columnstore)
// ============================================================
app.MapGet("/api/reports/sales-by-category",
    async (DateTime from, DateTime to, IReportService svc, CancellationToken ct) =>
        Results.Ok(await svc.GetSalesByCategoryAsync(from, to, ct)))
   .WithTags("Reports");

app.MapGet("/api/reports/sales-by-day",
    async (DateTime from, DateTime to, IReportService svc, CancellationToken ct) =>
        Results.Ok(await svc.GetSalesByDayAsync(from, to, ct)))
   .WithTags("Reports");

app.MapGet("/api/reports/top-products",
    async (DateTime from, DateTime to, int? top, IReportService svc, CancellationToken ct) =>
        Results.Ok(await svc.GetTopProductsAsync(from, to, top ?? 10, ct)))
   .WithTags("Reports");

// Phase 4 — business-state analytics (payment mix / funnel / inventory)
app.MapGet("/api/reports/sales-by-payment-method",
    async (IReportService svc, CancellationToken ct) =>
        Results.Ok(await svc.GetSalesByPaymentMethodAsync(ct)))
   .WithTags("Reports");

app.MapGet("/api/reports/order-funnel",
    async (IReportService svc, CancellationToken ct) =>
        Results.Ok(await svc.GetOrderFunnelAsync(ct)))
   .WithTags("Reports");

app.MapGet("/api/reports/low-stock",
    async (int? limit, IReportService svc, CancellationToken ct) =>
        Results.Ok(await svc.GetLowStockProductsAsync(limit ?? 50, ct)))
   .WithTags("Reports");

// ============================================================
//  AI Data Analyst — NL→SQL proxy (admin chat assistant)
//  Forwards the question to the internal ai-analyst service, which generates
//  AST-validated read-only SQL on the Gold layer and returns rows + summary.
//  Keeping it server-to-server means the analyst is never exposed to the
//  browser and inherits this API's JWT auth + correlation id.
// ============================================================
app.MapPost("/api/ask", async (
        AskRequest req,
        IHttpClientFactory factory,
        IDistributedCache cache,
        AiMetrics metrics,
        ILogger<Program> logger,
        HttpContext http,
        CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Question))
        return Results.BadRequest(new { error = "Question is required." });

    var user = http.User.FindFirstValue(ClaimTypes.Email)
               ?? http.User.FindFirstValue(ClaimTypes.Name)
               ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? "unknown";
    var includeSummary = req.IncludeSummary ?? true;
    var cacheKey = $"ask::{includeSummary}::{req.Question.Trim().ToLowerInvariant()}";

    // Cache hit — return without calling the LLM at all. (Distributed: Redis when
    // configured so cache is shared across API instances, else in-memory.)
    var cachedJson = await cache.GetStringAsync(cacheKey, ct);
    if (cachedJson is not null)
    {
        metrics.RecordCacheHit();
        logger.LogInformation("AI ask (cache hit) by {User}: {Question}", user, req.Question);
        return Results.Content(cachedJson, "application/json");
    }

    var sw = System.Diagnostics.Stopwatch.StartNew();
    var client = factory.CreateClient("analyst");
    try
    {
        var resp = await client.PostAsJsonAsync("/ask",
            new { question = req.Question, includeSummary }, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        sw.Stop();

        // Audit: who asked what, the outcome (Answered/Refused), and latency.
        var status = "unknown";
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("status", out var s))
                status = s.GetString() ?? "unknown";
        }
        catch { /* non-JSON error body — leave status unknown */ }

        // Metrics: Answered vs Refused (LLM outcomes) vs error (non-success HTTP).
        if (!resp.IsSuccessStatusCode)
            metrics.RecordError();
        else if (string.Equals(status, "Refused", StringComparison.OrdinalIgnoreCase))
            metrics.RecordRefused(sw.ElapsedMilliseconds);
        else
            metrics.RecordAnswered(sw.ElapsedMilliseconds);

        logger.LogInformation(
            "AI ask by {User}: {Question} -> {Status} in {ElapsedMs}ms",
            user, req.Question, status, sw.ElapsedMilliseconds);

        // Cache only successful answers (never cache a transient failure).
        if (resp.IsSuccessStatusCode)
            await cache.SetStringAsync(cacheKey, json,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) }, ct);

        return Results.Content(json, "application/json", statusCode: (int)resp.StatusCode);
    }
    catch (HttpRequestException ex)
    {
        metrics.RecordError();
        logger.LogError(ex, "AI Analyst service unreachable (asked by {User})", user);
        return Results.Problem(
            "AI Analyst service is unavailable. Ensure the analyst-api service is running.",
            statusCode: 503);
    }
})
.RequireAuthorization(p => p.RequireRole("Admin", "Staff"))
.RequireRateLimiting("ai-ask")
.WithTags("AI Analyst");

// AI usage metrics (refusal rate, cache-hit rate, avg LLM latency). Admin only.
app.MapGet("/api/admin/ai-metrics", (AiMetrics metrics) => Results.Ok(metrics.Snapshot()))
   .RequireAuthorization(p => p.RequireRole("Admin"))
   .WithTags("AI Analyst");

// ============================================================
//  Admin — dev helpers (trigger ETL ngay, reset data)
// ============================================================
app.MapPost("/api/admin/trigger-etl", (IBackgroundJobClient jobs) =>
{
    var jobId = jobs.Enqueue<ECommerPipeline.Infrastructure.Etl.EtlJob>(
        j => j.RunAsync(CancellationToken.None));
    return Results.Accepted(value: new
    {
        status   = "etl-enqueued",
        jobId,
        dashboard = "/hangfire",
        at       = DateTime.UtcNow
    });
}).RequireAuthorization(p => p.RequireRole("Admin", "Staff")).WithTags("Admin");

app.MapPost("/api/admin/compress-columnstore", (IBackgroundJobClient jobs) =>
{
    var jobId = jobs.Enqueue<ECommerPipeline.Infrastructure.Etl.CompressColumnstoreJob>(
        j => j.RunAsync(CancellationToken.None));
    return Results.Accepted(value: new
    {
        status   = "compress-enqueued",
        jobId,
        dashboard = "/hangfire",
        at       = DateTime.UtcNow
    });
}).RequireAuthorization(p => p.RequireRole("Admin", "Staff")).WithTags("Admin");

app.MapPost("/api/admin/data-quality", (IBackgroundJobClient jobs) =>
{
    var jobId = jobs.Enqueue<ECommerPipeline.Infrastructure.Etl.DataQualityJob>(
        j => j.RunAsync(CancellationToken.None));
    return Results.Accepted(value: new
    {
        status   = "dq-enqueued",
        jobId,
        dashboard = "/hangfire",
        at       = DateTime.UtcNow
    });
}).RequireAuthorization(p => p.RequireRole("Admin", "Staff")).WithTags("Admin");

app.MapPost("/api/admin/reset", async (ResetService reset, CancellationToken ct) =>
{
    await reset.ResetAsync(ct);
    return Results.Ok(new { status = "reset-completed", at = DateTime.UtcNow });
}).RequireAuthorization(p => p.RequireRole("Admin", "Staff")).WithTags("Admin");

// ============================================================
//  Excel Import — bulk-create customers / products / orders from .xlsx
// ============================================================
app.MapPost("/api/import/customers", async (IFormFile file, IImportService svc, CancellationToken ct) =>
{
    if (file is null || file.Length == 0) return Results.BadRequest("No file uploaded.");
    using var s = file.OpenReadStream();
    return Results.Ok(await svc.ImportCustomersAsync(s, ct));
}).DisableAntiforgery().WithTags("Import");

app.MapPost("/api/import/products", async (IFormFile file, IImportService svc, CancellationToken ct) =>
{
    if (file is null || file.Length == 0) return Results.BadRequest("No file uploaded.");
    using var s = file.OpenReadStream();
    return Results.Ok(await svc.ImportProductsAsync(s, ct));
}).DisableAntiforgery().WithTags("Import");

app.MapPost("/api/import/orders", async (IFormFile file, IImportService svc, CancellationToken ct) =>
{
    if (file is null || file.Length == 0) return Results.BadRequest("No file uploaded.");
    using var s = file.OpenReadStream();
    return Results.Ok(await svc.ImportOrdersAsync(s, ct));
}).DisableAntiforgery().WithTags("Import");

app.MapGet("/api/import/template/{kind}", async (string kind, IImportService svc, CancellationToken ct) =>
{
    if (!Enum.TryParse<ImportTemplate>(kind, ignoreCase: true, out var template))
        return Results.BadRequest($"Unknown template '{kind}'. Use customers/products/orders.");
    var bytes = await svc.GetTemplateAsync(template, ct);
    return Results.File(bytes,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        $"{template}-template.xlsx");
}).WithTags("Import");

// SPA fallback — any unmatched non-API route returns index.html so client-side
// routing (React Router) works on refresh/deep-link. Only active when wwwroot
// has an index.html (single-deployment mode). API routes above match first.
if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html")))
    app.MapFallbackToFile("index.html");

app.Run();

// Request body for the AI Analyst proxy endpoint (/api/ask).
public sealed record AskRequest(string Question, bool? IncludeSummary);
