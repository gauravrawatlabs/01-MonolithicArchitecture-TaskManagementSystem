using TaskManagementSystem.Data;
using TaskManagementSystem.Extensions;
using TaskManagementSystem.Middleware;

var builder = WebApplication.CreateBuilder(args);

// ---------- 1. Service registration (DI container) ----------
builder.Services.AddControllers();
builder.Services.AddProblemDetails();       // standard RFC 9457 error format (used by the middleware below)
builder.Services.AddApiDocumentation();
builder.Services.AddDatabase(builder.Configuration, builder.Environment);
builder.Services.AddApplicationServices();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>(name: "database");

var app = builder.Build();

// ---------- 2. Middleware pipeline (order matters) ----------
app.UseMiddleware<ExceptionHandlingMiddleware>();   // outermost: catches exceptions from everything below
app.UseStatusCodePages();                           // empty 4xx responses (e.g. unknown URL) → ProblemDetails
app.UseApiDocumentation();                          // Development only: /openapi/v1.json + /swagger

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");                     // GET /health -> Healthy / Unhealthy

app.Run();
