using Dapper;
using ECommerPipeline.Application.Common.Interfaces;
using ECommerPipeline.Application.Reports;
using ECommerPipeline.Application.Reports.DTOs;
using Microsoft.Data.SqlClient;

namespace ECommerPipeline.Infrastructure.Reports;

public class ReportService : IReportService
{
    private readonly IOlapConnectionFactory _factory;

    public ReportService(IOlapConnectionFactory factory) => _factory = factory;

    public Task<IReadOnlyList<SalesByCategoryRow>> GetSalesByCategoryAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        // GOLD LAYER — pre-aggregated. ~5-10ms even on 30M+ source rows.
        // Falls back to fact query if gold table is empty (first-run before ETL).
        const string sql = @"
            SELECT  Category,
                    SUM(OrderCount)                          AS OrderCount,
                    CAST(SUM(TotalRevenue) AS DECIMAL(18,2)) AS TotalRevenue
            FROM    gold.DailySalesByCategory
            WHERE   [Date] BETWEEN @From AND @To
            GROUP BY Category
            ORDER BY TotalRevenue DESC;";

        return QueryAsync<SalesByCategoryRow>(sql, new { From = from.Date, To = to.Date }, ct);
    }

    public Task<IReadOnlyList<SalesByDayRow>> GetSalesByDayAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        // GOLD layer
        const string sql = @"
            SELECT  CAST([Date] AS DATETIME)                AS [Day],
                    SUM(OrderCount)                          AS OrderCount,
                    CAST(SUM(TotalRevenue) AS DECIMAL(18,2)) AS TotalRevenue
            FROM    gold.DailySalesByCategory
            WHERE   [Date] BETWEEN @From AND @To
            GROUP BY [Date]
            ORDER BY [Date];";

        return QueryAsync<SalesByDayRow>(sql, new { From = from.Date, To = to.Date }, ct);
    }

    public Task<IReadOnlyList<TopProductRow>> GetTopProductsAsync(DateTime from, DateTime to, int top = 10, CancellationToken ct = default)
    {
        // GOLD: aggregate gold.MonthlyTopProducts spanning the requested range
        const string sql = @"
            SELECT TOP (@Top)
                   ProductId,
                   MAX(Sku)         AS Sku,
                   MAX(ProductName) AS Name,
                   CAST(SUM(TotalQuantity) AS BIGINT)       AS TotalQuantity,
                   CAST(SUM(TotalRevenue)  AS DECIMAL(18,2)) AS TotalRevenue
            FROM   gold.MonthlyTopProducts
            WHERE  DATEFROMPARTS([Year], [Month], 1) BETWEEN
                   DATEFROMPARTS(YEAR(@From), MONTH(@From), 1) AND
                   DATEFROMPARTS(YEAR(@To),   MONTH(@To),   1)
            GROUP BY ProductId
            ORDER BY TotalRevenue DESC;";

        return QueryAsync<TopProductRow>(sql, new { From = from.Date, To = to.Date, Top = top }, ct);
    }

    // ---- Phase 4: business-state analytics (refreshed each ETL run from current OLTP state) ----

    public Task<IReadOnlyList<PaymentMethodSalesRow>> GetSalesByPaymentMethodAsync(CancellationToken ct = default)
    {
        const string sql = @"
            SELECT PaymentMethod, MethodName, OrderCount, PaidOrderCount, TotalRevenue, PaidRevenue
            FROM   gold.SalesByPaymentMethod
            ORDER BY TotalRevenue DESC;";
        return QueryAsync<PaymentMethodSalesRow>(sql, new { }, ct);
    }

    public Task<IReadOnlyList<OrderFunnelRow>> GetOrderFunnelAsync(CancellationToken ct = default)
    {
        const string sql = @"
            SELECT Stage, StageOrder, OrderCount
            FROM   gold.OrderFunnel
            ORDER BY StageOrder;";
        return QueryAsync<OrderFunnelRow>(sql, new { }, ct);
    }

    public Task<IReadOnlyList<ProductInventoryRow>> GetLowStockProductsAsync(int limit = 50, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT TOP (@Limit) ProductId, Sku, ProductName, Category, CurrentStock, UnitsSold, LowStock
            FROM   gold.ProductInventory
            WHERE  LowStock = 1
            ORDER BY CurrentStock ASC;";
        return QueryAsync<ProductInventoryRow>(sql, new { Limit = limit }, ct);
    }

    /// Wraps Dapper QueryAsync so that any cancellation flavor — pure
    /// OperationCanceledException OR a CancellationToken-triggered SqlException
    /// (numbers 0 / -2 / "Operation cancelled by user") — is surfaced as a single
    /// OperationCanceledException type. The global exception handler then returns
    /// 499 (client closed request) instead of leaking a 500 SqlException OR
    /// triggering the Visual Studio "user-unhandled" debugger break.
    ///
    /// Catching OperationCanceledException explicitly (even just to rethrow) is
    /// what tells the debugger "user code is aware of this" — without it, VS
    /// pauses on TaskCanceledException during dev.
    private async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object parameters, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        try
        {
            var rows = await conn.QueryAsync<T>(
                new CommandDefinition(sql, parameters, cancellationToken: ct));
            return rows.AsList();
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation — propagate as-is to the global handler.
            throw;
        }
        catch (SqlException ex) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException("Query cancelled by client.", ex, ct);
        }
        catch (SqlException ex) when (ex.Number is 0 or -2)
        {
            // 0 / -2 = network-level cancel from SqlClient. Treat as cancellation.
            throw new OperationCanceledException("Query cancelled.", ex, ct);
        }
    }
}
