namespace TaskManagementSystem.Exceptions;

/// <summary>
/// The request input is invalid → 400 Bad Request with a per-field "errors" dictionary
/// (ValidationProblemDetails). Thrown by the ValidationFilter (FluentValidation) and when
/// ASP.NET Core Identity rejects input (e.g. password policy).
/// </summary>
public sealed class RequestValidationException(IDictionary<string, string[]> errors)
    : AppException("One or more validation errors occurred.")
{
    /// <summary>Field name (camelCase, matching the JSON) → error messages.</summary>
    public IDictionary<string, string[]> Errors { get; } = errors;

    public override int StatusCode => StatusCodes.Status400BadRequest;
    public override string Title => "One or more validation errors occurred.";
}
