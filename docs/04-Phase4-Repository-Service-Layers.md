# Phase 4 — Repository & Service Layers, Exception Handling, Swagger UI

> **Phase status:** ✅ Implemented & verified (2026-11-23) — awaiting user review
> **Goal:** Build the layered request path (Controller → Service → Repository → DbContext) with the **Category** module as the reference implementation, add global exception handling with ProblemDetails, and add **Swagger UI** so the API can be explored and tested in the browser.

---

## 1. Scope decisions for this phase

| Decision | Reason |
|---|---|
| **Category is the reference module** built end-to-end through all layers | Project/Task/Comment services depend on the *current user* and authorization (Phases 5–7). Building them now would mean rewriting them later. Category shows the full pattern; Phases 9–10 repeat it. |
| Only **GET** category endpoints are exposed | Create/Update/Delete must be Admin-only. They exist in the service already, but their endpoints arrive in Phase 8 together with `[Authorize]` policies. Never expose write endpoints without authorization. |
| **Swagger UI added now** (originally Phase 14) | User request: be able to see and test APIs in the browser. Phase 14 adds the JWT "Authorize" button and final polish. |
| Basic **exception middleware now** (Decision #6) | Services throw exceptions from this phase on; Phase 11 extends the middleware with validation errors, Serilog and correlation IDs. |

---

## 2. Concepts

### Layers and their single responsibilities

```
HTTP ─► CategoriesController   "HTTP adapter": route, bind input, return status code. No logic.
            │  CategoryResponse / CreateCategoryRequest (DTOs)
            ▼
        CategoryService        Business rules: uniqueness, timestamps, not-found, mapping.
            │  Category (entity)
            ▼
        CategoryRepository     Data access only: LINQ queries, Add/Remove, SaveChanges.
            │
            ▼
        ApplicationDbContext   EF Core Unit of Work → SQL Server
```

| Layer | Knows about | Must NOT know about |
|---|---|---|
| Controller | HTTP, DTOs, service interface | EF Core, entities, SQL |
| Service | DTOs, entities, repository interfaces, business rules | HTTP (`HttpContext`, status codes) |
| Repository | Entities, DbContext, LINQ | Business rules, DTOs, HTTP |

### Why a Repository on top of EF Core?
`DbContext` is already a Unit of Work + generic repository. We still add a thin, **specific** repository because:
- Services depend on an **interface** (`ICategoryRepository`) → easy to mock in unit tests (Phase 13).
- Query details (`AsNoTracking`, `OrderBy`, `Include`) live in one place.
- No generic `IRepository<T>` and no separate `UnitOfWork` class — those add abstraction without value on top of EF Core.

### Interface-based Dependency Injection
```csharp
services.AddScoped<ICategoryService, CategoryService>();   // "when someone asks for ICategoryService, give a CategoryService"
public sealed class CategoriesController(ICategoryService categoryService)   // constructor injection
```
The controller depends on the **abstraction**, not the concrete class (Dependency Inversion — the "D" in SOLID). The DI container builds the whole object graph per request:

```
Request scope
 └─ CategoriesController
     └─ CategoryService            (Scoped)
         ├─ CategoryRepository     (Scoped)
         │   └─ ApplicationDbContext (Scoped)  ← same instance for the whole request
         ├─ TimeProvider           (Singleton)
         └─ ILogger<CategoryService> (Singleton)
```

### DI lifetimes — what we use and why

| Lifetime | Instance per | Used for | Why |
|---|---|---|---|
| **Singleton** | Application | `TimeProvider`, loggers, `IProblemDetailsService` | Stateless + thread-safe; one shared instance is enough |
| **Scoped** | HTTP request | `DbContext`, repositories, services | Repositories hold the DbContext; services hold repositories; everything in one request shares one Unit of Work |
| **Transient** | Every injection | (none yet) | Lightweight stateless helpers; FluentValidation validators in Phase 8/11 |

**Captive dependency** (a common bug): a Singleton that depends on a Scoped service keeps that one instance forever → one DbContext shared across all requests and threads. In Development, ASP.NET Core's *scope validation* throws at startup if you do this.

**Middleware lifetime:** conventional middleware (`ExceptionHandlingMiddleware`) is created **once**. Its constructor may only take singletons; scoped services must be parameters of `InvokeAsync`.

### `TimeProvider`
Services call `timeProvider.GetUtcNow()` instead of `DateTime.UtcNow`. In production it's `TimeProvider.System`; in unit tests we pass a `FakeTimeProvider` with a fixed time, so timestamp assertions are deterministic.

### Tracking vs. no-tracking queries
- `AsNoTracking()` — read-only lists: EF doesn't keep snapshots → faster, less memory.
- Tracked (`GetByIdAsync`) — for updates: change a property, call `SaveChangesAsync()`, EF generates the `UPDATE` with only changed columns.

### DTOs as records + manual mapping
`CategoryResponse`, `CreateCategoryRequest`, `UpdateCategoryRequest` are immutable `record`s. `category.ToResponse()` (in `Mappings/`) maps explicitly — no AutoMapper.

### Exceptions as control flow for *expected* errors
Services throw typed exceptions; one middleware converts them to HTTP responses:

| Exception | Status | When |
|---|---|---|
| `NotFoundException` | 404 | Id doesn't exist |
| `ConflictException` | 409 | Duplicate name, invalid status transition |
| `ForbiddenException` | 403 | Authenticated but not allowed (Phase 7) |
| `BusinessRuleException` | 422 | Valid input that breaks a rule (e.g. assignee not a project member) |
| *anything else* | 500 | Bug / infrastructure failure — generic message only |

401 (not logged in) and 400 (validation) come from the authentication middleware (Phase 5) and FluentValidation (Phase 11).

### ProblemDetails (RFC 9457)
Every error has the same JSON shape, so clients handle errors uniformly:
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Resource not found",
  "status": 404,
  "detail": "Category with id '999' was not found.",
  "instance": "GET /api/categories/999",
  "traceId": "00-1e74955f15882121768f0a54412244bd-e27f00d5ff72b1d0-00"
}
```
`traceId` lets support find the matching server log entry without exposing internals.

### Race conditions and the unique index
`NameExistsAsync` gives a fast, friendly error, but two simultaneous requests can both pass it. The **unique index** is the real guarantee; `DbExceptionHelper.IsUniqueConstraintViolation` turns SQL errors 2601/2627 into the same 409 Conflict.

### OpenAPI vs. Swagger UI
| Piece | Package | URL | What |
|---|---|---|---|
| OpenAPI document | `Microsoft.AspNetCore.OpenApi` (built into .NET 10) | `/openapi/v1.json` | Machine-readable description of every endpoint, generated from controllers + XML comments |
| Swagger UI | `Swashbuckle.AspNetCore.SwaggerUI` 10.3.1 | `/swagger` | Interactive web page that reads the document and lets you "Try it out" |

`<GenerateDocumentationFile>true</GenerateDocumentationFile>` makes `/// <summary>` comments appear as endpoint descriptions. Both are **Development-only**.

---

## 3. Files created / changed

```
src/TaskManagementSystem/
├── Controllers/CategoriesController.cs           NEW  GET /api/categories, GET /api/categories/{id}
├── Data/
│   ├── ApplicationDbContext.cs                   CHG  + UtcDateTimeConverter convention
│   └── Converters/UtcDateTimeConverter.cs        NEW  DateTime read from DB → Kind = Utc ("Z" in JSON)
├── Exceptions/
│   ├── AppException.cs                           NEW  base: StatusCode + Title
│   ├── NotFoundException.cs                      NEW  404 (+ For(resource, id) helper)
│   ├── ConflictException.cs                      NEW  409
│   ├── ForbiddenException.cs                     NEW  403
│   └── BusinessRuleException.cs                  NEW  422
├── Extensions/
│   ├── ServiceCollectionExtensions.cs            CHG  + AddApplicationServices, AddApiDocumentation
│   └── WebApplicationExtensions.cs               NEW  UseApiDocumentation (OpenAPI + Swagger UI, Dev only)
├── Helpers/DbExceptionHelper.cs                  NEW  detects unique-constraint SQL errors
├── Interfaces/
│   ├── Repositories/ICategoryRepository.cs       NEW
│   └── Services/ICategoryService.cs              NEW
├── Mappings/CategoryMappings.cs                  NEW  Category → CategoryResponse
├── Middleware/ExceptionHandlingMiddleware.cs     NEW  exceptions → ProblemDetails
├── Models/DTOs/Categories/
│   ├── CategoryResponse.cs                       NEW
│   ├── CreateCategoryRequest.cs                  NEW
│   └── UpdateCategoryRequest.cs                  NEW
├── Repositories/CategoryRepository.cs            NEW
├── Services/CategoryService.cs                   NEW  Get/GetAll/Create/Update/Delete
├── Properties/launchSettings.json                CHG  opens /swagger on run
├── Program.cs                                    CHG  middleware + new registrations
└── TaskManagementSystem.csproj                   CHG  + SwaggerUI package, XML docs
```

### Program.cs pipeline now
```csharp
app.UseMiddleware<ExceptionHandlingMiddleware>();   // outermost: catches exceptions from everything below
app.UseStatusCodePages();                           // empty 4xx responses (e.g. unknown URL) → ProblemDetails
app.UseApiDocumentation();                          // Development only: /openapi/v1.json + /swagger
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
```

---

## 4. Request flow — `GET /api/categories/999`

```
Client ─► ExceptionHandlingMiddleware  (try { await next(context) })
            ─► StatusCodePages ─► Routing ─► CategoriesController.GetById(999)
                 ─► CategoryService.GetByIdAsync(999)
                      ─► CategoryRepository.GetByIdAsync(999) ─► SQL: SELECT TOP(1) ... WHERE Id = @id
                      ◄─ null
                      throw NotFoundException("Category with id '999' was not found.")
            ◄─ exception bubbles up through controller (no try/catch there)
          catch (AppException) → 404 + ProblemDetails JSON, log Warning
Client ◄─ 404 application/problem+json
```

---

## 5. How to use Swagger

```powershell
cd "D:\MyProjects\ASP DotNet Core\Monolithic Architecture\MonolithicArchitecture\01-MonolithicArchitecture-TaskManagementSystem"
dotnet run --project src/TaskManagementSystem --launch-profile https
```
The browser opens **https://localhost:7188/swagger** automatically (or open it manually).

1. Expand **Categories → GET /api/categories** → **Try it out** → **Execute** → see the 5 seeded categories.
2. **GET /api/categories/{id}** → enter `1` → Execute → 200.
3. Enter `999` → Execute → **404 ProblemDetails**.
4. Look at the console window running the app: you'll see the SQL EF Core executed and the warning log for the 404.

In Visual Studio: open `TaskManagementSystem.slnx`, choose the **https** profile, press **F5** — Swagger opens.

---

## 6. Verification results (2026-11-23)

| # | Test | Result |
|---|---|---|
| 1 | `dotnet build` | ✅ 0 warnings, 0 errors |
| 2 | `GET /api/categories` | ✅ 200, 5 categories ordered by name |
| 3 | `GET /api/categories/1` | ✅ 200 `{"id":1,"name":"Bug",...}` |
| 4 | `GET /api/categories/999` | ✅ 404 `application/problem+json`, detail "Category with id '999' was not found." |
| 5 | `GET /api/categories/abc` (route constraint `{id:int}`) | ✅ 404 ProblemDetails (via `UseStatusCodePages`) |
| 6 | `GET /api/does-not-exist` | ✅ 404 ProblemDetails |
| 7 | `/swagger/index.html` (Development) | ✅ 200 |
| 8 | OpenAPI document | ✅ title "Task Management System API", both paths with XML-comment summaries |
| 9 | Dates in JSON | ✅ `"2026-01-01T00:00:00Z"` (after UTC converter fix; `DateTime?` too) |
| 10 | `has-pending-model-changes` after converter | ✅ No model changes (converter needs no migration) |
| 11 | **Production** + unreachable DB → `GET /api/categories` | ✅ 500 with generic message + traceId only — no server name, SQL or stack trace |
| 12 | `/swagger` in **Production** | ✅ 404 (hidden) |
| 13 | Server log in Production | ✅ Full exception logged ("Unhandled exception while processing GET /api/categories") |

### Bug found & fixed during verification
JSON dates came back as `"2026-01-01T00:00:00"` (no `Z`). SQL Server `datetime2` stores no time zone, so EF returned `DateTimeKind.Unspecified`. Fixed with `UtcDateTimeConverter` applied to all `DateTime` properties via `ConfigureConventions`.

---

## 7. Common mistakes
- Business logic in controllers ("fat controllers") or in repositories.
- Returning entities from controllers instead of DTOs.
- `try/catch` in every action just to return 404/500 — centralize it.
- Returning `exception.Message`/stack trace for unexpected errors in production (leaks SQL, server names, file paths).
- Registering a repository as Singleton (captive DbContext).
- Injecting scoped services into a middleware **constructor**.
- Using `DateTime.Now` (local time) or `DateTime.UtcNow` directly in services (untestable).
- Relying only on "check then insert" for uniqueness — race conditions; the DB constraint is the real guard.
- Generic repository + Unit of Work wrappers around EF Core that add nothing.
- Forgetting `CancellationToken` — the DB keeps working for clients that already left.

## 8. Interview questions
1. What belongs in a controller, a service and a repository?
2. Is the Repository pattern useful with EF Core? Argue both sides.
3. Explain Transient, Scoped and Singleton with examples. What is a captive dependency?
4. Why can't you inject a scoped service into a middleware constructor?
5. What is ProblemDetails and why standardize error responses?
6. Exceptions vs. result objects for expected errors — trade-offs?
7. How do you avoid leaking sensitive information in error responses?
8. What does `AsNoTracking()` do and when should you use it?
9. Why use `TimeProvider` instead of `DateTime.UtcNow`?
10. How do you handle the race condition in "check if name exists, then insert"?
11. Difference between the OpenAPI document and Swagger UI?

## 9. Exercises
1. In Swagger, call `GET /api/categories/999` and find the matching log line in the console using the `traceId`.
2. Temporarily register `CategoryRepository` as **Singleton** and run the app. Read the startup error (scope validation), then revert.
3. Add a `GET /api/categories/by-name/{name}` endpoint through all three layers (interface → repository → service → controller). Remove it afterwards or keep it as practice.
4. Throw `new InvalidOperationException("boom")` in `CategoryService.GetAllAsync`, call the endpoint in Development and then in Production (`$env:ASPNETCORE_ENVIRONMENT="Production"`, `dotnet run --no-launch-profile`). Compare the responses. Revert.
