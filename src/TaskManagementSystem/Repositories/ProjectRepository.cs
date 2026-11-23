using Microsoft.EntityFrameworkCore;
using TaskManagementSystem.Data;
using TaskManagementSystem.Enums;
using TaskManagementSystem.Helpers;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Models.DTOs.Projects;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Repositories;

/// <summary>EF Core implementation of <see cref="IProjectRepository"/>.</summary>
public sealed class ProjectRepository(ApplicationDbContext dbContext) : IProjectRepository
{
    public async Task<(IReadOnlyList<ProjectSummaryResponse> Items, int TotalCount)> GetPagedForUserAsync(
        ProjectQueryParameters query, Guid userId, bool isAdmin, CancellationToken cancellationToken)
    {
        // Row-level visibility (shared rule): owner or member; Admin sees all.
        var projects = dbContext.Projects.AsNoTracking().VisibleTo(userId, isAdmin);

        if (query.OwnedByMe == true)
        {
            projects = projects.Where(p => p.OwnerId == userId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            projects = projects.Where(p => p.Name.Contains(search) || (p.Description != null && p.Description.Contains(search)));
        }

        var totalCount = await projects.CountAsync(cancellationToken);

        var items = await ApplySorting(projects, query)
            .ApplyPaging(query)
            .Select(p => new ProjectSummaryResponse(
                p.Id,
                p.Name,
                p.Description,
                p.StartDate,
                p.EndDate,
                p.OwnerId,
                p.Owner.FirstName + " " + p.Owner.LastName,
                p.Members.Count,           // COUNT sub-queries — no members/tasks are loaded
                p.Tasks.Count,
                p.CreatedAtUtc,
                p.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<Project?> GetByIdWithMembersAsync(int id, CancellationToken cancellationToken) =>
        dbContext.Projects
            .Include(p => p.Owner)
            .Include(p => p.Members).ThenInclude(m => m.User)
            .AsSplitQuery() // one SQL query per collection instead of one big joined result
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<bool> NameExistsForOwnerAsync(Guid ownerId, string name, int? excludeId, CancellationToken cancellationToken) =>
        dbContext.Projects.AnyAsync(
            p => p.OwnerId == ownerId && p.Name == name && (excludeId == null || p.Id != excludeId),
            cancellationToken);

    public Task<int> UnassignTasksAsync(int projectId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.Tasks
            .Where(t => t.ProjectId == projectId && t.AssignedToId == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.AssignedToId, (Guid?)null), cancellationToken);

    /// <summary>Whitelisted sorting with a unique tiebreaker (Id) for stable paging; null dates last.</summary>
    private static IOrderedQueryable<Project> ApplySorting(IQueryable<Project> projects, ProjectQueryParameters query)
    {
        var direction = query.SortDirection
                        ?? (query.SortBy == ProjectSortField.CreatedAt ? SortDirection.Desc : SortDirection.Asc);

        var sorted = query.SortBy switch
        {
            ProjectSortField.CreatedAt => projects.OrderByDirection(p => p.CreatedAtUtc, direction),
            ProjectSortField.Name => projects.OrderByDirection(p => p.Name, direction),
            ProjectSortField.StartDate => projects.OrderBy(p => p.StartDate == null).ThenByDirection(p => p.StartDate, direction),
            ProjectSortField.EndDate => projects.OrderBy(p => p.EndDate == null).ThenByDirection(p => p.EndDate, direction),
            _ => throw new ArgumentOutOfRangeException(nameof(query), query.SortBy, "Unsupported sort field.")
        };

        return sorted.ThenBy(p => p.Id);
    }

    public void Add(Project project) => dbContext.Projects.Add(project);

    public void Remove(Project project) => dbContext.Projects.Remove(project);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
