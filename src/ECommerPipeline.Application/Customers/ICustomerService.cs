using ECommerPipeline.Application.Common.DTOs;
using ECommerPipeline.Application.Customers.DTOs;

namespace ECommerPipeline.Application.Customers;

public interface ICustomerService
{
    Task<PagedResult<CustomerLookupDto>> SearchAsync(string? search, int page = 1, int pageSize = 50, CancellationToken ct = default);
}
