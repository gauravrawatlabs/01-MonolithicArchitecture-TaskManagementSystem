namespace TaskManagementSystem.Exceptions;

/// <summary>
/// The caller could not be authenticated → 401 Unauthorized.
/// Thrown by the login flow (wrong credentials) and when a token's user no longer exists / is inactive.
/// (Requests with a missing/invalid token never reach our code — the JwtBearer middleware returns 401.)
/// </summary>
public sealed class UnauthorizedException(string message) : AppException(message)
{
    public override int StatusCode => StatusCodes.Status401Unauthorized;
    public override string Title => "Unauthorized";
}
