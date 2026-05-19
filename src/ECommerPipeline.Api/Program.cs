using ECommerPipeline.Api.Hubs;
using ECommerPipeline.Api.Middleware;
using ECommerPipeline.Application.Common.Interfaces;
using ECommerPipeline.Application.Orders;
using ECommerPipeline.Application.Orders.DTOs;
using ECommerPipeline.Application.Reports;
using ECommerPipeline.Infrastructure;
using ECommerPipeline.Application.Orders.Validators;
using ECommerPipeline.Infrastructure.Initialization;
using FluentValidation;
using Hangfire;
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

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(
            "http://localhost:5173",  // Vite dev server
            "http://localhost:4173")  // Vite preview server
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

app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseCors("Frontend");

app.MapHealthChecks("/health");
app.MapHub<EtlNotificationHub>("/hub/etl");

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
}).WithTags("Admin");

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
}).WithTags("Admin");

app.MapPost("/api/admin/reset", async (ResetService reset, CancellationToken ct) =>
{
    await reset.ResetAsync(ct);
    return Results.Ok(new { status = "reset-completed", at = DateTime.UtcNow });
}).WithTags("Admin");

app.Run();
