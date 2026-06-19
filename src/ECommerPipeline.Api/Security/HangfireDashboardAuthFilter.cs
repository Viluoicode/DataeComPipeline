using System.Text;
using Hangfire.Dashboard;

namespace ECommerPipeline.Api.Security;

/// Gates the Hangfire dashboard (/hangfire). The app authenticates users with
/// JWT in localStorage, which a browser navigation to /hangfire can't send — so
/// the dashboard uses HTTP Basic auth instead:
///   • Development: open (convenience).
///   • Production: requires Basic credentials matching Hangfire:DashboardUser /
///     Hangfire:DashboardPassword. If those are not configured, access is denied
///     (safe default — never leave the job dashboard world-readable in prod).
public class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    private readonly IHostEnvironment _env;
    private readonly string? _user;
    private readonly string? _password;

    public HangfireDashboardAuthFilter(IHostEnvironment env, IConfiguration config)
    {
        _env = env;
        _user = config["Hangfire:DashboardUser"];
        _password = config["Hangfire:DashboardPassword"];
    }

    public bool Authorize(DashboardContext context)
    {
        if (_env.IsDevelopment()) return true;

        if (string.IsNullOrEmpty(_user) || string.IsNullOrEmpty(_password))
            return false;   // not configured in prod → deny

        var http = context.GetHttpContext();
        var header = http.Request.Headers.Authorization.ToString();

        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            http.Response.Headers["WWW-Authenticate"] = "Basic realm=\"Hangfire\"";
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
        }
        catch { return false; }

        var sep = decoded.IndexOf(':');
        if (sep < 0) return false;

        return decoded[..sep] == _user && decoded[(sep + 1)..] == _password;
    }
}
