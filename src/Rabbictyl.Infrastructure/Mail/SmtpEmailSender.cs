using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Rabbictyl.Configuration.Options;
using Rabbictyl.Core.Abstractions;

namespace Rabbictyl.Infrastructure.Mail;

/// <summary>
/// Minimal SMTP sender built on <see cref="SmtpClient"/>. The panel only needs
/// transactional mail (password reset), so no template engine is involved.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly MailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(MailOptions options, ILogger<SmtpEmailSender> logger)
    {
        _options = options;
        _logger = logger;
    }

    public bool Enabled => _options.Enabled;

    public async Task SendAsync(
        string to,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled)
        {
            _logger.LogInformation(
                "Mail is disabled; skipped '{Subject}' for {Recipient}.",
                subject,
                to);

            return;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true,
        };

        message.To.Add(to);

        if (!string.IsNullOrWhiteSpace(textBody))
        {
            message.AlternateViews.Add(
                AlternateView.CreateAlternateViewFromString(textBody, null, "text/plain"));
        }

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            EnableSsl = _options.ResolveEncryption() is not "none",
            Timeout = Math.Max(_options.TimeoutSeconds, 1) * 1000,
        };

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            client.Credentials = new NetworkCredential(_options.Username, _options.Password);
        }

        await client.SendMailAsync(message, cancellationToken);
    }
}
