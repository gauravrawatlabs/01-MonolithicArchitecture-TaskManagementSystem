using System.Text.Json.Nodes;
using TaskManagementSystem.Models.DTOs.Account;
using TaskManagementSystem.Models.DTOs.Auth;
using TaskManagementSystem.Models.DTOs.Categories;
using TaskManagementSystem.Models.DTOs.Comments;
using TaskManagementSystem.Models.DTOs.Projects;
using TaskManagementSystem.Models.DTOs.Tasks;
using TaskManagementSystem.Models.DTOs.Users;

namespace TaskManagementSystem.Extensions;

/// <summary>
/// Realistic request examples for the OpenAPI schemas. Swagger UI pre-fills "Try it out" with them,
/// instead of placeholders like "string" and 0 that would fail validation immediately.
/// Property names are camelCase and enums are strings — exactly what the API accepts.
/// </summary>
public static class OpenApiExamples
{
    private static readonly Dictionary<Type, string> Examples = new()
    {
        [typeof(RegisterRequest)] = """{ "email": "jane.doe@example.com", "password": "Str0ng!Pass", "firstName": "Jane", "lastName": "Doe" }""",
        [typeof(LoginRequest)] = """{ "email": "jane.doe@example.com", "password": "Str0ng!Pass" }""",
        [typeof(RefreshTokenRequest)] = """{ "refreshToken": "<refreshToken from the login response>" }""",
        [typeof(ChangePasswordRequest)] = """{ "currentPassword": "Str0ng!Pass", "newPassword": "N3w!Passw0rd" }""",
        [typeof(ForgotPasswordRequest)] = """{ "email": "jane.doe@example.com" }""",
        [typeof(ResendConfirmationEmailRequest)] = """{ "email": "jane.doe@example.com" }""",
        [typeof(UpdateProfileRequest)] = """{ "firstName": "Jane", "lastName": "Doe", "jobTitle": "Backend Developer" }""",
        [typeof(UpdateUserRolesRequest)] = """{ "roles": [ "Manager" ] }""",
        [typeof(UpdateUserStatusRequest)] = """{ "isActive": false }""",
        [typeof(CreateCategoryRequest)] = """{ "name": "Security", "description": "Vulnerabilities and hardening work." }""",
        [typeof(UpdateCategoryRequest)] = """{ "name": "Security", "description": "Security-related tasks." }""",
        [typeof(CreateProjectRequest)] = """{ "name": "Website Relaunch", "description": "New marketing site.", "startDate": "2026-12-01", "endDate": "2027-06-30" }""",
        [typeof(UpdateProjectRequest)] = """{ "name": "Website Relaunch", "description": "New marketing site (phase 2).", "startDate": "2026-12-01", "endDate": "2027-09-30" }""",
        [typeof(CreateTaskRequest)] = """{ "projectId": 1, "title": "Design the landing page", "description": "Hero section, pricing table, footer.", "priority": "High", "dueDateUtc": "2027-01-15T17:00:00Z", "categoryId": 2 }""",
        [typeof(UpdateTaskRequest)] = """{ "title": "Design the landing page", "description": "Hero section and pricing table.", "priority": "Critical", "dueDateUtc": "2027-01-20T17:00:00Z", "categoryId": 2 }""",
        [typeof(ChangeTaskStatusRequest)] = """{ "status": "InProgress" }""",
        [typeof(CreateCommentRequest)] = """{ "content": "First draft is ready for review." }""",
        [typeof(UpdateCommentRequest)] = """{ "content": "First draft is ready for review (updated link)." }"""
    };

    /// <summary>A fresh JsonNode each call (nodes can only have one parent document).</summary>
    public static JsonNode? For(Type type) =>
        Examples.TryGetValue(type, out var json) ? JsonNode.Parse(json) : null;
}
