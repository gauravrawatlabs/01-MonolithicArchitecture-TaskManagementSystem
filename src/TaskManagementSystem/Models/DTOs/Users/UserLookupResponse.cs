namespace TaskManagementSystem.Models.DTOs.Users;

/// <summary>
/// A minimal, active user returned by the lookup (e.g. for "add project member").
/// Contains only what a picker needs — no roles, login times or account status.
/// </summary>
/// <param name="Id">User id (use it as userId in POST /api/projects/{id}/members).</param>
/// <param name="FullName">First and last name.</param>
/// <param name="Email">Email address.</param>
/// <param name="JobTitle">Optional job title (helps tell people with the same name apart).</param>
public sealed record UserLookupResponse(Guid Id, string FullName, string Email, string? JobTitle);
