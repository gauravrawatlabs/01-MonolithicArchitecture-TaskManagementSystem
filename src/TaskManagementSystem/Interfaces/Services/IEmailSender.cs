namespace TaskManagementSystem.Interfaces.Services;

/// <summary>
/// Sends an email. Services depend on this abstraction, so the delivery mechanism
/// (pickup-directory files in development, an SMTP/email provider in production)
/// can be swapped in Program.cs without touching business code.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string toAddress, string subject, string htmlBody, CancellationToken cancellationToken);
}
