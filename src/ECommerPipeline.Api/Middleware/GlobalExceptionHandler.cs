using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ECommerPipeline.Api.Middleware;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Any cancellation is treated as "client closed request" — not a server
        // error. This covers: client aborted via AbortController (which doesn't
        // always propagate to RequestAborted depending on proxy), Vite dev-server
        // proxy timeout, React StrictMode rapid re-mounts, EF/Dapper cancellation
        // tokens firing mid-query, etc. We don't want any of these to log as ERR.
        if (exception is OperationCanceledException)
        {
            _logger.LogDebug("Request cancelled on {Path}", httpContext.Request.Path);
            if (!httpContext.Response.HasStarted)
                httpContext.Response.StatusCode = 499; // unofficial: client closed request
            return true;
        }

        _logger.LogError(exception, "Unhandled exception on {Path}", httpContext.Request.Path);

        var (status, title) = exception switch
        {
            InvalidOperationException => (StatusCodes.Status400BadRequest, "Invalid operation"),
            KeyNotFoundException      => (StatusCodes.Status404NotFound,   "Resource not found"),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            _                         => (StatusCodes.Status500InternalServerError, "Internal server error")
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title  = title,
            Detail = _env.IsDevelopment() ? exception.ToString() : exception.Message,
            Instance = httpContext.Request.Path
        };

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
