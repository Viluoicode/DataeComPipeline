using System.Data;
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
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var conn = (SqlConnection)_olap.CreateConnection();
        await conn.OpenAsync(ct);

        var watermark = await GetWatermarkAsync(conn, ct);
        _logger.LogInformation("ETL start. Last processed OrderItemId={Last}", watermark);

        await UpsertDimensionsAsync(conn, ct);
        var keyLookup = await LoadKeyLookupsAsync(conn, ct);

        var totalProcessed = 0;
        while (!ct.IsCancellationRequested)
        {
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
                using (var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx)
                {
                    DestinationTableName = "fact.SalesOrderItem",
                    BatchSize = 1000,
                    BulkCopyTimeout = 120
                })
                {
                    foreach (DataColumn col in fact.Columns)
                        bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
                    await bulk.WriteToServerAsync(fact, ct);
                }

                var maxId = rows.Max(r => r.Id);
                await UpdateWatermarkAsync(conn, tx, maxId, ct);
                await tx.CommitAsync(ct);

                watermark = maxId;
                totalProcessed += fact.Rows.Count;
                _logger.LogInformation("ETL batch loaded {Count} rows. Watermark={Max}", fact.Rows.Count, maxId);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        sw.Stop();
        _logger.LogInformation("ETL done. Total rows processed this run: {Total}", totalProcessed);

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
                City       NVARCHAR(100) NULL);", cancellationToken: ct));

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

        await conn.ExecuteAsync(new CommandDefinition(@"
            MERGE dim.Customer WITH (HOLDLOCK) AS t
            USING #StageCustomer AS s ON t.CustomerId = s.CustomerId
            WHEN MATCHED THEN UPDATE SET FullName=s.FullName, Email=s.Email, City=s.City
            WHEN NOT MATCHED THEN INSERT (CustomerId, FullName, Email, City)
                                  VALUES (s.CustomerId, s.FullName, s.Email, s.City);
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
                Brand     NVARCHAR(100) NULL);", cancellationToken: ct));

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

        await conn.ExecuteAsync(new CommandDefinition(@"
            MERGE dim.Product WITH (HOLDLOCK) AS t
            USING #StageProduct AS s ON t.ProductId = s.ProductId
            WHEN MATCHED THEN UPDATE SET Sku=s.Sku, Name=s.Name, Category=s.Category, Brand=s.Brand
            WHEN NOT MATCHED THEN INSERT (ProductId, Sku, Name, Category, Brand)
                                  VALUES (s.ProductId, s.Sku, s.Name, s.Category, s.Brand);
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
        var custs = (await conn.QueryAsync<(long CustomerId, int CustomerKey)>(
            new CommandDefinition("SELECT CustomerId, CustomerKey FROM dim.Customer", cancellationToken: ct))).ToList();
        var prods = (await conn.QueryAsync<(long ProductId, int ProductKey)>(
            new CommandDefinition("SELECT ProductId, ProductKey FROM dim.Product", cancellationToken: ct))).ToList();
        return new KeyLookup(
            custs.ToDictionary(x => x.CustomerId, x => x.CustomerKey),
            prods.ToDictionary(x => x.ProductId, x => x.ProductKey));
    }

    private record KeyLookup(Dictionary<long, int> Customers, Dictionary<long, int> Products);
}
