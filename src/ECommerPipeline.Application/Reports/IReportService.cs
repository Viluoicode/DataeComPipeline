using ECommerPipeline.Application.Reports.DTOs;

namespace ECommerPipeline.Application.Reports;

public interface IReportService
{
    Task<IReadOnlyList<SalesByCategoryRow>> GetSalesByCategoryAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<IReadOnlyList<SalesByDayRow>> GetSalesByDayAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<IReadOnlyList<TopProductRow>> GetTopProductsAsync(DateTime from, DateTime to, int top = 10, CancellationToken ct = default);

    // ---- Phase 4: business-state analytics ----
    Task<IReadOnlyList<PaymentMethodSalesRow>> GetSalesByPaymentMethodAsync(CancellationToken ct = default);
    Task<IReadOnlyList<OrderFunnelRow>> GetOrderFunnelAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ProductInventoryRow>> GetLowStockProductsAsync(int limit = 50, CancellationToken ct = default);
}
