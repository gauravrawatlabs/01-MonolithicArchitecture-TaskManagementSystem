using System.Net;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using TaskManagementSystem.Configuration;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Services;

/// <summary>
/// Generates Identity tokens (email confirmation, password reset) and emails them as links to the
/// client application. Tokens are Base64Url-encoded because the raw Identity token contains
/// characters like '+' and '/' that break inside URLs.
/// </summary>
public sealed class AccountEmailService(
    UserManager<ApplicationUser> userManager,
    IEmailSender emailSender,
    IOptions<EmailSettings> emailOptions) : IAccountEmailService
{
    public async Task SendEmailConfirmationAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var token = EncodeToken(await userManager.GenerateEmailConfirmationTokenAsync(user));
        var link = BuildClientLink("confirm-email", ("userId", user.Id.ToString()), ("token", token));

        var body = BuildBody(
            user.FirstName,
            "Please confirm your email address to unlock all features of Task Management System.",
            "Confirm email",
            link,
            "POST /api/account/confirm-email",
            $$"""{ "userId": "{{user.Id}}", "token": "{{token}}" }""");

        await emailSender.SendAsync(user.Email!, "Confirm your email address", body, cancellationToken);
    }

    public async Task SendPasswordResetAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var token = EncodeToken(await userManager.GeneratePasswordResetTokenAsync(user));
        var link = BuildClientLink("reset-password", ("email", user.Email!), ("token", token));

        var body = BuildBody(
            user.FirstName,
            "We received a request to reset your password. If you did not request this, you can ignore this email.",
            "Reset password",
            link,
            "POST /api/account/reset-password",
            $$"""{ "email": "{{user.Email}}", "token": "{{token}}", "newPassword": "<your new password>" }""");

        await emailSender.SendAsync(user.Email!, "Reset your password", body, cancellationToken);
    }

    private static string EncodeToken(string token) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private string BuildClientLink(string page, params (string Name, string Value)[] query)
    {
        var baseUrl = emailOptions.Value.ClientAppBaseUrl.TrimEnd('/');
        var queryString = string.Join("&", query.Select(q => $"{q.Name}={Uri.EscapeDataString(q.Value)}"));
        return $"{baseUrl}/{page}?{queryString}";
    }

    /// <summary>
    /// The developer section shows the exact API call, so the flow can be tested in Swagger
    /// without a front-end application. User-provided values are HTML-encoded (XSS-safe).
    /// </summary>
    private static string BuildBody(
        string firstName, string message, string buttonText, string link, string apiEndpoint, string apiBody) =>
        $"""
        <p>Hi {WebUtility.HtmlEncode(firstName)},</p>
        <p>{WebUtility.HtmlEncode(message)}</p>
        <p><a href="{WebUtility.HtmlEncode(link)}"
              style="background:#2563eb;color:#fff;padding:10px 16px;border-radius:4px;text-decoration:none">
              {WebUtility.HtmlEncode(buttonText)}</a></p>
        <p>This link expires in 3 hours.</p>
        <hr>
        <p><b>Developer testing (no front-end yet):</b> call <code>{WebUtility.HtmlEncode(apiEndpoint)}</code> in Swagger with:</p>
        <pre style="background:#f4f4f4;padding:8px;white-space:pre-wrap;word-break:break-all">{WebUtility.HtmlEncode(apiBody)}</pre>
        """;
}
