using ECommerPipeline.Domain.Common;
using ECommerPipeline.Domain.Enums;

namespace ECommerPipeline.Domain.Entities;

public class Order : BaseEntity
{
    public string OrderNumber { get; set; } = null!;
    public long CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public DateTime OrderDate { get; set; }
    public OrderStatus Status { get; set; }

    // ---- Money breakdown ----
    public decimal Subtotal { get; set; }      // sum of line totals (product revenue → flows to OLAP fact)
    public decimal ShippingFee { get; set; }   // flat fee (or 0 when free)
    public decimal TaxAmount { get; set; }      // VAT on the subtotal
    public decimal TotalAmount { get; set; }    // grand total charged = Subtotal + ShippingFee + TaxAmount

    // ---- Fulfilment / shipping (captured at checkout) ----
    public string? ShipFullName { get; set; }
    public string? ShipPhone { get; set; }
    public string? ShipAddress { get; set; }
    public string? Note { get; set; }

    // ---- Payment ----
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cod;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();

    // Audit trail of every status change (compliance + funnel analytics).
    public ICollection<OrderEvent> Events { get; set; } = new List<OrderEvent>();

    // Optimistic-concurrency token (SQL Server rowversion). Prevents two admins
    // from clobbering each other's status updates. Nullable in CLR so non-relational
    // providers (EF InMemory in tests) don't treat it as a required value to supply;
    // SQL Server auto-generates it.
    public byte[]? RowVersion { get; set; }
}
