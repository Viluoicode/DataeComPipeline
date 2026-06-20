using ECommerPipeline.Application.Common.DTOs;
using ECommerPipeline.Application.Products;
using ECommerPipeline.Application.Products.DTOs;
using ECommerPipeline.Domain.Entities;
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
            var raw = await q
                .OrderBy(p => p.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new { p.Id, p.Sku, p.Name, p.Category, p.Brand, p.Price, p.StockQuantity, p.ImageUrl })
                .ToListAsync(ct);

            // Build the serve URL in memory (avoids translating string interpolation in SQL).
            var items = raw.Select(p => new ProductLookupDto(
                p.Id, p.Sku, p.Name, p.Category, p.Brand, p.Price, p.StockQuantity,
                p.ImageUrl != null ? $"/api/products/{p.Id}/image" : null)).ToList();

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

    // ---- Phase 8: admin catalog management ----

    public async Task<ProductLookupDto> CreateAsync(CreateProductRequest r, CancellationToken ct = default)
    {
        var sku = r.Sku.Trim();
        if (await _db.Products.AnyAsync(p => p.Sku == sku, ct))
            throw new InvalidOperationException($"SKU '{sku}' already exists.");

        var product = new Product
        {
            Sku = sku,
            Name = r.Name.Trim(),
            Category = r.Category.Trim(),
            Brand = string.IsNullOrWhiteSpace(r.Brand) ? null : r.Brand.Trim(),
            Price = r.Price,
            StockQuantity = r.StockQuantity,
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);

        return ToDto(product);
    }

    public async Task<ProductLookupDto> UpdateAsync(long id, UpdateProductRequest r, CancellationToken ct = default)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException($"Product {id} not found.");

        product.Name = r.Name.Trim();
        product.Category = r.Category.Trim();
        product.Brand = string.IsNullOrWhiteSpace(r.Brand) ? null : r.Brand.Trim();
        product.Price = r.Price;
        product.StockQuantity = r.StockQuantity;
        product.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return ToDto(product);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException($"Product {id} not found.");

        // FK is Restrict — block delete if the product is referenced by any order.
        if (await _db.OrderItems.AnyAsync(i => i.ProductId == id, ct))
            throw new InvalidOperationException(
                "Product has orders and cannot be deleted. Set stock to 0 to retire it instead.");

        _db.Products.Remove(product);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetImageAsync(long id, string fileName, CancellationToken ct = default)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException($"Product {id} not found.");
        product.ImageUrl = fileName;
        product.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public Task<string?> GetImageFileNameAsync(long id, CancellationToken ct = default) =>
        _db.Products.AsNoTracking().Where(p => p.Id == id).Select(p => p.ImageUrl).FirstOrDefaultAsync(ct);

    private static ProductLookupDto ToDto(Product p) =>
        new(p.Id, p.Sku, p.Name, p.Category, p.Brand, p.Price, p.StockQuantity,
            p.ImageUrl != null ? $"/api/products/{p.Id}/image" : null);
}
