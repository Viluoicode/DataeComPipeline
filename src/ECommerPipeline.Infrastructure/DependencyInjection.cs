using ECommerPipeline.Application.Auth;
using ECommerPipeline.Application.Common.Interfaces;
using ECommerPipeline.Application.Customers;
using ECommerPipeline.Application.Import;
using ECommerPipeline.Application.Orders;
using ECommerPipeline.Application.Payments;
using ECommerPipeline.Application.Products;
using ECommerPipeline.Application.Reports;
using ECommerPipeline.Infrastructure.Auth;
using ECommerPipeline.Infrastructure.Customers;
using ECommerPipeline.Infrastructure.Etl;
using ECommerPipeline.Infrastructure.Import;
using ECommerPipeline.Infrastructure.Initialization;
using ECommerPipeline.Infrastructure.Notifications;
using ECommerPipeline.Infrastructure.Orders;
using ECommerPipeline.Infrastructure.Payments;
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

        // Payments — provider-agnostic gateways resolved by PaymentMethod.
        services.Configure<PaymentOptions>(config.GetSection(PaymentOptions.SectionName));
        services.AddHttpClient(); // MoMo create is a server-to-server POST
        services.AddScoped<IPaymentGateway, VnPayGateway>();
        services.AddScoped<IPaymentGateway, MomoGateway>();
        services.AddScoped<IPaymentService, PaymentService>();

        // Auth
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.SectionName));
        services.AddSingleton<JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();

        // ETL
        services.AddScoped<IEtlPipeline, SalesEtlPipeline>();
        services.AddScoped<EtlJob>();
        services.AddScoped<CompressColumnstoreJob>();
        services.AddScoped<DataQualityJob>();

        // Notifications — transactional outbox dispatcher (email + in-app SignalR).
        // Email defaults to a log-only sender so the stack runs with zero config;
        // set Email:Provider=Smtp (MailHog/SendGrid) for real delivery.
        services.Configure<EmailOptions>(config.GetSection(EmailOptions.SectionName));
        if ((config["Email:Provider"] ?? "None").Equals("Smtp", StringComparison.OrdinalIgnoreCase))
            services.AddScoped<IEmailSender, SmtpEmailSender>();
        else
            services.AddScoped<IEmailSender, NoOpEmailSender>();
        services.AddScoped<OutboxDispatchJob>();

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
        var config  = sp.GetRequiredService<IConfiguration>();

        // Cron schedules are config-driven so constrained hosts (Azure F1 free,
        // 60 CPU-min/day) can run jobs sparsely. Defaults suit local/Docker.
        var etlCron      = config["Jobs:EtlCron"]      ?? "*/5 * * * *";       // every 5 min
        var compressCron = config["Jobs:CompressCron"] ?? "0 2 * * *";          // 2 AM daily
        var dqCron       = config["Jobs:DataQualityCron"] ?? "2-59/15 * * * *"; // every 15 min, offset
        var outboxCron   = config["Jobs:OutboxCron"]   ?? "* * * * *";          // every minute

        manager.AddOrUpdate<EtlJob>(
            "sales-etl", j => j.RunAsync(CancellationToken.None), etlCron);

        manager.AddOrUpdate<CompressColumnstoreJob>(
            "compress-columnstore", j => j.RunAsync(CancellationToken.None), compressCron);

        manager.AddOrUpdate<DataQualityJob>(
            "data-quality", j => j.RunAsync(CancellationToken.None), dqCron);

        manager.AddOrUpdate<OutboxDispatchJob>(
            "outbox-dispatch", j => j.RunAsync(CancellationToken.None), outboxCron);
    }
}
