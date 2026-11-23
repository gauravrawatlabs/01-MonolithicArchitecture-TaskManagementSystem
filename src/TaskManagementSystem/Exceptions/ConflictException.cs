namespace TaskManagementSystem.Exceptions;

/// <summary>
/// The request conflicts with the current state of the resource → 409 Conflict.
/// Examples: duplicate category name, invalid task status transition.
/// </summary>
public sealed class ConflictException(string message) : AppException(message)
{
    public override int StatusCode => StatusCodes.Status409Conflict;
    public override string Title => "Conflict";
}
