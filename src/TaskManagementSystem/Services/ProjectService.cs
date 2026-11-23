using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskManagementSystem.Authorization.Operations;
using TaskManagementSystem.Data;
using TaskManagementSystem.Enums;
using TaskManagementSystem.Exceptions;
using TaskManagementSystem.Extensions;
using TaskManagementSystem.Helpers;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Mappings;
using TaskManagementSystem.Models.DTOs.Common;
using TaskManagementSystem.Models.DTOs.Projects;
using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Services;

/// <summary>Project business logic: ownership, membership, uniqueness and authorization.</summary>
public sealed class ProjectService(
    IProjectRepository projectRepository,
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext dbContext,
    IAuthorizationService authorizationService,
    TimeProvider timeProvider,
    ILogger<ProjectService> logger) : IProjectService
{
    private const string ResourceName = "Project";

    public async Task<PagedResult<ProjectSummaryResponse>> GetProjectsAsync(
        ProjectQueryParameters query, ClaimsPrincipal currentUser, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await projectRepository.GetPagedForUserAsync(
            query, currentUser.GetUserId(), currentUser.IsAdmin(), cancellationToken);

        return new PagedResult<ProjectSummaryResponse>(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<ProjectDetailsResponse> GetByIdAsync(int id, ClaimsPrincipal currentUser, CancellationToken cancellationToken)
    {
        var project = await GetAuthorizedProjectAsync(id, currentUser, ProjectOperations.Read, cancellationToken);
        return project.ToDetailsResponse();
    }

    public async Task<ProjectDetailsResponse> CreateAsync(
        CreateProjectRequest request, ClaimsPrincipal currentUser, CancellationToken cancellationToken)
    {
        var ownerId = currentUser.GetUserId();
        var name = request.Name.Trim();
        await EnsureNameIsUniqueAsync(ownerId, name, excludeId: null, cancellationToken);

        var now = UtcNow();
        var project = new Project
        {
            Name = name,
            Description = NormalizeOptional(request.Description),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            OwnerId = ownerId,
            CreatedAtUtc = now,
            // The owner is also a member (MemberRole = Owner). Both rows are inserted by ONE
            // SaveChanges call, which EF Core wraps in a single transaction automatically.
            Members = [new ProjectMember { UserId = ownerId, MemberRole = ProjectMemberRole.Owner, JoinedAtUtc = now }]
        };

        projectRepository.Add(project);
        await SaveChangesAsync(name, cancellationToken);

        logger.LogInformation("Project {ProjectId} created by user {UserId}", project.Id, ownerId);

        // Reload with Owner + Members.User for the response.
        var created = await projectRepository.GetByIdWithMembersAsync(project.Id, cancellationToken);
        return created!.ToDetailsResponse();
    }

    public async Task<ProjectDetailsResponse> UpdateAsync(
        int id, UpdateProjectRequest request, ClaimsPrincipal currentUser, CancellationToken cancellationToken)
    {
        var project = await GetAuthorizedProjectAsync(id, currentUser, ProjectOperations.Update, cancellationToken);

        var name = request.Name.Trim();
        await EnsureNameIsUniqueAsync(project.OwnerId, name, excludeId: id, cancellationToken);

        project.Name = name;
        project.Description = NormalizeOptional(request.Description);
        project.StartDate = request.StartDate;
        project.EndDate = request.EndDate;
        project.UpdatedAtUtc = UtcNow();

        await SaveChangesAsync(name, cancellationToken);

        logger.LogInformation("Project {ProjectId} updated by user {UserId}", id, currentUser.GetUserId());
        return project.ToDetailsResponse();
    }

    public async Task DeleteAsync(int id, ClaimsPrincipal currentUser, CancellationToken cancellationToken)
    {
        var project = await GetAuthorizedProjectAsync(id, currentUser, ProjectOperations.Delete, cancellationToken);

        // Cascade (configured in Phase 3): Tasks → Comments, and ProjectMembers are deleted by SQL Server.
        projectRepository.Remove(project);
        await projectRepository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Project {ProjectId} deleted by user {UserId}", id, currentUser.GetUserId());
    }

    public async Task<ProjectDetailsResponse> AddMemberAsync(
        int id, AddProjectMemberRequest request, ClaimsPrincipal currentUser, CancellationToken cancellationToken)
    {
        var project = await GetAuthorizedProjectAsync(id, currentUser, ProjectOperations.ManageMembers, cancellationToken);

        if (project.Members.Any(m => m.UserId == request.UserId))
        {
            throw new ConflictException("The user is already a member of this project.");
        }

        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null || !user.IsActive)
        {
            throw new BusinessRuleException("The user does not exist or is inactive.");
        }

        project.Members.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = user.Id,
            User = user,
            MemberRole = ProjectMemberRole.Member,
            JoinedAtUtc = UtcNow()
        });

        try
        {
            await projectRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DbExceptionHelper.IsUniqueConstraintViolation(exception))
        {
            // Two simultaneous "add member" requests: the composite primary key (ProjectId, UserId) wins.
            throw new ConflictException("The user is already a member of this project.");
        }

        logger.LogInformation("User {MemberId} added to project {ProjectId}", user.Id, id);
        return project.ToDetailsResponse();
    }

    public async Task RemoveMemberAsync(int id, Guid userId, ClaimsPrincipal currentUser, CancellationToken cancellationToken)
    {
        var project = await GetAuthorizedProjectAsync(id, currentUser, ProjectOperations.ManageMembers, cancellationToken);

        var member = project.Members.FirstOrDefault(m => m.UserId == userId)
                     ?? throw new NotFoundException("The user is not a member of this project.");

        if (member.MemberRole == ProjectMemberRole.Owner)
        {
            throw new BusinessRuleException("The project owner cannot be removed from the project.");
        }

        // Two writes (bulk UPDATE of tasks + DELETE of the membership) → one transaction.
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            // A non-member must not stay assigned to tasks in the project.
            var unassigned = await projectRepository.UnassignTasksAsync(id, userId, cancellationToken);

            project.Members.Remove(member);
            await projectRepository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation("User {MemberId} removed from project {ProjectId}; {Count} tasks unassigned",
                userId, id, unassigned);
        }
    }

    /// <summary>
    /// Loads the project and applies the two-step authorization check:
    ///   1. READ fails  → 404 (don't reveal that the project exists — project ids are guessable ints)
    ///   2. operation fails → 403 (the user can see it, but may not do this)
    /// </summary>
    private async Task<Project> GetAuthorizedProjectAsync(
        int id, ClaimsPrincipal currentUser, OperationAuthorizationRequirement operation, CancellationToken cancellationToken)
    {
        var notFound = NotFoundException.For(ResourceName, id);
        var project = await projectRepository.GetByIdWithMembersAsync(id, cancellationToken) ?? throw notFound;

        await authorizationService.AuthorizeOrNotFoundAsync(currentUser, project, ProjectOperations.Read, notFound);

        if (operation != ProjectOperations.Read)
        {
            await authorizationService.AuthorizeOrThrowAsync(currentUser, project, operation);
        }

        return project;
    }

    private async Task EnsureNameIsUniqueAsync(Guid ownerId, string name, int? excludeId, CancellationToken cancellationToken)
    {
        if (await projectRepository.NameExistsForOwnerAsync(ownerId, name, excludeId, cancellationToken))
        {
            throw DuplicateName(name);
        }
    }

    private async Task SaveChangesAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            await projectRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (DbExceptionHelper.IsUniqueConstraintViolation(exception))
        {
            throw DuplicateName(name); // race on IX_Projects_OwnerId_Name
        }
    }

    private static ConflictException DuplicateName(string name) =>
        new($"The owner already has a project named '{name}'.");

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
