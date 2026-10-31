namespace Rabbictyl.Core.Abstractions;

/// <summary>
/// Sends panel email. When SMTP is not configured the sender is disabled and
/// callers can tell the user that mail is unavailable instead of failing hard.
/// </summary>
public interface IEmailSender
{
    bool Enabled { get; }

    Task SendAsync(
        string to,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken cancellationToken = default);
}
