namespace TaskManagementSystem.Models.DTOs.Auth;

/// <summary>Data for creating a new account. New accounts always get the "User" role.</summary>
/// <param name="Email">Email address; also used as the login name.</param>
/// <param name="Password">At least 8 characters with upper-case, lower-case, digit and special character.</param>
/// <param name="FirstName">First name (max 50 characters).</param>
/// <param name="LastName">Last name (max 50 characters).</param>
public sealed record RegisterRequest(string Email, string Password, string FirstName, string LastName);
