using TaskManagementSystem.Data;
using TaskManagementSystem.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ---------- 1. Service registration (DI container) ----------
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddDatabase(builder.Configuration, builder.Environment);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>(name: "database");

var app = builder.Build();

// ---------- 2. Middleware pipeline (order matters) ----------
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();               // GET /openapi/v1.json (Swagger UI comes in Phase 14)
}

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");     // GET /health -> Healthy / Unhealthy

app.Run();
