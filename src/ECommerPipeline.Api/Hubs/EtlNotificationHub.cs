using Microsoft.AspNetCore.SignalR;

namespace ECommerPipeline.Api.Hubs;

/// SignalR hub for pushing ETL lifecycle events to connected clients.
/// Clients subscribe via /hub/etl and listen for "etl-completed" events.
public class EtlNotificationHub : Hub
{
}
