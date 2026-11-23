using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerUI;
using TaskManagementSystem.Configuration;

namespace TaskManagementSystem.Extensions;

/// <summary>
/// OpenAPI document generation (Microsoft.AspNetCore.OpenApi) + Swagger UI (Swashbuckle UI only).
///
/// The document is produced from the controllers, [ProducesResponseType] attributes and XML comments,
/// then refined by two TRANSFORMERS:
///   - document transformer  → metadata, tag descriptions, the Bearer security scheme
///   - operation transformer → operationId, request examples, security requirement, and the standard
///                             error responses (401 / 400 / 500) applied BY RULE instead of by memory
/// </summary>
public static class OpenApiExtensions
{
    public const string DocumentPath = "/openapi/v1.json";
    private const string BearerSchemeId = "Bearer";
    private const string ProblemJson = "application/problem+json";
    private const string ProblemDetailsSchemaId = "ProblemDetails";

    private static readonly (string Name, string Description)[] Tags =
    [
        ("Authentication", "Register, log in, refresh tokens (rotation), log out, current user."),
        ("Account", "Email verification and password management (forgot / reset / change)."),
        ("Users", "Own profile; Admin user management (roles, activation)."),
        ("Categories", "Task categories. Read: any user. Write: Admin with verified email."),
        ("Projects", "Projects and their teams. Owner/Admin manage; members read."),
        ("Tasks", "Tasks with priorities, due dates, assignment and a status workflow."),
        ("Comments", "Discussion on tasks. Author edits; author/project owner/Admin delete."),
        ("Dashboard", "Statistics scoped to what the caller is allowed to see.")
    ];

    public static IServiceCollection AddApiDocumentation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiDocumentationSettings>()
            .Bind(configuration.GetSection(ApiDocumentationSettings.SectionName));

        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer(DescribeDocumentAsync);
            options.AddOperationTransformer(DescribeOperationAsync);
        });

        return services;
    }

    /// <summary>
    /// Serves /openapi/v1.json and Swagger UI at /swagger — only when ApiDocumentation:Enabled is true
    /// (Development by default). Both are anonymous: reading the docs doesn't require a token.
    /// </summary>
    public static WebApplication UseApiDocumentation(this WebApplication app)
    {
        var settings = app.Services.GetRequiredService<IOptions<ApiDocumentationSettings>>().Value;
        if (!settings.Enabled)
        {
            return app;
        }

        app.MapOpenApi(DocumentPath).AllowAnonymous();

        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint(DocumentPath, "Task Management System API v1");
            options.RoutePrefix = "swagger";
            options.DocumentTitle = "Task Management System API";
            options.DocExpansion(DocExpansion.List);   // operations listed, details collapsed
            options.EnableFilter();                    // search box for tags/operations
            options.EnableTryItOutByDefault();         // request editors open immediately
            options.DisplayRequestDuration();
            options.EnablePersistAuthorization();      // keep the pasted token after a page refresh
        });

        return app;
    }

    private static Task DescribeDocumentAsync(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "Task Management System API";
        document.Info.Version = "v1";
        document.Info.Description =
            "ASP.NET Core (.NET 10) **monolithic** Web API for projects, tasks, categories and comments.\n\n" +
            "**Try it:** `POST /api/auth/login` → copy `accessToken` → click **Authorize** → paste it.\n\n" +
            "**Errors** are RFC 9457 ProblemDetails (`application/problem+json`) with `traceId` and `correlationId`; " +
            "validation errors add an `errors` object keyed by field. Quote the correlation id when reporting a problem.";
        document.Info.Contact = new OpenApiContact
        {
            Name = "Source code and documentation",
            Url = new Uri("https://github.com/gauravrawatlabs/01-MonolithicArchitecture-TaskManagementSystem")
        };

        document.Tags = new HashSet<OpenApiTag>(Tags.Select(tag => new OpenApiTag { Name = tag.Name, Description = tag.Description }));

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[BearerSchemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste the accessToken from POST /api/auth/login (without the word 'Bearer')."
        };

        return Task.CompletedTask;
    }

    private static Task DescribeOperationAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        // Stable, unique operation ids (e.g. "Tasks_ChangeStatus") — used by client generators
        // (NSwag, Kiota, openapi-generator) as method names, and by Swagger UI deep links.
        if (context.Description.ActionDescriptor is ControllerActionDescriptor action)
        {
            operation.OperationId ??= $"{action.ControllerName}_{action.ActionName}";
        }

        operation.Responses ??= new OpenApiResponses();

        // Request example on the body's media type — what Swagger UI pre-fills in "Try it out".
        var bodyType = context.Description.ParameterDescriptions
            .FirstOrDefault(p => p.Source == BindingSource.Body)?.Type;
        if (bodyType is not null
            && operation.RequestBody?.Content?.TryGetValue("application/json", out var jsonBody) == true
            && OpenApiExamples.For(bodyType) is { } example)
        {
            jsonBody.Example = example;
        }

        // Security: [Authorize] (class or action) without [AllowAnonymous] → Bearer token required → may return 401.
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var requiresAuth = metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any();
        if (requiresAuth)
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerSchemeId, context.Document)] = []
            });
            EnsureProblemResponse(operation, context, "401", "Missing, invalid or expired access token.");
        }

        // Anything with a body or query string goes through model binding + FluentValidation → may return 400.
        var hasInput = operation.RequestBody is not null
                       || operation.Parameters?.Any(p => p.In == ParameterLocation.Query) == true;
        if (hasInput)
        {
            EnsureProblemResponse(operation, context, "400", "Invalid input — see the 'errors' object.");
        }

        // Every operation can fail unexpectedly; production returns a generic message + correlationId.
        EnsureProblemResponse(operation, context, "500",
            "Unexpected server error. No internal details are returned; quote the correlationId when reporting it.");

        return Task.CompletedTask;
    }

    /// <summary>Adds a ProblemDetails response for the status code unless the action already documents it.</summary>
    private static void EnsureProblemResponse(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, string statusCode, string description)
    {
        if (operation.Responses!.ContainsKey(statusCode))
        {
            return;
        }

        operation.Responses[statusCode] = new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                [ProblemJson] = new() { Schema = new OpenApiSchemaReference(ProblemDetailsSchemaId, context.Document) }
            }
        };
    }
}
