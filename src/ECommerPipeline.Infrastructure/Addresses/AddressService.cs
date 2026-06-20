using ECommerPipeline.Application.Addresses;
using ECommerPipeline.Domain.Entities;
using ECommerPipeline.Infrastructure.Persistence.Oltp;
using Microsoft.EntityFrameworkCore;

namespace ECommerPipeline.Infrastructure.Addresses;

public class AddressService : IAddressService
{
    private readonly OltpDbContext _db;
    public AddressService(OltpDbContext db) => _db = db;

    public async Task<IReadOnlyList<AddressDto>> ListAsync(long customerId, CancellationToken ct = default)
    {
        return await _db.CustomerAddresses.AsNoTracking()
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.Id)
            .Select(a => new AddressDto(a.Id, a.FullName, a.Phone, a.Address, a.IsDefault))
            .ToListAsync(ct);
    }

    public async Task<AddressDto> CreateAsync(long customerId, SaveAddressRequest r, CancellationToken ct = default)
    {
        if (r.IsDefault) await ClearDefaultAsync(customerId, ct);

        var entity = new CustomerAddress
        {
            CustomerId = customerId,
            FullName = r.FullName.Trim(),
            Phone = r.Phone.Trim(),
            Address = r.Address.Trim(),
            IsDefault = r.IsDefault,
        };
        _db.CustomerAddresses.Add(entity);
        await _db.SaveChangesAsync(ct);

        return new AddressDto(entity.Id, entity.FullName, entity.Phone, entity.Address, entity.IsDefault);
    }

    public async Task<AddressDto> UpdateAsync(long customerId, long id, SaveAddressRequest r, CancellationToken ct = default)
    {
        var entity = await _db.CustomerAddresses.FirstOrDefaultAsync(a => a.Id == id && a.CustomerId == customerId, ct)
            ?? throw new KeyNotFoundException($"Address {id} not found.");

        if (r.IsDefault && !entity.IsDefault) await ClearDefaultAsync(customerId, ct);

        entity.FullName = r.FullName.Trim();
        entity.Phone = r.Phone.Trim();
        entity.Address = r.Address.Trim();
        entity.IsDefault = r.IsDefault;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new AddressDto(entity.Id, entity.FullName, entity.Phone, entity.Address, entity.IsDefault);
    }

    public async Task DeleteAsync(long customerId, long id, CancellationToken ct = default)
    {
        var entity = await _db.CustomerAddresses.FirstOrDefaultAsync(a => a.Id == id && a.CustomerId == customerId, ct)
            ?? throw new KeyNotFoundException($"Address {id} not found.");
        _db.CustomerAddresses.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }

    private async Task ClearDefaultAsync(long customerId, CancellationToken ct)
    {
        var current = await _db.CustomerAddresses
            .Where(a => a.CustomerId == customerId && a.IsDefault).ToListAsync(ct);
        foreach (var a in current) a.IsDefault = false;
    }
}
