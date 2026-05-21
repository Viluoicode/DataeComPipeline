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
        // Client disconnected mid-request — not a server error, don't spam logs.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogDebug("Request cancelled by client on {Path}", httpContext.Request.Path);
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
