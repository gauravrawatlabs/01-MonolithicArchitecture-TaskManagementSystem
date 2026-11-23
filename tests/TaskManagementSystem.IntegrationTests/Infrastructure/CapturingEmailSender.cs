using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using TaskManagementSystem.Interfaces.Services;

namespace TaskManagementSystem.IntegrationTests.Infrastructure;

public sealed record CapturedEmail(string To, string Subject, string HtmlBody);

/// <summary>Test double for IEmailSender: keeps emails in memory so tests can read their tokens.</summary>
public sealed partial class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<CapturedEmail> _emails = new();

    public Task SendAsync(string toAddress, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        _emails.Enqueue(new CapturedEmail(toAddress, subject, htmlBody));
        return Task.CompletedTask;
    }

    public CapturedEmail Latest(string to, string subjectContains) =>
        _emails.Where(e => e.To == to && e.Subject.Contains(subjectContains, StringComparison.OrdinalIgnoreCase)).LastOrDefault()
        ?? throw new InvalidOperationException($"No '{subjectContains}' email was sent to {to}.");

    /// <summary>userId + token from the developer section of the confirmation email.</summary>
    public (Guid UserId, string Token) GetEmailConfirmation(string to)
    {
        var body = WebUtility.HtmlDecode(Latest(to, "Confirm").HtmlBody);
        return (Guid.Parse(UserIdPattern().Match(body).Groups[1].Value), TokenPattern().Match(body).Groups[1].Value);
    }

    public string GetPasswordResetToken(string to) =>
        TokenPattern().Match(WebUtility.HtmlDecode(Latest(to, "Reset").HtmlBody)).Groups[1].Value;

    [GeneratedRegex("\"userId\": \"([^\"]+)\"")]
    private static partial Regex UserIdPattern();

    [GeneratedRegex("\"token\": \"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}
