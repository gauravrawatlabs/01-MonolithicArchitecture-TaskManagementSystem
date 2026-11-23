# Phase 2 — Project Setup: Solution, SQL Server & EF Core

> **Phase status:** ✅ Implemented & verified technically (2026-11-23) — awaiting user review
> **Goal:** Solution, one Web API project (the monolith), two test projects, and a working EF Core → SQL Server connection. No business features yet.

---

## 1. What was created

```
01-MonolithicArchitecture-TaskManagementSystem/
├── global.json                 ← pins the .NET SDK to 10.0.x
├── dotnet-tools.json           ← local tool manifest (dotnet-ef 10.0.15)
├── .gitignore                  ← standard .NET gitignore (dotnet new gitignore)
├── TaskManagementSystem.slnx   ← solution (new XML format, .NET 10 default)
├── docs/
├── src/
│   └── TaskManagementSystem/   ← THE monolith (only deployable)
│       ├── Controllers/        (empty — first controller arrives in Phase 5/8)
│       ├── Data/ApplicationDbContext.cs
│       ├── Extensions/ServiceCollectionExtensions.cs
│       ├── Properties/launchSettings.json
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       ├── Program.cs
│       └── TaskManagementSystem.csproj
└── tests/
    ├── TaskManagementSystem.UnitTests/         (template placeholder UnitTest1.cs — replaced in Phase 13)
    └── TaskManagementSystem.IntegrationTests/  (template placeholder UnitTest1.cs — replaced in Phase 13)
```

Other folders from the Phase 1 structure (`Models/`, `Services/`, …) are created when their first file is added — Git does not track empty folders.

---

## 2. Concepts

### Solution vs. Project
- A **project** (`.csproj`) compiles to one assembly.
- A **solution** (`.slnx`) is only a container telling the IDE / `dotnet build` which projects belong together.
- The app is still **one deployable** (`TaskManagementSystem.dll`). Test projects are never deployed, so they don't make this a "multi-project architecture". Clean Architecture would split the *application itself* into Domain/Application/Infrastructure/API projects — we don't.

### `global.json`
The machine has SDKs 8.0, 10.0.404 and 11.0. Without a pin, `dotnet` uses the newest SDK (11), so templates/tooling would behave like .NET 11.

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```
`latestFeature` = "any 10.0.x SDK ≥ 10.0.100" — never rolls forward to 11.

### Local tools (`dotnet-ef`)
The EF Core CLI version should match the EF Core runtime version. A **local tool manifest** (`dotnet-tools.json`) stores the version in the repo; anyone cloning runs `dotnet tool restore` and gets the same tool.

### EF Core packages (all 10.0.15)

| Package | Purpose | Deployed? |
|---|---|---|
| `Microsoft.EntityFrameworkCore.SqlServer` | SQL Server provider — LINQ → T-SQL (brings EF Core itself) | Yes |
| `Microsoft.EntityFrameworkCore.Design` | Design-time services used by `dotnet ef` (migrations scaffolding) | No (`PrivateAssets=all`) |
| `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` | `/health` checks DB reachability | Yes |
| `Microsoft.AspNetCore.OpenApi` (from template) | Generates `/openapi/v1.json` | Yes |

Other packages (Identity, JWT, Serilog, FluentValidation, …) are added **in the phase that uses them**.

### DbContext
EF Core's **session with the database**:
- **Unit of Work** — tracks changes, saves them in one `SaveChangesAsync()`.
- **Repository-like access** via `DbSet<T>`.
- **Model configuration** in `OnModelCreating`.

### Why DbContext is **Scoped** (first DI lifetime lesson)
`AddDbContext` registers the context as **Scoped** = one instance per HTTP request.

```
Request A ──► Scope A ──► DbContext #1 (own change tracker) ──► disposed at end of request
Request B ──► Scope B ──► DbContext #2 (own change tracker) ──► disposed at end of request
```
- **Singleton ❌** — DbContext is **not thread-safe**; concurrent requests would corrupt one shared change tracker and leak data between users.
- **Transient ❌** — two services in the same request would get *different* contexts; one `SaveChanges` would not see the other's changes.

### Configuration hierarchy (later overrides earlier)
```
appsettings.json → appsettings.{Environment}.json → User Secrets (Development only)
                 → Environment variables → Command-line args   (highest priority)
```
- The dev connection string uses **Windows authentication** → no password → safe in `appsettings.Development.json`.
- Production supplies `ConnectionStrings__DefaultConnection` as an **environment variable** (`__` = section separator).
- Real secrets (JWT signing key, Phase 5) go into **User Secrets**.

---

## 3. Commands that were run

```powershell
# 1. Install the .NET 10 SDK (needs admin/UAC approval)
winget install Microsoft.DotNet.SDK.10

# 2. From the repo root
cd "D:\MyProjects\ASP DotNet Core\Monolithic Architecture\MonolithicArchitecture\01-MonolithicArchitecture-TaskManagementSystem"
#    (global.json created by hand — see above)
dotnet --version                     # → 10.0.404

# 3. Solution + projects
dotnet new gitignore
dotnet new sln -n TaskManagementSystem
dotnet new webapi -n TaskManagementSystem -o src/TaskManagementSystem --use-controllers
dotnet new xunit  -n TaskManagementSystem.UnitTests        -o tests/TaskManagementSystem.UnitTests
dotnet new xunit  -n TaskManagementSystem.IntegrationTests -o tests/TaskManagementSystem.IntegrationTests
dotnet sln add src/TaskManagementSystem tests/TaskManagementSystem.UnitTests tests/TaskManagementSystem.IntegrationTests
dotnet add tests/TaskManagementSystem.UnitTests        reference src/TaskManagementSystem
dotnet add tests/TaskManagementSystem.IntegrationTests reference src/TaskManagementSystem

# 4. EF Core tool + packages
dotnet new tool-manifest
dotnet tool install dotnet-ef --version 10.0.15
dotnet add src/TaskManagementSystem package Microsoft.EntityFrameworkCore.SqlServer --version 10.0.15
dotnet add src/TaskManagementSystem package Microsoft.EntityFrameworkCore.Design --version 10.0.15
dotnet add src/TaskManagementSystem package Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore --version 10.0.15

# 5. Remove template sample code
Remove-Item src/TaskManagementSystem/WeatherForecast.cs
Remove-Item src/TaskManagementSystem/Controllers/WeatherForecastController.cs
Remove-Item src/TaskManagementSystem/TaskManagementSystem.http
```

- `--use-controllers` → MVC controllers instead of Minimal APIs (our design uses controllers).
- Test projects reference the app; the app **never** references test projects.

---

## 4. Code

### `src/TaskManagementSystem/TaskManagementSystem.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.15" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.15">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.15" />
    <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore" Version="10.0.15" />
  </ItemGroup>

</Project>
```

### `src/TaskManagementSystem/Data/ApplicationDbContext.cs`
```csharp
using Microsoft.EntityFrameworkCore;

namespace TaskManagementSystem.Data;

/// <summary>
/// The application's single EF Core session with SQL Server.
/// In Phase 3 this will inherit from IdentityDbContext and expose the domain DbSets.
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Picks up every IEntityTypeConfiguration<T> class in this assembly
        // (added under Data/Configurations in Phase 3).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
```
- **Primary constructor** — DI passes the options built by `AddDbContext` (provider + connection string). The context doesn't know where the connection string came from.
- **`ApplyConfigurationsFromAssembly`** — keeps `OnModelCreating` short; each entity gets its own configuration class.

### `src/TaskManagementSystem/Extensions/ServiceCollectionExtensions.cs`
```csharp
using Microsoft.EntityFrameworkCore;
using TaskManagementSystem.Data;

namespace TaskManagementSystem.Extensions;

/// <summary>
/// Groups service registrations by concern so Program.cs stays a readable composition root.
/// </summary>
public static class ServiceCollectionExtensions
{
    private const string ConnectionStringName = "DefaultConnection";

    public static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Fail fast at startup instead of on the first request.
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. " +
                "Set it in appsettings.Development.json (local) or the " +
                $"'ConnectionStrings__{ConnectionStringName}' environment variable.");
        }

        // AddDbContext registers ApplicationDbContext with a SCOPED lifetime (one per HTTP request).
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlServer(connectionString);

            if (environment.IsDevelopment())
            {
                // Richer exception messages while developing. We deliberately do NOT call
                // EnableSensitiveDataLogging(): it would write parameter values
                // (password hashes, token hashes, emails) into the logs.
                options.EnableDetailedErrors();
            }
        });

        return services;
    }
}
```
- **Extension method** pattern: `builder.Services.AddDatabase(...)` reads like built-in framework calls and keeps `Program.cs` small.
- **Fail fast**: misconfiguration is detected at startup, not by the first user.

### `src/TaskManagementSystem/appsettings.json`
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

### `src/TaskManagementSystem/appsettings.Development.json`
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=TaskManagementDb;Trusted_Connection=True;TrustServerCertificate=True"
  }
}
```
| Part | Meaning |
|---|---|
| `Server=localhost` | Default SQL Server instance (`MSSQLSERVER`) |
| `Trusted_Connection=True` | Windows authentication — no password in the file |
| `TrustServerCertificate=True` | `Microsoft.Data.SqlClient` encrypts by default; local SQL Server uses a self-signed cert. **Local dev only.** |
| `Database.Command: Information` | EF prints every SQL statement it executes (parameter values stay hidden) — see LINQ → SQL |

### `src/TaskManagementSystem/Program.cs`
```csharp
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
```
`Program.cs` is the **composition root**: section 1 builds the DI container, section 2 builds the middleware pipeline. Every later phase adds lines to these two sections — mostly via extension methods.

### Ports (`Properties/launchSettings.json`, generated)
| Profile | URLs |
|---|---|
| `http` | `http://localhost:5075` |
| `https` | `https://localhost:7188`, `http://localhost:5075` |

---

## 5. Verification results (2026-11-23)

| # | Check | Result |
|---|---|---|
| 1 | `dotnet --version` (in repo folder) | ✅ `10.0.404` |
| 2 | `dotnet build` | ✅ Build succeeded, 0 warnings, 0 errors |
| 3 | `dotnet ef dbcontext info --project src/TaskManagementSystem` | ✅ Provider `Microsoft.EntityFrameworkCore.SqlServer`, database `TaskManagementDb`, data source `localhost` |
| 4 | `dotnet ef database update --project src/TaskManagementSystem` | ✅ `Done.` — empty database created |
| 5 | SQL Server | ✅ `TaskManagementDb` exists with only `__EFMigrationsHistory` table |
| 6 | `GET http://localhost:5075/health` | ✅ `200 Healthy` |
| 7 | `GET http://localhost:5075/openapi/v1.json` | ✅ `200`, OpenAPI 3.1.1 document |

**Expected message (not an error):** `No instantiatable types implementing IEntityTypeConfiguration were found` — appears because there are no entity configurations yet. Disappears in Phase 3.

**Note on SDK install:** the first `winget` attempt failed with exit code `1602` ("You cancelled the installation") because the Windows UAC admin prompt was not approved. Re-running and approving the prompt fixed it.

### How to run it yourself
```powershell
dotnet run --project src/TaskManagementSystem --launch-profile https
# then open https://localhost:7188/health
```

---

## 6. Common mistakes
- Forgetting `global.json` with several SDKs installed → builds silently use SDK 11, templates differ.
- `dotnet-ef` version ≠ EF Core version → confusing "method not found" errors.
- Putting SQL logins with passwords in `appsettings.json` and committing them.
- Registering DbContext as Singleton, or injecting the Scoped DbContext into a Singleton service (**captive dependency**).
- Leaving `EnableSensitiveDataLogging()` on → secrets in logs.
- Missing `TrustServerCertificate=True` locally → `The certificate chain was issued by an authority that is not trusted`.

## 7. Interview questions
1. Why is `DbContext` registered as Scoped? What breaks if it's Singleton?
2. What is the configuration precedence order in ASP.NET Core?
3. What's the difference between `Microsoft.EntityFrameworkCore.Design` and `.Tools`?
4. What does `global.json` with `rollForward: latestFeature` do?
5. What is a composition root?
6. Does adding test projects to the solution make the app "not a monolith"? Why not?

## 8. Exercises
1. Temporarily remove the connection string from `appsettings.Development.json`, run the app, and read the fail-fast error. Restore it.
2. Run `$env:ConnectionStrings__DefaultConnection = "Server=localhost;Database=TmsEnvTest;Trusted_Connection=True;TrustServerCertificate=True"` then `dotnet ef dbcontext info --project src/TaskManagementSystem`. Which database name appears, and why? Clean up with `Remove-Item Env:ConnectionStrings__DefaultConnection`.
3. Stop the SQL Server service, call `/health`, observe the result, restart the service.
