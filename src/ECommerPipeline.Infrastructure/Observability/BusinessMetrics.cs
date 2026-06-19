using System.Diagnostics.Metrics;

namespace ECommerPipeline.Infrastructure.Observability;

/// Custom business metrics exposed via the Prometheus /metrics endpoint
/// (the meter is wired into OpenTelemetry in Program.cs). Registered as a
/// singleton; injected as an OPTIONAL constructor dependency into services so
/// direct unit-test construction (no DI) still works with a null no-op.
public sealed class BusinessMetrics : IDisposable
{
    public const string MeterName = "ECommerPipeline.Business";

    private readonly Meter _meter;
    private readonly Counter<long> _ordersCreated;
    private readonly Counter<long> _payments;

    public BusinessMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");
        _ordersCreated = _meter.CreateCounter<long>(
            "ecom_orders_created_total", unit: "{order}", description: "Orders created.");
        _payments = _meter.CreateCounter<long>(
            "ecom_payments_total", unit: "{payment}", description: "Online payment outcomes.");
    }

    public void OrderCreated() => _ordersCreated.Add(1);

    public void PaymentOutcome(bool success) =>
        _payments.Add(1, new KeyValuePair<string, object?>("outcome", success ? "success" : "failed"));

    public void Dispose() => _meter.Dispose();
}
