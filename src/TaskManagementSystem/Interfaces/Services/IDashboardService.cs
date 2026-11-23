using System.Security.Claims;
using TaskManagementSystem.Models.DTOs.Dashboard;

namespace TaskManagementSystem.Interfaces.Services;

/// <summary>Dashboard statistics scoped to what the current user may see.</summary>
public interface IDashboardService
{
    Task<DashboardResponse> GetDashboardAsync(ClaimsPrincipal currentUser, CancellationToken cancellationToken);
}
