using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Models.DTOs.Dashboard;

namespace TaskManagementSystem.Controllers;

/// <summary>Dashboard statistics.</summary>
[ApiController]
[Route("api/dashboard")]
[Tags("Dashboard")]
[Authorize]
public sealed class DashboardController(IDashboardService dashboardService) : ControllerBase
{
    /// <summary>Task and project statistics for everything you can see (Admins: the whole system).</summary>
    /// <remarks>
    /// Includes totals, overdue, due in the next 7 days, completed in the last 7 days, your assigned work,
    /// counts by status and priority, and progress of the 10 projects with the most open tasks.
    /// </remarks>
    /// <response code="200">The dashboard.</response>
    /// <response code="401">Not logged in.</response>
    [HttpGet]
    [ProducesResponseType<DashboardResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<DashboardResponse>> Get(CancellationToken cancellationToken)
    {
        return Ok(await dashboardService.GetDashboardAsync(User, cancellationToken));
    }
}
