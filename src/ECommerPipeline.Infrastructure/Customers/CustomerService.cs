using ECommerPipeline.Application.Common.DTOs;
using ECommerPipeline.Application.Customers;
using ECommerPipeline.Application.Customers.DTOs;
using ECommerPipeline.Infrastructure.Persistence.Oltp;
using Microsoft.EntityFrameworkCore;

namespace ECommerPipeline.Infrastructure.Customers;

public class CustomerService : ICustomerService
{
    private readonly OltpDbContext _db;
    public CustomerService(OltpDbContext db) => _db = db;

    public async Task<PagedResult<CustomerLookupDto>> SearchAsync(
        string? search, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        page     = page     <= 0 ? 1  : page;
        pageSize = pageSize <= 0 ? 50 : Math.Min(pageSize, 500);

        try
        {
            var q = _db.Customers.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(c => c.FullName.Contains(s) || c.Email.Contains(s) || (c.City ?? "").Contains(s));
            }

            var total = await q.CountAsync(ct);
            var items = await q
                .OrderBy(c => c.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new CustomerLookupDto(c.Id, c.FullName, c.Email, c.Phone, c.City))
                .ToListAsync(ct);

            return new PagedResult<CustomerLookupDto>(items, page, pageSize, total);
        }
        catch (OperationCanceledException) { throw; } // ack cancellation for VS debugger
    }
}
