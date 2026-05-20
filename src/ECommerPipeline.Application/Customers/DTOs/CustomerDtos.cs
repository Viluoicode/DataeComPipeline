namespace ECommerPipeline.Application.Customers.DTOs;

public record CustomerLookupDto(long Id, string FullName, string Email, string? Phone, string? City);
