using ECommerPipeline.Application.Common.DTOs;
using ECommerPipeline.Application.Products;
using ECommerPipeline.Application.Products.DTOs;
using ECommerPipeline.Infrastructure.Persistence.Oltp;
using Microsoft.EntityFrameworkCore;

namespace ECommerPipeline.Infrastructure.Products;

public class ProductService : IProductService
{
    private readonly OltpDbContext _db;
    public ProductService(OltpDbContext db) => _db = db;

    public async Task<PagedResult<ProductLookupDto>> SearchAsync(
        string? search, string? category = null, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        page     = page     <= 0 ? 1  : page;
        pageSize = pageSize <= 0 ? 50 : Math.Min(pageSize, 500);

        try
        {
            var q = _db.Products.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(p => p.Sku.Contains(s) || p.Name.Contains(s));
            }
            if (!string.IsNullOrWhiteSpace(category))
                q = q.Where(p => p.Category == category);

            var total = await q.CountAsync(ct);
            var items = await q
                .OrderBy(p => p.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new ProductLookupDto(p.Id, p.Sku, p.Name, p.Category, p.Brand, p.Price, p.StockQuantity))
                .ToListAsync(ct);

            return new PagedResult<ProductLookupDto>(items, page, pageSize, total);
        }
        catch (OperationCanceledException) { throw; } // ack cancellation for VS debugger
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _db.Products.AsNoTracking()
                .Select(p => p.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync(ct);
        }
        catch (OperationCanceledException) { throw; }
    }
}
