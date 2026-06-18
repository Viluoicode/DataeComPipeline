using ECommerPipeline.Application.Common.Interfaces;
using ECommerPipeline.Infrastructure.Persistence.Oltp;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ECommerPipeline.Infrastructure.Notifications;

/// Drains the transactional outbox: for each pending message it sends the email
/// and pushes the in-app SignalR notification, then stamps ProcessedAt. Failures
/// increment Attempts and are retried on the next run (up to MaxAttempts), so a
/// transient SMTP outage never loses a notification.
public class OutboxDispatchJob
{
    private const int BatchSize = 50;
    private const int MaxAttempts = 5;

    private readonly OltpDbContext _db;
    private readonly IEmailSender _email;
    private readonly ICustomerNotifier _notifier;
    private readonly ILogger<OutboxDispatchJob> _logger;

    public OutboxDispatchJob(
        OltpDbContext db,
        IEmailSender email,
        ICustomerNotifier notifier,
        ILogger<OutboxDispatchJob> logger)
    {
        _db = db;
        _email = email;
        _notifier = notifier;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var pending = await _db.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.Attempts < MaxAttempts)
            .OrderBy(m => m.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        foreach (var msg in pending)
        {
            try
            {
                var order = await _db.Orders.AsNoTracking()
                    .Include(o => o.Customer)
                    .FirstOrDefaultAsync(o => o.Id == msg.OrderId, ct);

                // Order was deleted — nothing to notify; drop the message.
                if (order is null) { msg.ProcessedAt = DateTime.UtcNow; continue; }

                var (subject, html, inApp) = EmailTemplates.Build(msg.EventType, order);

                if (order.Customer?.Email is { Length: > 0 } email)
                    await _email.SendAsync(
                        new EmailMessage(email, order.Customer.FullName, subject, html), ct);

                await _notifier.NotifyOrderAsync(order.CustomerId,
                    new OrderNotification(order.Id, order.OrderNumber,
                        (int)order.Status, (int)order.PaymentStatus, inApp), ct);

                msg.ProcessedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                msg.Attempts++;
                msg.LastError = ex.Message;
                _logger.LogWarning(ex,
                    "Outbox message {Id} ({Type}) failed, attempt {N}", msg.Id, msg.EventType, msg.Attempts);
            }
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Outbox dispatched {Count} message(s).", pending.Count);
    }
}
