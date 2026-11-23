namespace TaskManagementSystem.Models.DTOs.Auth;

/// <summary>Credentials for logging in.</summary>
/// <param name="Email">Account email address.</param>
/// <param name="Password">Account password.</param>
public sealed record LoginRequest(string Email, string Password);
