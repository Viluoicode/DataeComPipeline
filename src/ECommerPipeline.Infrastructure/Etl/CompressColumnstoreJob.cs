using Dapper;
using ECommerPipeline.Application.Common.Interfaces;
using Hangfire;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace ECommerPipeline.Infrastructure.Etl;

/// Nightly maintenance: detect uncompressed (OPEN/CLOSED) row groups
/// in fact.SalesOrderItem and force-compress them.
/// Why: SQL Server only auto-compresses when a row group reaches ~1M rows.
/// For a low-volume system this never happens → query stays slow against
/// the row-based delta store. Running REORGANIZE nightly keeps OLAP fast.
public class CompressColumnstoreJob
{
    private readonly IOlapConnectionFactory _olap;
    private readonly ILogger<CompressColumnstoreJob> _logger;

    public CompressColumnstoreJob(IOlapConnectionFactory olap, ILogger<CompressColumnstoreJob> logger)
    {
        _olap = olap;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        using var conn = (SqlConnection)_olap.CreateConnection();
        await conn.OpenAsync(ct);

        // 1. Diagnose: how many row groups are uncompressed?
        const string diagSql = @"
            SELECT
                SUM(CASE WHEN state_description = 'OPEN'       THEN 1 ELSE 0 END) AS OpenCount,
                SUM(CASE WHEN state_description = 'CLOSED'     THEN 1 ELSE 0 END) AS ClosedCount,
                SUM(CASE WHEN state_description = 'COMPRESSED' THEN 1 ELSE 0 END) AS CompressedCount,
                COUNT(*) AS TotalGroups,
                ISNULL(SUM(total_rows), 0) AS TotalRows
            FROM sys.column_store_row_groups
            WHERE object_id = OBJECT_ID('fact.SalesOrderItem');";

        var stats = await conn.QuerySingleAsync<RowGroupStats>(
            new CommandDefinition(diagSql, cancellationToken: ct));

        _logger.LogInformation(
            "Columnstore health: Open={Open}, Closed={Closed}, Compressed={Compressed}, TotalRows={Rows}",
            stats.OpenCount, stats.ClosedCount, stats.CompressedCount, stats.TotalRows);

        var uncompressed = stats.OpenCount + stats.ClosedCount;
        if (uncompressed == 0)
        {
            _logger.LogInformation("No uncompressed row groups. Skipping REORGANIZE.");
            return;
        }

        // 2. Force-compress all row groups.
        // REORGANIZE is online (no table lock) and idempotent.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Compressing {Count} uncompressed row groups...", uncompressed);

        await conn.ExecuteAsync(new CommandDefinition(@"
            ALTER INDEX CCI_SalesOrderItem ON fact.SalesOrderItem
                REORGANIZE WITH (COMPRESS_ALL_ROW_GROUPS = ON);",
            commandTimeout: 600,
            cancellationToken: ct));

        sw.Stop();

        // 3. Re-diagnose to confirm
        var after = await conn.QuerySingleAsync<RowGroupStats>(
            new CommandDefinition(diagSql, cancellationToken: ct));

        _logger.LogInformation(
            "Compression done in {Ms} ms. Now Open={Open}, Closed={Closed}, Compressed={Compressed}",
            sw.ElapsedMilliseconds, after.OpenCount, after.ClosedCount, after.CompressedCount);
    }

    private record RowGroupStats(int OpenCount, int ClosedCount, int CompressedCount, int TotalGroups, long TotalRows);
}
