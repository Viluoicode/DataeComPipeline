using ECommerPipeline.Application.Common.Interfaces;
using ECommerPipeline.Application.Customers;
using ECommerPipeline.Application.Import;
using ECommerPipeline.Application.Orders;
using ECommerPipeline.Application.Products;
using ECommerPipeline.Application.Reports;
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
        services.AddDbContext<OltpDbContext>(o =>
            o.UseSqlServer(config.GetConnectionString("OltpConnection")));
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

        // ETL
        services.AddScoped<IEtlPipeline, SalesEtlPipeline>();
        services.AddScoped<EtlJob>();
        services.AddScoped<CompressColumnstoreJob>();

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
    }
}
