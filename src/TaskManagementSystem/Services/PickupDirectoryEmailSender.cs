using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using TaskManagementSystem.Configuration;
using TaskManagementSystem.Interfaces.Services;

namespace TaskManagementSystem.Services;

/// <summary>
/// Development email "delivery": writes each email as an .html file into the pickup directory.
/// Open the file in a browser to read it and click its link.
///
/// Why not just log the email? Emails contain one-time tokens (verification, password reset);
/// logs are shipped to log stores and read by many people — secrets must never be logged.
///
/// Registered as a SINGLETON: stateless; depends only on singletons.
/// </summary>
public sealed class PickupDirectoryEmailSender(
    IOptions<EmailSettings> emailOptions,
    IHostEnvironment environment,
    TimeProvider timeProvider,
    ILogger<PickupDirectoryEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string toAddress, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        var settings = emailOptions.Value;
        var directory = Path.IsPathRooted(settings.PickupDirectory)
            ? settings.PickupDirectory
            : Path.Combine(environment.ContentRootPath, settings.PickupDirectory);
        Directory.CreateDirectory(directory);

        var timestamp = timeProvider.GetUtcNow();
        var fileName = $"{timestamp:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.html";
        var filePath = Path.Combine(directory, fileName);

        var document = new StringBuilder()
            .AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">")
            .AppendLine($"<title>{WebUtility.HtmlEncode(subject)}</title></head><body style=\"font-family:sans-serif\">")
            .AppendLine("<table style=\"background:#f4f4f4;padding:8px;margin-bottom:16px\">")
            .AppendLine($"<tr><td><b>From:</b></td><td>{WebUtility.HtmlEncode(settings.FromAddress)}</td></tr>")
            .AppendLine($"<tr><td><b>To:</b></td><td>{WebUtility.HtmlEncode(toAddress)}</td></tr>")
            .AppendLine($"<tr><td><b>Subject:</b></td><td>{WebUtility.HtmlEncode(subject)}</td></tr>")
            .AppendLine($"<tr><td><b>Date (UTC):</b></td><td>{timestamp:yyyy-MM-dd HH:mm:ss}</td></tr>")
            .AppendLine("</table>")
            .AppendLine(htmlBody)
            .AppendLine("</body></html>")
            .ToString();

        await File.WriteAllTextAsync(filePath, document, Encoding.UTF8, cancellationToken);

        // Log WHERE the email went, never its content (it contains tokens).
        logger.LogInformation("Email '{Subject}' written to pickup directory as {FileName}", subject, fileName);
    }
}
