using System.Net;
using System.Net.Mail;
using ECommerPipeline.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECommerPipeline.Infrastructure.Notifications;

/// Real SMTP sender. Works with MailHog (host=mailhog, port=1025, no auth/TLS)
/// in dev and any SMTP relay in prod.
public class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _opt;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> opt, ILogger<SmtpEmailSender> logger)
    {
        _opt = opt.Value;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage m, CancellationToken ct = default)
    {
        using var client = new SmtpClient(_opt.Host, _opt.Port) { EnableSsl = _opt.UseSsl };
        if (!string.IsNullOrEmpty(_opt.User))
            client.Credentials = new NetworkCredential(_opt.User, _opt.Password);

        using var mail = new MailMessage
        {
            From = new MailAddress(_opt.FromEmail, _opt.FromName),
            Subject = m.Subject,
            Body = m.HtmlBody,
            IsBodyHtml = true,
        };
        mail.To.Add(new MailAddress(m.ToEmail, m.ToName));

        await client.SendMailAsync(mail, ct);
        _logger.LogInformation("Email sent to {To}: {Subject}", m.ToEmail, m.Subject);
    }
}

/// Default sender when Email:Provider is not "Smtp". Logs instead of sending so
/// the stack runs end-to-end with zero email configuration.
public class NoOpEmailSender : IEmailSender
{
    private readonly ILogger<NoOpEmailSender> _logger;
    public NoOpEmailSender(ILogger<NoOpEmailSender> logger) => _logger = logger;

    public Task SendAsync(EmailMessage m, CancellationToken ct = default)
    {
        _logger.LogInformation("[email:noop] To {To} — {Subject}", m.ToEmail, m.Subject);
        return Task.CompletedTask;
    }
}
