using Serilog.Context;

namespace ECommerPipeline.Api.Middleware;

/// <summary>
/// Assigns a correlation id to every request so a single user action can be
/// traced across all log lines (and downstream services).
///
/// - Reads incoming "X-Correlation-ID" header if the caller provides one
///   (lets a frontend / gateway propagate an id end-to-end).
/// - Otherwise generates a new GUID.
/// - Pushes it into Serilog's LogContext so EVERY log line within the request
///   carries { CorrelationId } — visible in Seq / structured JSON output.
/// - Echoes it back in the response header so the client can log it too.
/// </summary>
public class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing)
                            && !string.IsNullOrWhiteSpace(existing)
            ? existing.ToString()
            : Guid.NewGuid().ToString();

        context.Items[HeaderName] = correlationId;

        // Echo back to caller (set before response starts)
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // Every log line inside this scope gets { CorrelationId }
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
