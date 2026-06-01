using System.Data;
using System.Diagnostics;
using Dapper;
using ECommerPipeline.Application.Common.Interfaces;
using ECommerPipeline.Infrastructure.Persistence.Oltp;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ECommerPipeline.Infrastructure.Etl;

/// Incremental ETL: OLTP OrderItems -> OLAP fact.SalesOrderItem.
/// Watermark-based extract (LastProcessedRowId) for idempotent, resumable runs.
public class SalesEtlPipeline : IEtlPipeline
{
    private const string PipelineName = "SalesOrderItem";
    private const int BatchSize = 5000;

    // OpenTelemetry ActivitySource — every batch creates a span we can see in Jaeger.
    // Source name matches what's registered in Program.cs (.AddSource("ECommerPipeline.Etl"))
    private static readonly ActivitySource ActivitySource = new("ECommerPipeline.Etl", "1.0.0");

    private readonly OltpDbContext _oltp;
    private readonly IOlapConnectionFactory _olap;
    private readonly IEtlNotifier _notifier;
    private readonly ILogger<SalesEtlPipeline> _logger;

    public SalesEtlPipeline(
        OltpDbContext oltp,
        IOlapConnectionFactory olap,
        IEtlNotifier notifier,
        ILogger<SalesEtlPipeline> logger)
    {
        _oltp = oltp;
        _olap = olap;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        // Root span for the whole ETL run — child spans (extract, transform, load,
        // gold refresh) will hang off this one in Jaeger.
        using var rootActivity = ActivitySource.StartActivity(
            "etl.sales.run", ActivityKind.Internal);
        rootActivity?.SetTag("pipeline.name", PipelineName);
        rootActivity?.SetTag("batch.size", BatchSize);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var conn = (SqlConnection)_olap.CreateConnection();
        await conn.OpenAsync(ct);

        var watermark = await GetWatermarkAsync(conn, ct);
        rootActivity?.SetTag("watermark.start", watermark);
        _logger.LogInformation("ETL start. Last processed OrderItemId={Last}", watermark);

        using (var dimActivity = ActivitySource.StartActivity("etl.dimensions.upsert"))
        {
            await UpsertDimensionsAsync(conn, ct);
        }

        var keyLookup = await LoadKeyLookupsAsync(conn, ct);

        var totalProcessed = 0;
        var batchNumber = 0;
        while (!ct.IsCancellationRequested)
        {
            batchNumber++;
            using var batchActivity = ActivitySource.StartActivity("etl.batch");
            batchActivity?.SetTag("batch.number", batchNumber);
            batchActivity?.SetTag("watermark.current", watermark);

            var rows = await _oltp.OrderItems
                .AsNoTracking()
                .Where(i => i.Id > watermark)
                .OrderBy(i => i.Id)
                .Take(BatchSize)
                .Select(i => new
                {
                    i.Id,
                    i.OrderId,
                    i.ProductId,
                    i.Quantity,
                    i.UnitPrice,
                    i.LineTotal,
                    OrderDate = i.Order.OrderDate,
                    CustomerId = i.Order.CustomerId
                })
                .ToListAsync(ct);

            if (rows.Count == 0) break;
            batchActivity?.SetTag("rows.extracted", rows.Count);

            // ============ BRONZE — raw landing ============
            var bronze = new DataTable();
            bronze.Columns.Add("OrderItemId", typeof(long));
            bronze.Columns.Add("OrderId",     typeof(long));
            bronze.Columns.Add("CustomerId",  typeof(long));
            bronze.Columns.Add("ProductId",   typeof(long));
            bronze.Columns.Add("OrderDate",   typeof(DateTime));
            bronze.Columns.Add("Quantity",    typeof(int));
            bronze.Columns.Add("UnitPrice",   typeof(decimal));
            bronze.Columns.Add("LineTotal",   typeof(decimal));

            foreach (var r in rows)
                bronze.Rows.Add(r.Id, r.OrderId, r.CustomerId, r.ProductId,
                                r.OrderDate, r.Quantity, r.UnitPrice, r.LineTotal);

            // ============ SILVER — fact table (existing logic) ============
            var fact = new DataTable();
            fact.Columns.Add("DateKey", typeof(int));
            fact.Columns.Add("CustomerKey", typeof(int));
            fact.Columns.Add("ProductKey", typeof(int));
            fact.Columns.Add("OrderId", typeof(long));
            fact.Columns.Add("OrderItemId", typeof(long));
            fact.Columns.Add("Quantity", typeof(int));
            fact.Columns.Add("UnitPrice", typeof(decimal));
            fact.Columns.Add("LineTotal", typeof(decimal));
            fact.Columns.Add("EtlLoadedAt", typeof(DateTime));

            var now = DateTime.UtcNow;
            foreach (var r in rows)
            {
                var dateKey = int.Parse(r.OrderDate.ToString("yyyyMMdd"));
                if (!keyLookup.Customers.TryGetValue(r.CustomerId, out var custKey)) continue;
                if (!keyLookup.Products.TryGetValue(r.ProductId, out var prodKey)) continue;

                fact.Rows.Add(dateKey, custKey, prodKey, r.OrderId, r.Id,
                              r.Quantity, r.UnitPrice, r.LineTotal, now);
            }

            using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            try
            {
                // Bronze first — idempotent (skip duplicates)
                await BulkLoadAsync(conn, tx, bronze, "bronze.OrderItem_Raw", ct);

                // Silver fact
                await BulkLoadAsync(conn, tx, fact, "fact.SalesOrderItem", ct);

                var maxId = rows.Max(r => r.Id);
                await UpdateWatermarkAsync(conn, tx, maxId, ct);
                await tx.CommitAsync(ct);

                watermark = maxId;
                totalProcessed += fact.Rows.Count;
                _logger.LogInformation("ETL batch: Bronze+Silver loaded {Count} rows. Watermark={Max}",
                    fact.Rows.Count, maxId);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        // ============ GOLD — refresh pre-aggregated tables ============
        if (totalProcessed > 0)
        {
            using var goldActivity = ActivitySource.StartActivity("etl.gold.refresh");
            var goldSw = System.Diagnostics.Stopwatch.StartNew();
            await RefreshGoldLayerAsync(conn, ct);
            goldActivity?.SetTag("duration.ms", goldSw.ElapsedMilliseconds);
            _logger.LogInformation("Gold layer refreshed in {Ms} ms", goldSw.ElapsedMilliseconds);
        }

        sw.Stop();
        rootActivity?.SetTag("rows.total", totalProcessed);
        rootActivity?.SetTag("watermark.end", watermark);
        rootActivity?.SetTag("duration.ms", sw.ElapsedMilliseconds);
        _logger.LogInformation("ETL done. Total rows processed: {Total}", totalProcessed);

        // Push real-time event to connected clients (e.g. the React dashboard).
        await _notifier.NotifyEtlCompletedAsync(new EtlCompletedEvent(
            TotalRowsProcessed: totalProcessed,
            Watermark: watermark,
            CompletedAt: DateTime.UtcNow,
            DurationMs: sw.ElapsedMilliseconds), ct);
    }

    private static async Task<long> GetWatermarkAsync(SqlConnection conn, CancellationToken ct)
    {
        const string sql = @"
            SELECT LastProcessedRowId
            FROM   etl.Watermark
            WHERE  PipelineName = @Name;";
        var val = await conn.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(sql, new { Name = PipelineName }, cancellationToken: ct));
        return val ?? 0L;
    }

    private static async Task UpdateWatermarkAsync(SqlConnection conn, SqlTransaction tx, long maxId, CancellationToken ct)
    {
        const string sql = @"
            MERGE etl.Watermark AS t
            USING (SELECT @Name AS PipelineName) AS s ON t.PipelineName = s.PipelineName
            WHEN MATCHED THEN UPDATE SET LastProcessedRowId = @MaxId,
                                         LastProcessedAt    = SYSUTCDATETIME(),
                                         UpdatedAt          = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (PipelineName, LastProcessedAt, LastProcessedRowId)
                                  VALUES (@Name, SYSUTCDATETIME(), @MaxId);";
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { Name = PipelineName, MaxId = maxId }, tx, cancellationToken: ct));
    }

    private async Task UpsertDimensionsAsync(SqlConnection conn, CancellationToken ct)
    {
        await BulkUpsertCustomersAsync(conn, ct);
        await BulkUpsertProductsAsync(conn, ct);
        await EnsureDateDimensionAsync(conn, ct);
    }

    private async Task BulkUpsertCustomersAsync(SqlConnection conn, CancellationToken ct)
    {
        var customers = await _oltp.Customers.AsNoTracking()
            .Select(c => new { c.Id, c.FullName, c.Email, c.City })
            .ToListAsync(ct);
        if (customers.Count == 0) return;

        await conn.ExecuteAsync(new CommandDefinition(@"
            CREATE TABLE #StageCustomer (
                CustomerId BIGINT NOT NULL,
                FullName   NVARCHAR(200) NOT NULL,
                Email      NVARCHAR(200) NOT NULL,
                City       NVARCHAR(100) NULL,
                RowHash    BINARY(32) NULL);", cancellationToken: ct));

        var table = new DataTable();
        table.Columns.Add("CustomerId", typeof(long));
        table.Columns.Add("FullName",   typeof(string));
        table.Columns.Add("Email",      typeof(string));
        table.Columns.Add("City",       typeof(string));
        foreach (var c in customers)
            table.Rows.Add(c.Id, c.FullName, c.Email, (object?)c.City ?? DBNull.Value);

        using (var bulk = new SqlBulkCopy(conn) { DestinationTableName = "#StageCustomer", BatchSize = 5000 })
        {
            foreach (DataColumn col in table.Columns)
                bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
            await bulk.WriteToServerAsync(table, ct);
        }

        // SCD Type 2 logic:
        //   1. Compute SHA256 hash of tracked columns to detect change cheaply
        //   2. For each customer in staging:
        //      - If no current version → INSERT version 1
        //      - If current version has SAME hash → no-op
        //      - If current version has DIFFERENT hash → close old (set ValidTo + IsCurrent=0)
        //                                                + insert new version
        // This preserves history: dashboards for past periods show the customer
        // state AT THAT TIME, not the latest state.
        await conn.ExecuteAsync(new CommandDefinition(@"
            -- Compute hash on staging (RowHash column already exists from CREATE TABLE)
            UPDATE #StageCustomer
            SET RowHash = HASHBYTES('SHA2_256',
                CONCAT(FullName, '|', Email, '|', ISNULL(City, '')));

            DECLARE @Now DATETIME2 = SYSUTCDATETIME();

            -- Step 1: Close current versions that changed
            UPDATE t
            SET    ValidTo = @Now, IsCurrent = 0
            FROM   dim.Customer t WITH (HOLDLOCK)
            JOIN   #StageCustomer s ON s.CustomerId = t.CustomerId
            WHERE  t.IsCurrent = 1
              AND  t.RowHash <> s.RowHash;

            -- Step 2: Insert new versions (both new customers AND changed ones)
            INSERT INTO dim.Customer
                (CustomerId, FullName, Email, City,
                 ValidFrom, ValidTo, IsCurrent, Version, RowHash)
            SELECT  s.CustomerId, s.FullName, s.Email, s.City,
                    @Now, NULL, 1,
                    ISNULL((SELECT MAX(Version) FROM dim.Customer
                            WHERE CustomerId = s.CustomerId), 0) + 1,
                    s.RowHash
            FROM    #StageCustomer s
            WHERE   NOT EXISTS (
                SELECT 1 FROM dim.Customer t
                WHERE  t.CustomerId = s.CustomerId
                  AND  t.IsCurrent = 1
                  AND  t.RowHash = s.RowHash
            );

            DROP TABLE #StageCustomer;", cancellationToken: ct));
    }

    private async Task BulkUpsertProductsAsync(SqlConnection conn, CancellationToken ct)
    {
        var products = await _oltp.Products.AsNoTracking()
            .Select(p => new { p.Id, p.Sku, p.Name, p.Category, p.Brand })
            .ToListAsync(ct);
        if (products.Count == 0) return;

        await conn.ExecuteAsync(new CommandDefinition(@"
            CREATE TABLE #StageProduct (
                ProductId BIGINT NOT NULL,
                Sku       VARCHAR(50) NOT NULL,
                Name      NVARCHAR(300) NOT NULL,
                Category  NVARCHAR(100) NOT NULL,
                Brand     NVARCHAR(100) NULL,
                RowHash   BINARY(32) NULL);", cancellationToken: ct));

        var table = new DataTable();
        table.Columns.Add("ProductId", typeof(long));
        table.Columns.Add("Sku",       typeof(string));
        table.Columns.Add("Name",      typeof(string));
        table.Columns.Add("Category",  typeof(string));
        table.Columns.Add("Brand",     typeof(string));
        foreach (var p in products)
            table.Rows.Add(p.Id, p.Sku, p.Name, p.Category, (object?)p.Brand ?? DBNull.Value);

        using (var bulk = new SqlBulkCopy(conn) { DestinationTableName = "#StageProduct", BatchSize = 5000 })
        {
            foreach (DataColumn col in table.Columns)
                bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
            await bulk.WriteToServerAsync(table, ct);
        }

        // SCD Type 2 — same pattern as Customer (RowHash column already exists from CREATE TABLE)
        await conn.ExecuteAsync(new CommandDefinition(@"
            UPDATE #StageProduct
            SET RowHash = HASHBYTES('SHA2_256',
                CONCAT(Sku, '|', Name, '|', Category, '|', ISNULL(Brand, '')));

            DECLARE @Now DATETIME2 = SYSUTCDATETIME();

            -- Close changed current versions
            UPDATE t
            SET    ValidTo = @Now, IsCurrent = 0
            FROM   dim.Product t WITH (HOLDLOCK)
            JOIN   #StageProduct s ON s.ProductId = t.ProductId
            WHERE  t.IsCurrent = 1
              AND  t.RowHash <> s.RowHash;

            -- Insert new versions (new + changed)
            INSERT INTO dim.Product
                (ProductId, Sku, Name, Category, Brand,
                 ValidFrom, ValidTo, IsCurrent, Version, RowHash)
            SELECT  s.ProductId, s.Sku, s.Name, s.Category, s.Brand,
                    @Now, NULL, 1,
                    ISNULL((SELECT MAX(Version) FROM dim.Product
                            WHERE ProductId = s.ProductId), 0) + 1,
                    s.RowHash
            FROM    #StageProduct s
            WHERE   NOT EXISTS (
                SELECT 1 FROM dim.Product t
                WHERE  t.ProductId = s.ProductId
                  AND  t.IsCurrent = 1
                  AND  t.RowHash = s.RowHash
            );

            DROP TABLE #StageProduct;", cancellationToken: ct));
    }

    private async Task EnsureDateDimensionAsync(SqlConnection conn, CancellationToken ct)
    {
        // Pre-populate a generous calendar window: 2 years back, 1 year ahead.
        // Idempotent — only inserts dates that don't already exist.
        const string sql = @"
            ;WITH calendar AS (
                SELECT @Start AS d
                UNION ALL
                SELECT DATEADD(DAY, 1, d) FROM calendar WHERE d < @End
            )
            INSERT INTO dim.Date(DateKey,[Date],[Year],[Quarter],[Month],MonthName,[Day],DayOfWeek,IsWeekend)
            SELECT CAST(CONVERT(VARCHAR(8), c.d, 112) AS INT),
                   c.d, YEAR(c.d), DATEPART(QUARTER,c.d), MONTH(c.d), DATENAME(MONTH,c.d),
                   DAY(c.d), DATEPART(WEEKDAY,c.d),
                   CASE WHEN DATEPART(WEEKDAY,c.d) IN (1,7) THEN 1 ELSE 0 END
            FROM   calendar c
            WHERE  NOT EXISTS (SELECT 1 FROM dim.Date d WHERE d.[Date] = c.d)
            OPTION (MAXRECURSION 0);";

        var today = DateTime.UtcNow.Date;
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            Start = today.AddYears(-2),
            End   = today.AddYears(1)
        }, cancellationToken: ct));
    }

    private static async Task<KeyLookup> LoadKeyLookupsAsync(SqlConnection conn, CancellationToken ct)
    {
        // SCD Type 2: only fetch CURRENT versions. Fact rows reference the
        // surrogate key that was current at load time — that's intentional.
        var custs = (await conn.QueryAsync<(long CustomerId, int CustomerKey)>(
            new CommandDefinition(
                "SELECT CustomerId, CustomerKey FROM dim.Customer WHERE IsCurrent = 1",
                cancellationToken: ct))).ToList();
        var prods = (await conn.QueryAsync<(long ProductId, int ProductKey)>(
            new CommandDefinition(
                "SELECT ProductId, ProductKey FROM dim.Product WHERE IsCurrent = 1",
                cancellationToken: ct))).ToList();
        return new KeyLookup(
            custs.ToDictionary(x => x.CustomerId, x => x.CustomerKey),
            prods.ToDictionary(x => x.ProductId, x => x.ProductKey));
    }

    private record KeyLookup(Dictionary<long, int> Customers, Dictionary<long, int> Products);

    // ============================================================
    //  Bulk load helper (Bronze + Silver fact use same pattern)
    // ============================================================
    private static async Task BulkLoadAsync(
        SqlConnection conn, SqlTransaction tx, DataTable data, string targetTable, CancellationToken ct)
    {
        if (data.Rows.Count == 0) return;

        using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx)
        {
            DestinationTableName = targetTable,
            BatchSize = 1000,
            BulkCopyTimeout = 120
        };
        foreach (DataColumn col in data.Columns)
            bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);

        // Bronze has UNIQUE on OrderItemId — duplicate inserts blow up.
        // ETL is idempotent already (watermark), but if a backfill re-runs
        // overlapping rows we don't want to crash. Skip dupes via NOT EXISTS pattern.
        if (targetTable.StartsWith("bronze.", StringComparison.OrdinalIgnoreCase))
        {
            // Stage table is created EXPLICITLY with only the columns we load.
            // (Can't `SELECT TOP 0 * INTO` from bronze — that copies BronzeKey IDENTITY
            //  + IngestedAt/SourceSystem which are NOT NULL but lose their DEFAULTs,
            //  so the bulk copy would fail inserting NULL into them.)
            var stagingTable = $"#stage_{Guid.NewGuid():N}";
            bulk.DestinationTableName = stagingTable;

            await conn.ExecuteAsync(new CommandDefinition($@"
                CREATE TABLE {stagingTable} (
                    OrderItemId BIGINT NOT NULL,
                    OrderId     BIGINT NOT NULL,
                    CustomerId  BIGINT NOT NULL,
                    ProductId   BIGINT NOT NULL,
                    OrderDate   DATETIME2 NOT NULL,
                    Quantity    INT NOT NULL,
                    UnitPrice   DECIMAL(18,2) NOT NULL,
                    LineTotal   DECIMAL(18,2) NOT NULL);", tx, cancellationToken: ct));

            await bulk.WriteToServerAsync(data, ct);

            // INSERT into bronze (BronzeKey IDENTITY + IngestedAt/SourceSystem DEFAULTs fill themselves)
            await conn.ExecuteAsync(new CommandDefinition($@"
                INSERT INTO {targetTable}
                    (OrderItemId, OrderId, CustomerId, ProductId, OrderDate, Quantity, UnitPrice, LineTotal)
                SELECT s.OrderItemId, s.OrderId, s.CustomerId, s.ProductId, s.OrderDate, s.Quantity, s.UnitPrice, s.LineTotal
                FROM   {stagingTable} s
                WHERE  NOT EXISTS (SELECT 1 FROM {targetTable} t WHERE t.OrderItemId = s.OrderItemId);
                DROP TABLE {stagingTable};", tx, cancellationToken: ct));
        }
        else
        {
            await bulk.WriteToServerAsync(data, ct);
        }
    }

    // ============================================================
    //  GOLD LAYER refresh — pre-aggregated tables for dashboards
    //  Strategy: truncate + repopulate. Cheaper than incremental for demo size.
    //  Production with >10M rows would do incremental refresh per partition.
    // ============================================================
    private async Task RefreshGoldLayerAsync(SqlConnection conn, CancellationToken ct)
    {
        const string sql = @"
            -- ===== gold.DailySalesByCategory =====
            TRUNCATE TABLE gold.DailySalesByCategory;
            INSERT INTO gold.DailySalesByCategory
                ([Date], Category, OrderCount, ItemCount, TotalRevenue, AvgOrderValue)
            SELECT
                d.[Date],
                p.Category,
                COUNT(DISTINCT f.OrderId)                          AS OrderCount,
                COUNT(*)                                            AS ItemCount,
                CAST(SUM(f.LineTotal) AS DECIMAL(18,2))            AS TotalRevenue,
                CAST(SUM(f.LineTotal) / NULLIF(COUNT(DISTINCT f.OrderId), 0)
                     AS DECIMAL(18,2))                              AS AvgOrderValue
            FROM   fact.SalesOrderItem f
            JOIN   dim.Date    d ON d.DateKey = f.DateKey
            JOIN   dim.Product p ON p.ProductKey = f.ProductKey
            GROUP BY d.[Date], p.Category;

            -- ===== gold.MonthlyTopProducts =====
            TRUNCATE TABLE gold.MonthlyTopProducts;
            ;WITH monthly AS (
                SELECT
                    d.[Year],
                    d.[Month],
                    p.ProductId,
                    p.Sku,
                    p.Name AS ProductName,
                    p.Category,
                    CAST(SUM(f.Quantity) AS BIGINT)       AS TotalQuantity,
                    CAST(SUM(f.LineTotal) AS DECIMAL(18,2)) AS TotalRevenue,
                    ROW_NUMBER() OVER (PARTITION BY d.[Year], d.[Month] ORDER BY SUM(f.LineTotal) DESC) AS Rnk
                FROM   fact.SalesOrderItem f
                JOIN   dim.Date    d ON d.DateKey = f.DateKey
                JOIN   dim.Product p ON p.ProductKey = f.ProductKey
                GROUP BY d.[Year], d.[Month], p.ProductId, p.Sku, p.Name, p.Category
            )
            INSERT INTO gold.MonthlyTopProducts
                ([Year], [Month], ProductId, Sku, ProductName, Category,
                 TotalQuantity, TotalRevenue, RankInMonth)
            SELECT [Year], [Month], ProductId, Sku, ProductName, Category,
                   TotalQuantity, TotalRevenue, CAST(Rnk AS INT)
            FROM   monthly
            WHERE  Rnk <= 50;  -- top 50 per month

            -- ===== gold.CustomerLifetimeValue =====
            TRUNCATE TABLE gold.CustomerLifetimeValue;
            INSERT INTO gold.CustomerLifetimeValue
                (CustomerId, FirstOrderDate, LastOrderDate, TotalOrders, TotalRevenue,
                 AvgOrderValue, DaysSinceLastOrder)
            SELECT
                c.CustomerId,
                MIN(d.[Date])                                       AS FirstOrderDate,
                MAX(d.[Date])                                       AS LastOrderDate,
                COUNT(DISTINCT f.OrderId)                           AS TotalOrders,
                CAST(SUM(f.LineTotal) AS DECIMAL(18,2))             AS TotalRevenue,
                CAST(SUM(f.LineTotal) / NULLIF(COUNT(DISTINCT f.OrderId), 0)
                     AS DECIMAL(18,2))                              AS AvgOrderValue,
                DATEDIFF(DAY, MAX(d.[Date]), CAST(SYSUTCDATETIME() AS DATE)) AS DaysSinceLastOrder
            FROM   fact.SalesOrderItem f
            JOIN   dim.Customer c ON c.CustomerKey = f.CustomerKey
            JOIN   dim.Date     d ON d.DateKey     = f.DateKey
            GROUP BY c.CustomerId;
        ";

        await conn.ExecuteAsync(new CommandDefinition(sql, commandTimeout: 300, cancellationToken: ct));
    }
}
