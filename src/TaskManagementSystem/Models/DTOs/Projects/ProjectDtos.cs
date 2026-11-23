using TaskManagementSystem.Enums;
using TaskManagementSystem.Models.DTOs.Common;

namespace TaskManagementSystem.Models.DTOs.Projects;

/// <summary>A project in list results.</summary>
/// <param name="Id">Project id.</param>
/// <param name="Name">Project name (unique per owner).</param>
/// <param name="Description">Optional description.</param>
/// <param name="StartDate">Optional start date (yyyy-MM-dd).</param>
/// <param name="EndDate">Optional end date (yyyy-MM-dd), on or after StartDate.</param>
/// <param name="OwnerId">The owner's user id.</param>
/// <param name="OwnerName">The owner's full name.</param>
/// <param name="MemberCount">Number of members, including the owner.</param>
/// <param name="TaskCount">Number of tasks in the project.</param>
/// <param name="CreatedAtUtc">Creation time (UTC).</param>
/// <param name="UpdatedAtUtc">Last update time (UTC), or null.</param>
public sealed record ProjectSummaryResponse(
    int Id,
    string Name,
    string? Description,
    DateOnly? StartDate,
    DateOnly? EndDate,
    Guid OwnerId,
    string OwnerName,
    int MemberCount,
    int TaskCount,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

/// <summary>A project with its team.</summary>
/// <param name="Id">Project id.</param>
/// <param name="Name">Project name.</param>
/// <param name="Description">Optional description.</param>
/// <param name="StartDate">Optional start date.</param>
/// <param name="EndDate">Optional end date.</param>
/// <param name="OwnerId">The owner's user id.</param>
/// <param name="OwnerName">The owner's full name.</param>
/// <param name="Members">All members, owner first.</param>
/// <param name="CreatedAtUtc">Creation time (UTC).</param>
/// <param name="UpdatedAtUtc">Last update time (UTC), or null.</param>
public sealed record ProjectDetailsResponse(
    int Id,
    string Name,
    string? Description,
    DateOnly? StartDate,
    DateOnly? EndDate,
    Guid OwnerId,
    string OwnerName,
    IReadOnlyList<ProjectMemberResponse> Members,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

/// <summary>A member of a project's team.</summary>
/// <param name="UserId">The member's user id.</param>
/// <param name="FullName">The member's full name.</param>
/// <param name="Email">The member's email address.</param>
/// <param name="MemberRole">Owner or Member (role inside this project).</param>
/// <param name="JoinedAtUtc">When the user joined the project (UTC).</param>
public sealed record ProjectMemberResponse(
    Guid UserId,
    string FullName,
    string Email,
    string MemberRole,
    DateTime JoinedAtUtc);

/// <summary>Data for creating a project. The caller becomes its owner.</summary>
/// <param name="Name">Project name (max 150 characters, unique among your projects).</param>
/// <param name="Description">Optional description (max 2000 characters).</param>
/// <param name="StartDate">Optional start date, e.g. "2026-12-01".</param>
/// <param name="EndDate">Optional end date, on or after StartDate.</param>
public sealed record CreateProjectRequest(string Name, string? Description, DateOnly? StartDate, DateOnly? EndDate);

/// <summary>Data for updating a project (replaces all editable fields).</summary>
/// <param name="Name">Project name (max 150 characters, unique among the owner's projects).</param>
/// <param name="Description">Optional description (max 2000 characters).</param>
/// <param name="StartDate">Optional start date.</param>
/// <param name="EndDate">Optional end date, on or after StartDate.</param>
public sealed record UpdateProjectRequest(string Name, string? Description, DateOnly? StartDate, DateOnly? EndDate);

/// <summary>Adds an existing, active user to the project team.</summary>
/// <param name="UserId">The user to add.</param>
public sealed record AddProjectMemberRequest(Guid UserId);

/// <summary>Search, filters, sorting and paging for the project list (bound from the query string).</summary>
public sealed class ProjectQueryParameters : PagedQueryParameters
{
    /// <summary>Matches name or description (contains).</summary>
    public string? Search { get; init; }

    /// <summary>true = only projects you own.</summary>
    public bool? OwnedByMe { get; init; }

    /// <summary>CreatedAt (default), Name, StartDate or EndDate.</summary>
    public ProjectSortField SortBy { get; init; } = ProjectSortField.CreatedAt;

    /// <summary>Asc or Desc. Default: Desc for CreatedAt (newest first), Asc otherwise.</summary>
    public SortDirection? SortDirection { get; init; }
}
