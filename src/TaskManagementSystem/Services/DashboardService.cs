using System.Security.Claims;
using TaskManagementSystem.Enums;
using TaskManagementSystem.Extensions;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Models.DTOs.Dashboard;

namespace TaskManagementSystem.Services;

/// <summary>
/// Composes the dashboard from a few aggregate queries. The queries run one after another:
/// a DbContext is NOT thread-safe, so Task.WhenAll on the same context would throw.
/// </summary>
public sealed class DashboardService(IDashboardRepository dashboardRepository, TimeProvider timeProvider) : IDashboardService
{
    private const int TopProjects = 10;

    public async Task<DashboardResponse> GetDashboardAsync(ClaimsPrincipal currentUser, CancellationToken cancellationToken)
    {
        var userId = currentUser.GetUserId();
        var isAdmin = currentUser.IsAdmin();
        var now = timeProvider.GetUtcNow().UtcDateTime; // one "now" for every query → consistent numbers

        var projectCount = await dashboardRepository.CountProjectsAsync(userId, isAdmin, cancellationToken);
        var counters = await dashboardRepository.GetTaskCountersAsync(userId, isAdmin, now, cancellationToken);
        var byStatus = await dashboardRepository.CountByStatusAsync(userId, isAdmin, cancellationToken);
        var byPriority = await dashboardRepository.CountByPriorityAsync(userId, isAdmin, cancellationToken);
        var projects = await dashboardRepository.GetProjectProgressAsync(userId, isAdmin, now, TopProjects, cancellationToken);

        return new DashboardResponse(
            now,
            isAdmin ? "AllProjects" : "MyProjects",
            projectCount,
            new TaskTotals(counters.Total, counters.Open, counters.Overdue, counters.DueInNext7Days, counters.CompletedInLast7Days),
            new MyWorkSummary(counters.MyOpen, counters.MyOverdue, counters.MyDueInNext7Days),
            WithAllKeys(byStatus),
            WithAllKeys(byPriority),
            projects);
    }

    /// <summary>
    /// GROUP BY only returns groups that exist. A chart needs every status/priority, so missing
    /// enum values are added with 0 — in enum (business) order.
    /// </summary>
    private static IReadOnlyDictionary<TEnum, int> WithAllKeys<TEnum>(Dictionary<TEnum, int> counts) where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().ToDictionary(value => value, value => counts.GetValueOrDefault(value));
}
