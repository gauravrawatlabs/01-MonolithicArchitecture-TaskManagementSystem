namespace TaskManagementSystem.Exceptions;

/// <summary>The requested resource does not exist → 404 Not Found.</summary>
public sealed class NotFoundException(string message) : AppException(message)
{
    public override int StatusCode => StatusCodes.Status404NotFound;
    public override string Title => "Resource not found";

    public static NotFoundException For(string resourceName, object key) =>
        new($"{resourceName} with id '{key}' was not found.");
}
