namespace ECommerPipeline.Application.Common.Interfaces;

/// Transactional-email abstraction. Implemented by an SMTP sender (MailHog in
/// dev, real SMTP/SendGrid in prod) or a no-op logger when email is disabled.
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

public record EmailMessage(string ToEmail, string ToName, string Subject, string HtmlBody);
