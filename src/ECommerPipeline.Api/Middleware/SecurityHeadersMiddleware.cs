namespace ECommerPipeline.Api.Middleware;

/// Adds baseline security response headers to every response. Defence-in-depth
/// against clickjacking, MIME sniffing, and referrer leakage. CSP + HSTS are
/// only emitted outside Development so they don't interfere with the Scalar/Swagger
/// dev UI (and HSTS is only meaningful behind the TLS-terminating proxy in prod).
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _env;

    public SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment env)
    {
        _next = next;
        _env = env;
    }

    public Task InvokeAsync(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["X-XSS-Protection"] = "0";   // legacy filter off; modern defence is CSP

        if (!_env.IsDevelopment())
        {
            // The SPA is served by nginx; this protects API responses + any
            // single-deployment wwwroot. frame-ancestors 'none' = no embedding.
            h["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'; base-uri 'self'";
            // TLS is terminated at the reverse proxy (Caddy/nginx); the browser
            // only ever sees this over HTTPS, so advertising HSTS is safe.
            h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        }

        return _next(ctx);
    }
}
