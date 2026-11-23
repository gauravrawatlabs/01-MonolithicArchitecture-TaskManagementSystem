using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using TaskManagementSystem.Configuration;
using TaskManagementSystem.Data;
using TaskManagementSystem.Exceptions;
using TaskManagementSystem.Extensions;
using TaskManagementSystem.Filters;
using TaskManagementSystem.Helpers;
using TaskManagementSystem.Middleware;

// Startup logger: catches failures BEFORE the real Serilog configuration exists
// (bad configuration, missing secrets, DI errors). Replaced by AddSerilogLogging below.
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ---------- 1. Service registration (DI container) ----------
    builder.AddSerilogLogging();

    builder.Services.AddControllers(options =>
        {
            // Run FluentValidation for every action argument that has a validator.
            options.Filters.Add<ValidationFilter>();

            // FluentValidation is the single source of validation rules; don't let MVC add an implicit
            // [Required] for non-nullable reference types (that would produce a second, different message).
            options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
        })
        .AddJsonOptions(options =>
        {
            // Enums as readable strings ("InProgress") instead of numbers (1) in requests and responses.
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());

            // Don't echo System.Text.Json's exception text (it contains internal .NET type names) in 400s.
            options.AllowInputFormatterExceptionMessages = false;
        })
        .ConfigureApiBehaviorOptions(options =>
            // Model-binding errors (malformed JSON, "abc" for an int, unknown enum value) go through the
            // SAME path as FluentValidation errors: RequestValidationException → ExceptionHandlingMiddleware.
            // One error writer = one format (camelCase keys, correlationId, traceId) for every 400.
            options.InvalidModelStateResponseFactory = context =>
                throw new RequestValidationException(ValidationErrorKeys.FromModelState(context.ModelState)));

    // The OpenAPI generator reads these (minimal-API) JSON options — keep them in sync so the
    // documented schema also shows enums as strings.
    builder.Services.ConfigureHttpJsonOptions(options =>
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    builder.Services.AddProblemDetailsWithCorrelationId();   // RFC 9457 errors + "correlationId"
    builder.Services.AddCorsPolicy(builder.Configuration);
    builder.Services.AddApiDocumentation();
    builder.Services.AddDatabase(builder.Configuration, builder.Environment);
    builder.Services.AddIdentityAndJwtAuthentication(builder.Configuration);
    builder.Services.AddAuthorizationPolicies();
    builder.Services.AddApplicationServices();
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<ApplicationDbContext>(name: "database");

    var app = builder.Build();

    // ---------- 2. Middleware pipeline (order matters — see docs/11) ----------
    app.UseMiddleware<CorrelationIdMiddleware>();       // 1. outermost: every log line below carries CorrelationId
    app.UseSerilogRequestSummary();                     // 2. one summary line per request, with the FINAL status code
    app.UseMiddleware<ExceptionHandlingMiddleware>();   // 3. exceptions → ProblemDetails (+ logged with CorrelationId)
    app.UseStatusCodePages();                           // 4. empty 4xx (401/403/404) → ProblemDetails

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();                                  // 5. browsers: "only ever use HTTPS for this host"
    }
    app.UseHttpsRedirection();                          // 6. http → https

    app.UseApiDocumentation();                          // 7. Development only: /openapi/v1.json + /swagger

    app.UseRouting();                                   // 8. match the endpoint
    app.UseCors(CorsSettings.PolicyName);               // 9. CORS (after routing, before auth: preflights need no token)
    app.UseAuthentication();                            // 10. WHO are you? token → HttpContext.User
    app.UseAuthorization();                             // 11. WHAT may you do? policies → 401 / 403

    app.MapControllers();                               // 12. endpoints
    app.MapHealthChecks("/health").AllowAnonymous();

    // ---------- 3. Startup tasks ----------
    await app.SeedDatabaseAsync();                      // idempotent: creates the admin user if configured

    await app.RunAsync();
}
catch (Exception exception) when (exception is not HostAbortedException)
{
    // HostAbortedException is thrown on purpose by tools like "dotnet ef" after they've built the
    // service provider — it's not a crash, so it's not logged as one.
    Log.Fatal(exception, "Application terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();                     // flush buffered log events before the process exits
}
