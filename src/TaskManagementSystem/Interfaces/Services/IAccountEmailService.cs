using TaskManagementSystem.Models.Entities;

namespace TaskManagementSystem.Interfaces.Services;

/// <summary>Builds and sends account emails that contain Identity tokens.</summary>
public interface IAccountEmailService
{
    Task SendEmailConfirmationAsync(ApplicationUser user, CancellationToken cancellationToken);

    Task SendPasswordResetAsync(ApplicationUser user, CancellationToken cancellationToken);
}
