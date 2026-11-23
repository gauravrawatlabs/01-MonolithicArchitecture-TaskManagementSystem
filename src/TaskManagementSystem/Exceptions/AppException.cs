namespace TaskManagementSystem.Exceptions;

/// <summary>
/// Base class for EXPECTED application errors (not found, conflict, forbidden, business rule).
/// Services throw these; ExceptionHandlingMiddleware turns them into ProblemDetails responses
/// with the right HTTP status code. Their messages are written by us, so they are safe to
/// return to the client. Any other exception type is treated as an unexpected 500 error.
/// </summary>
public abstract class AppException(string message) : Exception(message)
{
    public abstract int StatusCode { get; }
    public abstract string Title { get; }
}
