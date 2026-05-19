using ECommerPipeline.Application.Reports.DTOs;

namespace ECommerPipeline.Application.Reports;

public interface IReportService
{
    Task<IReadOnlyList<SalesByCategoryRow>> GetSalesByCategoryAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<IReadOnlyList<SalesByDayRow>> GetSalesByDayAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<IReadOnlyList<TopProductRow>> GetTopProductsAsync(DateTime from, DateTime to, int top = 10, CancellationToken ct = default);
}
