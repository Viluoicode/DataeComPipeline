using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace ECommerPipeline.Api.Hubs;

/// Per-customer notification hub (/hub/notifications). On connect, the
/// authenticated connection joins a group keyed by its customer id, so the
/// outbox dispatcher can push order events to exactly that customer's browser.
public class NotificationHub : Hub
{
    public static string GroupFor(long customerId) => $"user-{customerId}";

    public override async Task OnConnectedAsync()
    {
        var idStr = Context.User?.FindFirstValue("sub")
                    ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (long.TryParse(idStr, out var customerId))
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(customerId));

        await base.OnConnectedAsync();
    }
}
