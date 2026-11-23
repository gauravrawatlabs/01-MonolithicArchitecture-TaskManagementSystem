using FluentValidation.TestHelper;
using Microsoft.Extensions.Time.Testing;
using TaskManagementSystem.Enums;
using TaskManagementSystem.Models.DTOs.Account;
using TaskManagementSystem.Models.DTOs.Auth;
using TaskManagementSystem.Models.DTOs.Projects;
using TaskManagementSystem.Models.DTOs.Tasks;
using TaskManagementSystem.Models.DTOs.Users;
using TaskManagementSystem.UnitTests.TestHelpers;
using TaskManagementSystem.Validators.Account;
using TaskManagementSystem.Validators.Auth;
using TaskManagementSystem.Validators.Projects;
using TaskManagementSystem.Validators.Tasks;
using TaskManagementSystem.Validators.Users;

namespace TaskManagementSystem.UnitTests.Validators;

/// <summary>
/// Validator tests use FluentValidation's TestHelper: TestValidate(...) then
/// ShouldHaveValidationErrorFor / ShouldNotHaveAnyValidationErrors.
/// </summary>
public sealed class ValidatorTests
{
    // ---------- Registration: required, email, length, password policy ----------

    [Fact]
    public void Register_ValidRequest_HasNoErrors()
    {
        var result = new RegisterRequestValidator().TestValidate(
            new RegisterRequest("jane@example.com", "Str0ng!Pass", "Jane", "Doe"));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Register_InvalidEmail_HasEmailError(string email)
    {
        var result = new RegisterRequestValidator().TestValidate(new RegisterRequest(email, "Str0ng!Pass", "Jane", "Doe"));

        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Theory]
    [InlineData("Sh0rt!", "at least 8")]
    [InlineData("str0ng!pass", "upper-case")]
    [InlineData("STR0NG!PASS", "lower-case")]
    [InlineData("Strong!Pass", "digit")]
    [InlineData("Str0ngPass1", "special")]
    public void Register_WeakPassword_ExplainsWhichRuleFailed(string password, string expectedMessagePart)
    {
        var result = new RegisterRequestValidator().TestValidate(new RegisterRequest("jane@example.com", password, "Jane", "Doe"));

        result.ShouldHaveValidationErrorFor(x => x.Password);
        Assert.Contains(result.Errors, e => e.PropertyName == "Password" && e.ErrorMessage.Contains(expectedMessagePart));
    }

    [Fact]
    public void Register_NamesTooLongOrEmpty_HaveErrors()
    {
        var result = new RegisterRequestValidator().TestValidate(
            new RegisterRequest("jane@example.com", "Str0ng!Pass", new string('x', 51), ""));

        result.ShouldHaveValidationErrorFor(x => x.FirstName);
        result.ShouldHaveValidationErrorFor(x => x.LastName);
    }

    [Fact]
    public void ChangePassword_NewEqualsCurrent_HasError()
    {
        var result = new ChangePasswordRequestValidator().TestValidate(new ChangePasswordRequest("Str0ng!Pass", "Str0ng!Pass"));

        result.ShouldHaveValidationErrorFor(x => x.NewPassword)
              .WithErrorMessage("New password must be different from the current password.");
    }

    // ---------- Task due date: depends on the clock → FakeTimeProvider ----------

    private static CreateTaskRequestValidator TaskValidatorAt(DateTimeOffset now) => new(new FakeTimeProvider(now));

    [Fact]
    public void CreateTask_DueDateInThePast_HasError()
    {
        var validator = TaskValidatorAt(TestData.Now);
        var request = new CreateTaskRequest(1, "Title", null, null, TestData.Now.UtcDateTime.AddMinutes(-1), null);

        validator.TestValidate(request).ShouldHaveValidationErrorFor(x => x.DueDateUtc)
                 .WithErrorMessage("Due date must be in the future.");
    }

    [Fact]
    public void CreateTask_DueDateExactlyNow_HasError()
    {
        var request = new CreateTaskRequest(1, "Title", null, null, TestData.Now.UtcDateTime, null);

        TaskValidatorAt(TestData.Now).TestValidate(request).ShouldHaveValidationErrorFor(x => x.DueDateUtc);
    }

    [Fact]
    public void CreateTask_DueDateInTheFutureOrMissing_IsValid()
    {
        var validator = TaskValidatorAt(TestData.Now);

        validator.TestValidate(new CreateTaskRequest(1, "Title", null, TaskPriority.High, TestData.Now.UtcDateTime.AddDays(1), 2))
                 .ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new CreateTaskRequest(1, "Title", null, null, null, null))
                 .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void CreateTask_InvalidFields_HaveErrors()
    {
        var request = new CreateTaskRequest(0, new string('t', 201), null, (TaskPriority)99, null, 0);

        var result = TaskValidatorAt(TestData.Now).TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.ProjectId);
        result.ShouldHaveValidationErrorFor(x => x.Title);
        result.ShouldHaveValidationErrorFor(x => x.Priority);
        result.ShouldHaveValidationErrorFor(x => x.CategoryId);
    }

    // ---------- Business-rule style validation ----------

    [Theory]
    [InlineData("2027-01-01", "2026-12-31", true)]
    [InlineData("2027-01-01", "2027-01-01", false)]
    [InlineData("2027-01-01", null, false)]
    [InlineData(null, "2026-01-01", false)]
    public void CreateProject_EndDateBeforeStartDate_HasError(string? start, string? end, bool expectError)
    {
        var request = new CreateProjectRequest(
            "Apollo", null,
            start is null ? null : DateOnly.Parse(start),
            end is null ? null : DateOnly.Parse(end));

        var result = new CreateProjectRequestValidator().TestValidate(request);

        if (expectError)
        {
            result.ShouldHaveValidationErrorFor(x => x.EndDate);
        }
        else
        {
            result.ShouldNotHaveAnyValidationErrors();
        }
    }

    [Fact]
    public void TaskQuery_DueFromAfterDueTo_AndBadPaging_HaveErrors()
    {
        var query = new TaskQueryParameters
        {
            DueFrom = new DateTime(2027, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            DueTo = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Page = 0,
            PageSize = 101
        };

        var result = new TaskQueryParametersValidator().TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.DueTo);
        result.ShouldHaveValidationErrorFor(x => x.Page);       // rules inherited from PagedQueryValidator
        result.ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void TaskQuery_Defaults_AreValid()
    {
        new TaskQueryParametersValidator().TestValidate(new TaskQueryParameters()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void UpdateRoles_DuplicateOrUnknownRoles_HaveErrors()
    {
        var validator = new UpdateUserRolesRequestValidator();

        validator.TestValidate(new UpdateUserRolesRequest(["User", "user"])).ShouldHaveValidationErrorFor(x => x.Roles);
        validator.TestValidate(new UpdateUserRolesRequest(["Boss"])).ShouldHaveValidationErrorFor("Roles[0]");
        validator.TestValidate(new UpdateUserRolesRequest(["manager"])).ShouldNotHaveAnyValidationErrors();
    }
}
