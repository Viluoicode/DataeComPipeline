using ECommerPipeline.Application.Common.DTOs;
using ECommerPipeline.Application.Products.DTOs;

namespace ECommerPipeline.Application.Products;

public interface IProductService
{
    Task<PagedResult<ProductLookupDto>> SearchAsync(string? search, string? category = null, int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken ct = default);

    // ---- Phase 8: admin catalog management ----
    Task<ProductLookupDto> CreateAsync(CreateProductRequest request, CancellationToken ct = default);
    Task<ProductLookupDto> UpdateAsync(long id, UpdateProductRequest request, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}
