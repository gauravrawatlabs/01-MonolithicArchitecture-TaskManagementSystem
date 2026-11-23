using System.Text.Json.Serialization;
using TaskManagementSystem.Data;
using TaskManagementSystem.Extensions;
using TaskManagementSystem.Filters;
using TaskManagementSystem.Middleware;

var builder = WebApplication.CreateBuilder(args);

// ---------- 1. Service registration (DI container) ----------
builder.Services.AddControllers(options =>
{
    // Run FluentValidation for every action argument that has a validator.
    options.Filters.Add<ValidationFilter>();

    // FluentValidation is the single source of validation rules; don't let MVC add an implicit
    // [Required] for non-nullable reference types (that would produce a second, different message).
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
})
.AddJsonOptions(options =>
    // Enums as readable strings ("InProgress") instead of numbers (1) in requests and responses.
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The OpenAPI generator reads these (minimal-API) JSON options — keep them in sync so the
// documented schema also shows enums as strings.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();       // standard RFC 9457 error format (used by the middleware below)
builder.Services.AddApiDocumentation();
builder.Services.AddDatabase(builder.Configuration, builder.Environment);
builder.Services.AddIdentityAndJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorizationPolicies();
builder.Services.AddApplicationServices();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>(name: "database");

var app = builder.Build();

// ---------- 2. Middleware pipeline (order matters) ----------
app.UseMiddleware<ExceptionHandlingMiddleware>();   // outermost: catches exceptions from everything below
app.UseStatusCodePages();                           // empty 4xx responses (401, unknown URL) → ProblemDetails
app.UseApiDocumentation();                          // Development only: /openapi/v1.json + /swagger

app.UseHttpsRedirection();
app.UseAuthentication();                            // WHO are you? token → HttpContext.User
app.UseAuthorization();                             // WHAT may you do? policies → 401 / 403

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();    // load balancers / monitors call it without a token

// ---------- 3. Startup tasks ----------
await app.SeedDatabaseAsync();                      // idempotent: creates the admin user if configured

await app.RunAsync();
