namespace ECommerPipeline.Application.Common.Interfaces;

/// Abstraction so Infrastructure (ETL pipeline) can push events without depending on SignalR.
/// Implemented in the Api layer where SignalR lives.
public interface IEtlNotifier
{
    Task NotifyEtlCompletedAsync(EtlCompletedEvent evt, CancellationToken ct = default);
    Task NotifyDataQualityAlertAsync(DataQualityAlertEvent evt, CancellationToken ct = default);
}

public record EtlCompletedEvent(
    int TotalRowsProcessed,
    long Watermark,
    DateTime CompletedAt,
    long DurationMs);

public record DataQualityAlertEvent(
    int FailedCount,
    int CriticalCount,
    DateTime AlertedAt);
