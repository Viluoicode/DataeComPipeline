using ECommerPipeline.Application.Auth;
using ECommerPipeline.Application.Common.Interfaces;
using ECommerPipeline.Application.Customers;
using ECommerPipeline.Application.Import;
using ECommerPipeline.Application.Orders;
using ECommerPipeline.Application.Products;
using ECommerPipeline.Application.Reports;
using ECommerPipeline.Infrastructure.Auth;
using ECommerPipeline.Infrastructure.Customers;
using ECommerPipeline.Infrastructure.Etl;
using ECommerPipeline.Infrastructure.Import;
using ECommerPipeline.Infrastructure.Initialization;
using ECommerPipeline.Infrastructure.Orders;
using ECommerPipeline.Infrastructure.Persistence.Olap;
using ECommerPipeline.Infrastructure.Persistence.Oltp;
using ECommerPipeline.Infrastructure.Products;
using ECommerPipeline.Infrastructure.Reports;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerPipeline.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        // OLTP — EF Core, write path
        // EnableRetryOnFailure: auto-retry on transient SQL errors (network blip,
        // pool exhaustion, deadlock retry). Without this, EF surfaces SqlException
        // immediately and the user sees a 500 on a transient issue.
        services.AddDbContext<OltpDbContext>(o =>
            o.UseSqlServer(config.GetConnectionString("OltpConnection"), sql =>
                sql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorNumbersToAdd: null)));
        services.AddScoped<IOltpDbContext>(sp => sp.GetRequiredService<OltpDbContext>());

        // OLAP — Dapper, read path
        services.Configure<OlapOptions>(opt =>
            opt.OlapConnection = config.GetConnectionString("OlapConnection")!);
        services.AddSingleton<IOlapConnectionFactory, OlapConnectionFactory>();

        // Services
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IImportService, ExcelImportService>();

        // Auth
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.SectionName));
        services.AddSingleton<JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();

        // ETL
        services.AddScoped<IEtlPipeline, SalesEtlPipeline>();
        services.AddScoped<EtlJob>();
        services.AddScoped<CompressColumnstoreJob>();
        services.AddScoped<DataQualityJob>();

        // Bootstrap + dev utilities
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<ResetService>();

        // Hangfire
        services.AddHangfire(cfg => cfg
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(config.GetConnectionString("HangfireConnection"),
                new SqlServerStorageOptions
                {
                    CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                    SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                    QueuePollInterval = TimeSpan.Zero,
                    UseRecommendedIsolationLevel = true,
                    DisableGlobalLocks = true
                }));
        services.AddHangfireServer();

        return services;
    }

    public static void RegisterRecurringJobs(IServiceProvider sp)
    {
        var manager = sp.GetRequiredService<IRecurringJobManager>();

        // ETL: every 5 minutes
        manager.AddOrUpdate<EtlJob>(
            "sales-etl",
            j => j.RunAsync(CancellationToken.None),
            "*/5 * * * *");

        // Columnstore maintenance: every night at 02:00 UTC
        manager.AddOrUpdate<CompressColumnstoreJob>(
            "compress-columnstore",
            j => j.RunAsync(CancellationToken.None),
            "0 2 * * *");

        // Data quality checks: every 15 minutes (right after ETL settles)
        manager.AddOrUpdate<DataQualityJob>(
            "data-quality",
            j => j.RunAsync(CancellationToken.None),
            "2-59/15 * * * *");  // minute 2, 17, 32, 47 — offset from ETL (which runs */5)
    }
}
