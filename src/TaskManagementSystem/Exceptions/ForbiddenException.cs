namespace TaskManagementSystem.Exceptions;

/// <summary>
/// The user is authenticated but not allowed to perform this action on this resource → 403 Forbidden.
/// Used by resource-based authorization checks inside services (Phase 7).
/// (401 Unauthorized — "who are you?" — is produced by the authentication middleware, not by exceptions.)
/// </summary>
public sealed class ForbiddenException(string message = "You do not have permission to perform this action.")
    : AppException(message)
{
    public override int StatusCode => StatusCodes.Status403Forbidden;
    public override string Title => "Forbidden";
}
