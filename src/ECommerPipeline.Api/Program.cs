using System.Security.Claims;
using System.Text;
using ECommerPipeline.Api.Hubs;
using ECommerPipeline.Api.Middleware;
using ECommerPipeline.Application.Auth;
using ECommerPipeline.Application.Auth.DTOs;
using ECommerPipeline.Application.Auth.Validators;
using ECommerPipeline.Application.Common.Interfaces;
using ECommerPipeline.Application.Customers;
using ECommerPipeline.Application.Import;
using ECommerPipeline.Application.Orders;
using ECommerPipeline.Application.Orders.DTOs;
using ECommerPipeline.Application.Products;
using ECommerPipeline.Application.Reports;
using ECommerPipeline.Domain.Enums;
using ECommerPipeline.Infrastructure;
using ECommerPipeline.Application.Orders.Validators;
using ECommerPipeline.Infrastructure.Initialization;
using FluentValidation;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) =>
{
    lc.ReadFrom.Configuration(ctx.Configuration)
      .Enrich.FromLogContext()
      .Enrich.WithProperty("Application", "ECommerPipeline.Api")
      .WriteTo.Console();

    var seqUrl = ctx.Configuration["Seq:Url"];
    if (!string.IsNullOrWhiteSpace(seqUrl))
        lc.WriteTo.Seq(seqUrl);
});

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.Services.AddSignalR();
builder.Services.AddSingleton<IEtlNotifier, SignalREtlNotifier>();

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

builder.Services.AddHealthChecks()
    .AddSqlServer(builder.Configuration.GetConnectionString("OltpConnection")!,
                  name: "oltp", tags: new[] { "db", "oltp" })
    .AddSqlServer(builder.Configuration.GetConnectionString("OlapConnection")!,
                  name: "olap", tags: new[] { "db", "olap" });

var app = builder.Build();

// ---- Auto-init: ensure DBs + migrate OLTP + apply OLAP schema + seed ----
using (var scope = app.Services.CreateScope())
{
    var init = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await init.InitializeAsync();
}

app.UseHangfireDashboard("/hangfire");
ECommerPipeline.Infrastructure.DependencyInjection.RegisterRecurringJobs(app.Services);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(opt => opt
        .WithTitle("ECommerPipeline API")
        .WithTheme(ScalarTheme.BluePlanet)
        .WithDefaultHttpClient(ScalarTarget.Http, ScalarClient.Http11));
}

app.UseAuthentication();
app.UseAuthorization();

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
});
app.UseCors("Frontend");

app.MapHealthChecks("/health");
app.MapHub<EtlNotificationHub>("/hub/etl");

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
}).WithTags("Auth");

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
}).WithTags("Auth");

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
        IValidator<CreateOrderRequest> validator,
        IOrderService svc,
        CancellationToken ct) =>
{
    var result = await validator.ValidateAsync(req, ct);
    if (!result.IsValid)
        return Results.ValidationProblem(result.ToDictionary());

    return Results.Ok(await svc.CreateAsync(req, ct));
}).WithTags("Orders");

app.MapGet("/api/orders", async (
        int? page,
        int? pageSize,
        OrderStatus? status,
        long? customerId,
        DateTime? from,
        DateTime? to,
        string? search,
        IOrderService svc,
        CancellationToken ct) =>
{
    var query = new OrderQueryParams(
        Page:       page     ?? 1,
        PageSize:   pageSize ?? 20,
        Status:     status,
        CustomerId: customerId,
        From:       from,
        To:         to,
        Search:     search);
    return Results.Ok(await svc.GetPagedAsync(query, ct));
}).WithTags("Orders");

app.MapGet("/api/orders/{id:long}", async (long id, IOrderService svc, CancellationToken ct) =>
{
    var detail = await svc.GetByIdAsync(id, ct);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
}).WithTags("Orders");

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

app.Run();
