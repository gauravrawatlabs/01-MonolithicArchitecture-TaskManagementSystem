namespace TaskManagementSystem.Exceptions;

/// <summary>
/// The request is well-formed but breaks a business rule → 422 Unprocessable Entity.
/// Example: assigning a task to a user who is not a member of the project.
/// (Input-format problems such as "Name is required" are validation errors → 400, Phase 11.)
/// </summary>
public sealed class BusinessRuleException(string message) : AppException(message)
{
    public override int StatusCode => StatusCodes.Status422UnprocessableEntity;
    public override string Title => "Business rule violation";
}
