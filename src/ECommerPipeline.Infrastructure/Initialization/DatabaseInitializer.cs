using System.Reflection;
using Bogus;
using Dapper;
using ECommerPipeline.Domain.Entities;
using ECommerPipeline.Domain.Enums;
using ECommerPipeline.Infrastructure.Persistence.Oltp;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ECommerPipeline.Infrastructure.Initialization;

/// One-shot bootstrap on app startup:
///  1. Ensure OLTP / OLAP / Hangfire databases exist
///  2. Apply EF migrations on OLTP
///  3. Apply OLAP star-schema script (idempotent — IF NOT EXISTS guards)
///  4. Seed demo customers + products if OLTP is empty
public class DatabaseInitializer
{
    private readonly OltpDbContext _oltp;
    private readonly IConfiguration _config;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(OltpDbContext oltp, IConfiguration config, ILogger<DatabaseInitializer> logger)
    {
        _oltp = oltp;
        _config = config;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // Azure SQL doesn't allow CREATE DATABASE from app code — the DB is
        // provisioned via the portal. Set Database:AutoCreate=false there.
        // Locally / Docker (default true) the app creates the 3 databases itself.
        if (_config.GetValue("Database:AutoCreate", true))
            await EnsureDatabasesExistAsync(ct);
        else
            _logger.LogInformation("Database:AutoCreate=false — skipping CREATE DATABASE (Azure SQL mode).");

        await ApplyOltpMigrationsAsync(ct);
        await ApplyOlapSchemaAsync(ct);
        await SeedAsync(ct);
        _logger.LogInformation("DatabaseInitializer: ready.");
    }

    private async Task EnsureDatabasesExistAsync(CancellationToken ct)
    {
        await EnsureDbAsync(_config.GetConnectionString("OltpConnection")!, ct);
        await EnsureDbAsync(_config.GetConnectionString("OlapConnection")!, ct);
        await EnsureDbAsync(_config.GetConnectionString("HangfireConnection")!, ct);
    }

    private async Task EnsureDbAsync(string connectionString, CancellationToken ct)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var dbName = builder.InitialCatalog;
        builder.InitialCatalog = "master";

        await using var conn = new SqlConnection(builder.ConnectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            $"IF DB_ID(@n) IS NULL CREATE DATABASE [{dbName}];",
            new { n = dbName }, cancellationToken: ct));
        _logger.LogInformation("DB ensured: {Db}", dbName);
    }

    private async Task ApplyOltpMigrationsAsync(CancellationToken ct)
    {
        await _oltp.Database.MigrateAsync(ct);
        _logger.LogInformation("OLTP migrations applied.");
    }

    private async Task ApplyOlapSchemaAsync(CancellationToken ct)
    {
        var asm = typeof(DatabaseInitializer).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("OlapSchema.sql", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("OlapSchema.sql resource not found.");

        using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var script = await reader.ReadToEndAsync(ct);

        await using var conn = new SqlConnection(_config.GetConnectionString("OlapConnection"));
        await conn.OpenAsync(ct);

        // Split on GO statements (none in our script, but safe for future-proofing)
        foreach (var batch in SplitOnGo(script))
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            await conn.ExecuteAsync(new CommandDefinition(batch, cancellationToken: ct));
        }
        _logger.LogInformation("OLAP schema applied.");
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        if (await _oltp.Customers.AnyAsync(ct))
        {
            _logger.LogInformation("Seed skipped — OLTP already has data.");
            return;
        }

        var customerCount = _config.GetValue("Seed:CustomerCount", 1000);
        var productCount  = _config.GetValue("Seed:ProductCount",  200);
        var orderCount    = _config.GetValue("Seed:OrderCount",    10_000);

        _logger.LogInformation("Seeding {C} customers, {P} products, {O} orders...",
            customerCount, productCount, orderCount);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        Randomizer.Seed = new Random(20260518);
        _oltp.ChangeTracker.AutoDetectChangesEnabled = false;

        // ---- Default admin + demo customer (with passwords for login) ----
        // Demo credentials are intentionally easy for portfolio reviewers.
        var adminPwd = BCrypt.Net.BCrypt.HashPassword("admin123", workFactor: 11);
        var demoPwd  = BCrypt.Net.BCrypt.HashPassword("demo123",  workFactor: 11);

        _oltp.Customers.AddRange(
            new Customer
            {
                FullName = "Admin User",
                Email    = "admin@ecom.com",
                Phone    = "0900000001",
                City     = "HCM",
                PasswordHash = adminPwd,
                Role     = Domain.Enums.UserRole.Admin,
            },
            new Customer
            {
                FullName = "Demo Customer",
                Email    = "demo@ecom.com",
                Phone    = "0900000002",
                City     = "HN",
                PasswordHash = demoPwd,
                Role     = Domain.Enums.UserRole.Customer,
            });
        await _oltp.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded admin@ecom.com / admin123 and demo@ecom.com / demo123");

        // ---- Customers ----
        var cityChoices = new[] { "HCM", "HN", "DN", "HP", "CT", "BMT", "VT", "NT" };
        var customerFaker = new Faker<Customer>()
            .RuleFor(c => c.FullName, f => f.Name.FullName())
            .RuleFor(c => c.Email,    (f, c) => f.Internet.Email(c.FullName.Split(' ')[0], uniqueSuffix: f.IndexFaker.ToString()))
            .RuleFor(c => c.Phone,    f => f.Phone.PhoneNumber("09########"))
            .RuleFor(c => c.City,     f => f.PickRandom(cityChoices));

        var customers = customerFaker.Generate(customerCount);
        _oltp.Customers.AddRange(customers);
        await _oltp.SaveChangesAsync(ct);

        // ---- Products ----
        var categories = new[] { "Electronics", "Footwear", "Apparel", "Home", "Books", "Sports", "Beauty", "Grocery" };
        var brands     = new[] { "Apple", "Samsung", "Nike", "Adidas", "Sony", "LG", "Uniqlo", "Local" };
        var productFaker = new Faker<Product>()
            .RuleFor(p => p.Sku,           f => $"SKU-{f.IndexFaker:D6}")
            .RuleFor(p => p.Name,          f => f.Commerce.ProductName())
            .RuleFor(p => p.Category,      f => f.PickRandom(categories))
            .RuleFor(p => p.Brand,         f => f.PickRandom(brands))
            .RuleFor(p => p.Price,         f => Math.Round((decimal)f.Random.Double(50_000, 60_000_000), 2))
            .RuleFor(p => p.StockQuantity, f => f.Random.Int(10, 1000));

        var products = productFaker.Generate(productCount);
        _oltp.Products.AddRange(products);
        await _oltp.SaveChangesAsync(ct);

        // ---- Orders (spread across last 90 days) ----
        var now = DateTime.UtcNow;
        var rng = new Random(42);

        const int batchSize = 500;
        for (var i = 0; i < orderCount; i += batchSize)
        {
            var batch = Math.Min(batchSize, orderCount - i);
            var orders = new List<Order>(batch);

            for (var k = 0; k < batch; k++)
            {
                var customer = customers[rng.Next(customers.Count)];
                var itemCount = rng.Next(1, 6); // 1..5 items
                var chosen = new HashSet<long>();
                var items = new List<OrderItem>(itemCount);

                for (var j = 0; j < itemCount; j++)
                {
                    Product p;
                    do { p = products[rng.Next(products.Count)]; } while (!chosen.Add(p.Id));
                    var qty = rng.Next(1, 5);
                    items.Add(new OrderItem
                    {
                        ProductId = p.Id,
                        Quantity  = qty,
                        UnitPrice = p.Price,
                        LineTotal = p.Price * qty
                    });
                }

                var orderDate = now.AddDays(-rng.Next(0, 90)).AddMinutes(-rng.Next(0, 1440));
                var totalIndex = i + k;

                var status = (OrderStatus)rng.Next(1, 5);   // Pending..Delivered

                // Realistic, internally-consistent payment distribution so the
                // Phase 4 payment/funnel analytics have meaningful data:
                //   ~50% COD, ~30% VNPay, ~20% MoMo.
                var pmRoll = rng.Next(100);
                var method = pmRoll < 50 ? PaymentMethod.Cod
                           : pmRoll < 80 ? PaymentMethod.VnPay
                           : PaymentMethod.Momo;
                PaymentStatus payStatus;
                if (method == PaymentMethod.Cod)
                    payStatus = status == OrderStatus.Delivered ? PaymentStatus.Paid : PaymentStatus.Unpaid;
                else // online orders that progressed past Pending must have been paid
                    payStatus = status >= OrderStatus.Confirmed ? PaymentStatus.Paid
                              : rng.Next(100) < 70 ? PaymentStatus.Paid
                              : rng.Next(2) == 0 ? PaymentStatus.Pending : PaymentStatus.Failed;

                orders.Add(new Order
                {
                    OrderNumber   = $"ORD-SEED-{totalIndex:D7}",
                    CustomerId    = customer.Id,
                    OrderDate     = orderDate,
                    Status        = status,
                    PaymentMethod = method,
                    PaymentStatus = payStatus,
                    Items         = items,
                    TotalAmount   = items.Sum(x => x.LineTotal)
                });
            }

            _oltp.Orders.AddRange(orders);
            await _oltp.SaveChangesAsync(ct);
            _oltp.ChangeTracker.Clear();
        }

        _oltp.ChangeTracker.AutoDetectChangesEnabled = true;
        sw.Stop();
        _logger.LogInformation("Seed done in {Ms} ms.", sw.ElapsedMilliseconds);
    }

    private static IEnumerable<string> SplitOnGo(string script) =>
        System.Text.RegularExpressions.Regex.Split(script, @"^\s*GO\s*$",
            System.Text.RegularExpressions.RegexOptions.Multiline |
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}
