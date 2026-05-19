using ECommerPipeline.Application.Common.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace ECommerPipeline.Api.Hubs;

public class SignalREtlNotifier : IEtlNotifier
{
    private readonly IHubContext<EtlNotificationHub> _hub;

    public SignalREtlNotifier(IHubContext<EtlNotificationHub> hub) => _hub = hub;

    public Task NotifyEtlCompletedAsync(EtlCompletedEvent evt, CancellationToken ct = default) =>
        _hub.Clients.All.SendAsync("etl-completed", evt, ct);
}
