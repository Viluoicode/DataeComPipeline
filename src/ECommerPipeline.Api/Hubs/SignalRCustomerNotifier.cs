using ECommerPipeline.Application.Common.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace ECommerPipeline.Api.Hubs;

public class SignalRCustomerNotifier : ICustomerNotifier
{
    private readonly IHubContext<NotificationHub> _hub;

    public SignalRCustomerNotifier(IHubContext<NotificationHub> hub) => _hub = hub;

    public Task NotifyOrderAsync(long customerId, OrderNotification notification, CancellationToken ct = default) =>
        _hub.Clients.Group(NotificationHub.GroupFor(customerId))
            .SendAsync("order-notification", notification, ct);
}
