using Microsoft.EntityFrameworkCore;
using TaskManagementSystem.Data;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Models.DTOs.Users;

namespace TaskManagementSystem.Repositories;

/// <summary>EF Core implementation of <see cref="IUserRepository"/>.</summary>
public sealed class UserRepository(ApplicationDbContext dbContext) : IUserRepository
{
    public async Task<(IReadOnlyList<UserProfileResponse> Items, int TotalCount)> GetPagedAsync(
        UserQueryParameters query, CancellationToken cancellationToken)
    {
        var users = dbContext.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            users = users.Where(u =>
                u.Email!.Contains(search) || u.FirstName.Contains(search) || u.LastName.Contains(search));
        }

        if (query.IsActive is { } isActive)
        {
            users = users.Where(u => u.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var normalizedRole = query.Role.Trim().ToUpperInvariant();
            users = users.Where(u => dbContext.UserRoles.Any(ur =>
                ur.UserId == u.Id &&
                dbContext.Roles.Any(r => r.Id == ur.RoleId && r.NormalizedName == normalizedRole)));
        }

        // Count BEFORE paging (same filters), so the client knows how many pages exist.
        var totalCount = await users.CountAsync(cancellationToken);

        var items = await users
            .OrderBy(u => u.Email)          // paging requires a stable order
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(u => new UserProfileResponse(
                u.Id,
                u.Email!,
                u.FirstName,
                u.LastName,
                u.JobTitle,
                u.EmailConfirmed,
                u.IsActive,
                dbContext.UserRoles
                    .Where(ur => ur.UserId == u.Id)
                    .Join(dbContext.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
                    .OrderBy(name => name)
                    .ToList(),
                u.CreatedAtUtc,
                u.LastLoginAtUtc))
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
