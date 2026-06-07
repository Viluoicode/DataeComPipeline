namespace ECommerPipeline.Api.Observability;

/// <summary>
/// Lightweight, thread-safe in-memory counters for the AI Data Analyst endpoint.
/// Surfaced at GET /api/admin/ai-metrics so operators can watch refusal rate,
/// cache effectiveness and latency without a full metrics backend.
/// (In production these would be OpenTelemetry metrics scraped by Prometheus.)
/// </summary>
public sealed class AiMetrics
{
    private long _total, _answered, _refused, _errors, _cacheHits, _latencyMsSum, _latencySamples;

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>Served from cache — no LLM call. Counts toward total, not toward latency.</summary>
    public void RecordCacheHit()
    {
        Interlocked.Increment(ref _total);
        Interlocked.Increment(ref _cacheHits);
    }

    public void RecordAnswered(long ms)
    {
        Interlocked.Increment(ref _total);
        Interlocked.Increment(ref _answered);
        AddLatency(ms);
    }

    public void RecordRefused(long ms)
    {
        Interlocked.Increment(ref _total);
        Interlocked.Increment(ref _refused);
        AddLatency(ms);
    }

    /// <summary>Analyst service unreachable / non-success — not an LLM outcome.</summary>
    public void RecordError()
    {
        Interlocked.Increment(ref _total);
        Interlocked.Increment(ref _errors);
    }

    private void AddLatency(long ms)
    {
        Interlocked.Add(ref _latencyMsSum, ms);
        Interlocked.Increment(ref _latencySamples);
    }

    /// <summary>Immutable snapshot for the metrics endpoint.</summary>
    public object Snapshot()
    {
        long total = Interlocked.Read(ref _total);
        long answered = Interlocked.Read(ref _answered);
        long refused = Interlocked.Read(ref _refused);
        long errors = Interlocked.Read(ref _errors);
        long cacheHits = Interlocked.Read(ref _cacheHits);
        long samples = Interlocked.Read(ref _latencySamples);
        long latSum = Interlocked.Read(ref _latencyMsSum);

        return new
        {
            since = StartedAt,
            totalQuestions = total,
            answered,
            refused,
            errors,
            cacheHits,
            refusalRatePct = total == 0 ? 0 : Math.Round(100.0 * refused / total, 1),
            cacheHitRatePct = total == 0 ? 0 : Math.Round(100.0 * cacheHits / total, 1),
            avgLlmLatencyMs = samples == 0 ? 0 : latSum / samples,
        };
    }
}
