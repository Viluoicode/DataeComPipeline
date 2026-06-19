using System.Diagnostics;
using Dapper;
using ECommerPipeline.Application.Common.Interfaces;
using Hangfire;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace ECommerPipeline.Infrastructure.Etl;

/// <summary>
/// Post-ETL data quality checks. Inspired by Great Expectations / dbt tests.
/// Each test queries OLAP, compares to expected, persists result into dq.TestResults.
/// Failed CRITICAL tests trigger a SignalR alert so the dashboard can flag staleness.
///
/// Categories (industry-standard):
///   - Uniqueness:    primary keys / business keys are unique
///   - Integrity:     foreign keys resolve
///   - Freshness:     data is recent enough
///   - Completeness:  expected row counts, no NULL where mandatory
///   - Business:      domain-specific invariants (TotalAmount > 0, etc.)
/// </summary>
public class DataQualityJob
{
    private static readonly ActivitySource ActivitySource = new("ECommerPipeline.DataQuality", "1.0.0");

    private readonly IOlapConnectionFactory _olap;
    private readonly IEtlNotifier _notifier;
    private readonly ILogger<DataQualityJob> _logger;

    public DataQualityJob(
        IOlapConnectionFactory olap,
        IEtlNotifier notifier,
        ILogger<DataQualityJob> logger)
    {
        _olap = olap;
        _notifier = notifier;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        using var rootActivity = ActivitySource.StartActivity("dq.run");

        using var conn = (SqlConnection)_olap.CreateConnection();
        await conn.OpenAsync(ct);

        var tests = BuildTestSuite();
        rootActivity?.SetTag("test.count", tests.Count);
        var failed = 0;
        var critical = 0;

        foreach (var test in tests)
        {
            using var testActivity = ActivitySource.StartActivity($"dq.{test.Name}");
            testActivity?.SetTag("test.category", test.Category);
            testActivity?.SetTag("test.severity", test.Severity);

            try
            {
                var actual = await conn.ExecuteScalarAsync<long>(
                    new CommandDefinition(test.Sql, cancellationToken: ct));
                var pass = test.Predicate(actual);

                testActivity?.SetTag("test.actual", actual);
                testActivity?.SetTag("test.passed", pass);
                if (!pass)
                    testActivity?.SetStatus(ActivityStatusCode.Error, $"Expected {test.ExpectedDescription}, got {actual}");

                await PersistResultAsync(conn, test, actual, pass, message: null, ct);

                if (!pass)
                {
                    failed++;
                    if (test.Severity == "Critical") critical++;
                    _logger.LogWarning("DQ FAIL: {Test} — actual={Actual}, expected={Expected}",
                        test.Name, actual, test.ExpectedDescription);
                }
            }
            catch (Exception ex)
            {
                await PersistResultAsync(conn, test, actual: 0, pass: false, message: ex.Message, ct);
                _logger.LogError(ex, "DQ test errored: {Test}", test.Name);
                failed++;
                if (test.Severity == "Critical") critical++;
            }
        }

        _logger.LogInformation("DQ run done. {Total} tests, {Failed} failed ({Critical} critical)",
            tests.Count, failed, critical);

        // Alert dashboard if any critical test failed
        if (critical > 0)
        {
            await _notifier.NotifyDataQualityAlertAsync(new DataQualityAlertEvent(
                FailedCount: failed,
                CriticalCount: critical,
                AlertedAt: DateTime.UtcNow), ct);
        }
    }

    private static async Task PersistResultAsync(
        SqlConnection conn, DqTest test, long actual, bool pass, string? message, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO dq.TestResults
                (TestName, Category, Severity, Status, ActualValue, ExpectedValue, Message)
            VALUES (@Name, @Category, @Severity, @Status, @Actual, @Expected, @Message);";
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            test.Name,
            test.Category,
            test.Severity,
            Status = pass ? "Pass" : "Fail",
            Actual = actual.ToString(),
            Expected = test.ExpectedDescription,
            Message = message,
        }, cancellationToken: ct));
    }

    // ============================================================
    //  Test definitions
    //  Each test = single scalar query + a predicate to decide pass/fail
    // ============================================================
    private static List<DqTest> BuildTestSuite() => new()
    {
        // ---------- UNIQUENESS ----------
        new("fact_no_duplicate_orderitemid", "Uniqueness", "Critical",
            "0 duplicate OrderItemIds in fact",
            actual => actual == 0,
            @"SELECT COUNT(*) FROM (
                SELECT OrderItemId
                FROM   fact.SalesOrderItem
                GROUP BY OrderItemId
                HAVING COUNT(*) > 1
              ) d;"),

        new("dim_customer_one_current_per_id", "Uniqueness", "Critical",
            "0 customers with >1 IsCurrent=1 row",
            actual => actual == 0,
            @"SELECT COUNT(*) FROM (
                SELECT CustomerId
                FROM   dim.Customer
                WHERE  IsCurrent = 1
                GROUP BY CustomerId
                HAVING COUNT(*) > 1
              ) d;"),

        // ---------- INTEGRITY ----------
        new("fact_customerkey_resolves", "Integrity", "Critical",
            "0 fact rows with orphan CustomerKey",
            actual => actual == 0,
            @"SELECT COUNT(*)
              FROM   fact.SalesOrderItem f
              LEFT JOIN dim.Customer c ON c.CustomerKey = f.CustomerKey
              WHERE  c.CustomerKey IS NULL;"),

        new("fact_productkey_resolves", "Integrity", "Critical",
            "0 fact rows with orphan ProductKey",
            actual => actual == 0,
            @"SELECT COUNT(*)
              FROM   fact.SalesOrderItem f
              LEFT JOIN dim.Product p ON p.ProductKey = f.ProductKey
              WHERE  p.ProductKey IS NULL;"),

        new("fact_datekey_resolves", "Integrity", "Critical",
            "0 fact rows with orphan DateKey",
            actual => actual == 0,
            @"SELECT COUNT(*)
              FROM   fact.SalesOrderItem f
              LEFT JOIN dim.Date d ON d.DateKey = f.DateKey
              WHERE  d.DateKey IS NULL;"),

        // ---------- FRESHNESS ----------
        new("fact_freshness_24h", "Freshness", "Warning",
            "ETL loaded fact in last 24h",
            actual => actual >= 1,
            @"SELECT CASE WHEN EXISTS(
                  SELECT 1 FROM fact.SalesOrderItem
                  WHERE  EtlLoadedAt > DATEADD(HOUR, -24, SYSUTCDATETIME())
              ) THEN 1 ELSE 0 END;"),

        new("gold_freshness_2h", "Freshness", "Warning",
            "Gold layer refreshed in last 2h",
            actual => actual >= 1,
            @"SELECT CASE WHEN EXISTS(
                  SELECT 1 FROM gold.DailySalesByCategory
                  WHERE  RefreshedAt > DATEADD(HOUR, -2, SYSUTCDATETIME())
              ) THEN 1 ELSE 0 END;"),

        // ---------- COMPLETENESS ----------
        new("fact_row_count_nonzero", "Completeness", "Critical",
            ">0 rows in fact (sanity)",
            actual => actual > 0,
            "SELECT COUNT(*) FROM fact.SalesOrderItem;"),

        new("dim_customer_count_nonzero", "Completeness", "Critical",
            ">0 customers in dim",
            actual => actual > 0,
            "SELECT COUNT(*) FROM dim.Customer WHERE IsCurrent = 1;"),

        // ---------- BUSINESS RULES ----------
        new("fact_no_negative_revenue", "Business", "Critical",
            "0 fact rows with LineTotal <= 0",
            actual => actual == 0,
            "SELECT COUNT(*) FROM fact.SalesOrderItem WHERE LineTotal <= 0;"),

        new("fact_no_zero_quantity", "Business", "Critical",
            "0 fact rows with Quantity <= 0",
            actual => actual == 0,
            "SELECT COUNT(*) FROM fact.SalesOrderItem WHERE Quantity <= 0;"),

        new("fact_unitprice_lineTotal_consistency", "Business", "Warning",
            "0 fact rows where Quantity*UnitPrice ≠ LineTotal (rounded)",
            actual => actual == 0,
            @"SELECT COUNT(*) FROM fact.SalesOrderItem
              WHERE ABS(Quantity * UnitPrice - LineTotal) > 0.01;"),

        // ---------- BUSINESS RULES (Phase 4 — payment / inventory) ----------
        new("inventory_no_negative_stock", "Business", "Critical",
            "0 products with CurrentStock < 0 (oversell guard)",
            actual => actual == 0,
            "SELECT COUNT(*) FROM gold.ProductInventory WHERE CurrentStock < 0;"),

        new("inventory_low_stock_watch", "Business", "Warning",
            "0 products below the low-stock threshold (informational)",
            actual => actual == 0,
            "SELECT COUNT(*) FROM gold.ProductInventory WHERE LowStock = 1;"),

        new("payment_paid_not_exceed_total", "Business", "Critical",
            "0 payment methods where PaidRevenue > TotalRevenue",
            actual => actual == 0,
            "SELECT COUNT(*) FROM gold.SalesByPaymentMethod WHERE PaidRevenue > TotalRevenue;"),
    };

    private record DqTest(
        string Name,
        string Category,
        string Severity,
        string ExpectedDescription,
        Func<long, bool> Predicate,
        string Sql);
}
