using ECommerPipeline.Application.Common.DTOs;
using ECommerPipeline.Application.Products.DTOs;

namespace ECommerPipeline.Application.Products;

public interface IProductService
{
    Task<PagedResult<ProductLookupDto>> SearchAsync(string? search, string? category = null, int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken ct = default);
}
