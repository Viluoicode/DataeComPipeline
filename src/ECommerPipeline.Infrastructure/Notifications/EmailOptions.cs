namespace ECommerPipeline.Infrastructure.Notifications;

public class EmailOptions
{
    public const string SectionName = "Email";

    /// "None" → log-only (default, works with zero setup). "Smtp" → real SMTP
    /// (MailHog in dev: host=mailhog port=1025; SendGrid/SMTP in prod).
    public string Provider { get; set; } = "None";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public bool UseSsl { get; set; } = false;
    public string FromEmail { get; set; } = "no-reply@ecompipeline.local";
    public string FromName { get; set; } = "ECommerPipeline";
    public string? User { get; set; }
    public string? Password { get; set; }
}
