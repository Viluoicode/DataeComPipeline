namespace ECommerPipeline.Application.Addresses;

/// Customer address book. All operations are scoped to the calling customer id
/// (passed in from the JWT claim) so a customer can only touch their own addresses.
public interface IAddressService
{
    Task<IReadOnlyList<AddressDto>> ListAsync(long customerId, CancellationToken ct = default);
    Task<AddressDto> CreateAsync(long customerId, SaveAddressRequest req, CancellationToken ct = default);
    Task<AddressDto> UpdateAsync(long customerId, long id, SaveAddressRequest req, CancellationToken ct = default);
    Task DeleteAsync(long customerId, long id, CancellationToken ct = default);
}

public record AddressDto(long Id, string FullName, string Phone, string Address, bool IsDefault);

public record SaveAddressRequest(string FullName, string Phone, string Address, bool IsDefault = false);
