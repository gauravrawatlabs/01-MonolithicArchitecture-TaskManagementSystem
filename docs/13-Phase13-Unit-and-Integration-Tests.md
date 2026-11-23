# Phase 13 — Unit Tests & Integration Tests

> **Phase status:** ✅ Implemented & verified (2026-11-24) — awaiting user review
> **Goal:** An automated safety net: fast unit tests for business rules and services (xUnit + Moq), and integration tests that run the real HTTP pipeline against a dedicated SQL Server test database (WebApplicationFactory).

---

## 1. Results

```
dotnet test --settings coverlet.runsettings --collect:"XPlat Code Coverage"

Passed!  - Failed: 0, Passed: 138, Skipped: 0, Total: 138, Duration: 338 ms  - TaskManagementSystem.UnitTests.dll
Passed!  - Failed: 0, Passed:  20, Skipped: 0, Total:  20, Duration: 22 s    - TaskManagementSystem.IntegrationTests.dll

Merged coverage (application code, migrations excluded):  Line 86.9 %  ·  Branch 62 % (144 of 232)
Build: 0 warnings, 0 errors
```

## 2. Concepts

### The test pyramid
```
            ▲  fewer, slower, more realistic
           ╱ ╲        Integration tests (20)  — real HTTP pipeline + real SQL Server, ~22 s
          ╱   ╲
         ╱─────╲      Unit tests (138)       — pure logic, no I/O, ~0.3 s
        ╱       ╲
       ▼  many, fast, isolated
```
| | Unit test | Integration test |
|---|---|---|
| Scope | One class (service, validator, handler, state machine) | Whole request: middleware → auth → controller → service → EF Core → SQL Server |
| Dependencies | Mocked / faked / real-but-pure | Real (only email sender and config replaced) |
| Speed | Milliseconds | Seconds |
| Finds | Logic errors in rules | Wiring errors: DI, middleware order, routing, JSON, SQL, migrations, auth config |

### Arrange · Act · Assert
Every test has three visible blocks:
```csharp
[Fact]
public async Task CreateAsync_WhenNameAlreadyExists_ThrowsConflict_AndSavesNothing()
{
    // Arrange — set up the world
    _repository.Setup(r => r.NameExistsAsync("Bug", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);
    var service = CreateService();

    // Act — do exactly one thing
    var act = () => service.CreateAsync(new CreateCategoryRequest("  Bug  ", null), CancellationToken.None);

    // Assert — check the outcome (and important side effects)
    await Assert.ThrowsAsync<ConflictException>(act);
    _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
}
```
Naming: `Method_Scenario_ExpectedResult` — a failing test name already explains the bug.

### Test doubles — which one where
| Double | Used for | Why |
|---|---|---|
| **Mock** (Moq) | Repositories, `UserManager`, `SignInManager` | Control return values and **verify interactions** ("SaveChanges never called") |
| **Fake** | `FakeTimeProvider`, `CapturingEmailSender` | Working lightweight implementation: deterministic clock; in-memory email inbox |
| **Real** | `TokenService`, authorization handlers, `IAuthorizationService` | Pure code without I/O — mocking it would only test the mock |

**Sociable unit tests:** `TestAuthorization.Create()` builds the *real* `IAuthorizationService` with the *real* handlers. Service tests therefore verify "outsider gets 404, member gets 403" exactly as production behaves.

**Why `TimeProvider` paid off:** since Phase 4 every service asks `TimeProvider` for "now". Tests pass `new FakeTimeProvider(2026-11-24 10:00 UTC)` and can assert exact timestamps (`CreatedAtUtc`, `CompletedAtUtc`, token `exp`) and move time with `_clock.Advance(...)`.

**Mocking concrete Identity classes:** `UserManager<T>`/`SignInManager<T>` aren't interfaces, but their methods are `virtual`, so Moq can override them (`TestHelpers/MockIdentity.cs`).

### Data-driven tests
- `[Theory] + [InlineData]` for small tables (password rules, permission cells).
- `[MemberData]` for generated cases: **all 25 (from, to) status pairs** are checked against an *independent copy* of the workflow specification — add a transition in code without updating the spec and a test fails.

### WebApplicationFactory — the app in memory
```
CustomWebApplicationFactory : WebApplicationFactory<Program>
  ├─ UseEnvironment("Testing")            → no Swagger, no User Secrets, JSON logs
  ├─ UseSetting(connection string, JWT key, admin seed)   ← visible to Program.cs immediately
  ├─ ConfigureTestServices: IEmailSender → CapturingEmailSender
  └─ InitializeAsync: guard → EnsureDeleted → Migrate   (once per run)
Factory.CreateClient() → HttpClient talking to an in-memory TestServer (no ports, no network)
```
- **`UseSetting` vs `ConfigureAppConfiguration`:** `Program.cs` reads configuration **before** `builder.Build()` (`AddDatabase`, `AddCorsPolicy`). `UseSetting` values are visible at that point; configuration added through `ConfigureAppConfiguration` would arrive too late with minimal hosting.
- **`Program` accessibility:** .NET 10 makes the top-level `Program` class public for test projects automatically — no `public partial class Program {}` needed.
- **Code after `Build()` still runs** (seeding the admin) — the factory waits for the app to call `Run`.

### Test database strategy (Decision #5)
| Choice | Reason |
|---|---|
| Separate **SQL Server** database `TaskManagementDb_IntegrationTests` | Same engine as production: real constraints, cascades, filtered indexes, `ExecuteUpdate`, collation. EF InMemory / SQLite would silently behave differently. |
| Recreated from **migrations** each run | Also tests that the migrations build a working schema. |
| **Guard** before `EnsureDeleted` | Refuses to drop any database whose name isn't the test database. A test also asserts the running app uses it. |
| One shared app + DB per run (`ICollectionFixture`) | Fast (one startup). Tests stay independent by creating their own users/projects with unique emails — never relying on data from another test. |
| DB kept after the run | Inspect it in SSMS when a test fails; the next run recreates it. |

Verified after the run: development DB unchanged (2 users, 0 projects); test DB has 22 users.

### Replacing a service for one test
```csharp
await using var crashingApp = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
{
    s.RemoveAll<ICategoryService>();
    s.AddScoped<ICategoryService, CrashingCategoryService>();   // throws "Server=secret-db;Password=hunter2"
}));
```
→ proves a real 500 returns only the generic message: no `secret-db`, no exception type.

### Code coverage
- `coverlet.collector` gathers coverage per test project; `coverlet.runsettings` limits it to the app assembly and **excludes EF migrations** (generated code would dilute the numbers).
- **ReportGenerator** (local tool) merges both reports into one HTML + text summary in `TestResults/CoverageReport` (gitignored).
- Coverage shows **what was executed, not what was verified** — a guide to gaps, not a goal. Known gaps: `DbExceptionHelper` (race path — verified manually in Phase 8), `IdentityErrorMapper` error branches, `PickupDirectoryEmailSender` (replaced in tests), some update/query validators.

---

## 3. What is tested

### Unit tests (138, incl. theory cases)
| File | Covers |
|---|---|
| `BusinessRules/TaskStatusWorkflowTests` | All 25 transitions vs. spec, reopen flags, next statuses, `IsClosed` |
| `Authorization/ResourceAuthorizationHandlerTests` | Project / Task / Comment permission matrices cell by cell; **fail-closed** when navigation not loaded; removed creator loses rights |
| `Validators/ValidatorTests` | Required, email, length, each password rule, new ≠ current password, **due date vs. fake clock** (past, exactly now, future), end ≥ start, dueFrom ≤ dueTo, inherited paging rules, duplicate/unknown roles |
| `Services/CategoryServiceTests` | Not found, duplicate → 409 without saving, trimming, timestamps from clock, uniqueness excludes itself |
| `Services/TaskServiceTests` | Invalid transitions → 409 (no save), CompletedAt set/cleared, reopen 403 vs owner OK, other member 403, **outsider 404**, assign to non-member/inactive → 422, defaults on create, due date after project end → 422, unknown category, past due on update → 400, overdue task editable |
| `Services/ProjectServiceTests` | Outsider 404, member read (owner first), member update **403**, duplicate member 409, inactive user 422, owner removal 422 |
| `Services/AuthServiceTests` | Unknown/inactive/locked/wrong password → identical generic 401; success stores **only the hash**; reuse of rotated token revokes all; logout-revoked token doesn't; expired token; logout idempotent |
| `Services/TokenAndDashboardServiceTests` | JWT claims, roles, `exp`, signature valid / forged-key invalid; refresh token size/hash/expiry/uniqueness; dashboard zero-filling and Admin vs. user scope |

### Integration tests (20)
| File | Covers |
|---|---|
| `AuthenticationTests` | Register 201 + email; duplicate 409; wrong password 401; `/me` without / with / tampered token; refresh rotation + **reuse revokes all**; logout; email verification unlocks writes (403 → 200); password reset end-to-end |
| `AuthorizationTests` | Admin-only 403/200; category CRUD (403, 201 + Location, 409, 204, 404); project creation Manager-only; profile self/other/admin |
| `ProjectAndTaskApiTests` | Full lifecycle across modules (visibility 404, create, assign 403/200/422, workflow 409/200/403, comments, dashboard scope, cascade delete); task list visibility + **priority sort order** |
| `ErrorAndInfrastructureTests` | App uses the test DB; health + correlation id; validation ProblemDetails shape; camelCase binding keys without type names; 404 resource/route; **500 without leaking secrets** |

---

## 4. Files created / changed

```
coverlet.runsettings                                   NEW  coverage scope (app only, no migrations)
dotnet-tools.json                                      CHG  + dotnet-reportgenerator-globaltool 5.6.1
tests/TaskManagementSystem.UnitTests/
├── TaskManagementSystem.UnitTests.csproj              CHG  + Moq 4.21.0, Microsoft.Extensions.TimeProvider.Testing
├── TestHelpers/TestUsers.cs, TestData.cs, TestAuthorization.cs, MockIdentity.cs   NEW
├── BusinessRules/TaskStatusWorkflowTests.cs           NEW
├── Authorization/ResourceAuthorizationHandlerTests.cs NEW
├── Validators/ValidatorTests.cs                       NEW
└── Services/CategoryServiceTests.cs, TaskServiceTests.cs, ProjectServiceTests.cs,
             AuthServiceTests.cs, TokenAndDashboardServiceTests.cs                  NEW
tests/TaskManagementSystem.IntegrationTests/
├── TaskManagementSystem.IntegrationTests.csproj       CHG  + Microsoft.AspNetCore.Mvc.Testing 10.0.15
├── Infrastructure/CustomWebApplicationFactory.cs      NEW  (+ IntegrationTestCollection)
├── Infrastructure/CapturingEmailSender.cs             NEW
├── Infrastructure/IntegrationTestBase.cs              NEW
└── AuthenticationTests.cs, AuthorizationTests.cs, ProjectAndTaskApiTests.cs, ErrorAndInfrastructureTests.cs   NEW
(template UnitTest1.cs files removed)
```

No application code changed in this phase — the code was already testable (interfaces, DI, `TimeProvider`, pure handlers).

## 5. Running the tests

```powershell
dotnet test                                                     # everything
dotnet test tests/TaskManagementSystem.UnitTests                # unit tests only (no database needed)
dotnet test --filter "FullyQualifiedName~TaskServiceTests"      # one class
dotnet test --filter "FullyQualifiedName~Refresh"               # by name

# Coverage report (merged HTML in TestResults/CoverageReport/index.html)
dotnet tool restore
dotnet test --settings coverlet.runsettings --collect:"XPlat Code Coverage" --results-directory TestResults
dotnet reportgenerator "-reports:TestResults/**/coverage.cobertura.xml" "-targetdir:TestResults/CoverageReport" "-reporttypes:Html;TextSummary"
```
Integration tests need the local SQL Server (`localhost`, Windows authentication). In Visual Studio: **Test → Test Explorer → Run All**. Stop a running debug session first (it locks the build output).

### Problems hit while building the tests
- `coverlet.runsettings` first failed with *"An XML comment cannot contain '--'"* — the comment contained `dotnet test --settings`. Reworded.
- First coverage numbers (13 % / 70 %) were misleading: two separate reports **and** generated migration code counted. Fixed with the runsettings filter + ReportGenerator merge → 86.9 %.

---

## 6. Common mistakes
- Integration tests against the development/production database (no guard).
- Using EF InMemory for "integration" tests — no SQL, no constraints, no transactions.
- `DateTime.UtcNow` inside services → flaky, untestable time logic.
- Tests depending on each other's data or execution order.
- Mocking everything, including pure logic — tests then only check the mocks.
- Asserting only the happy path; no tests for 401/403/404/409/422.
- One giant assertion-less test ("it didn't throw").
- Treating coverage % as the goal instead of a map of untested code.
- Leaving secrets in test configuration that mirror production values.
- Forgetting to verify side effects (`SaveChanges` never called on failure).

## 7. Interview questions
1. Difference between unit and integration tests? What does each catch that the other can't?
2. Explain Arrange-Act-Assert. Why one "Act" per test?
3. Mock vs. stub vs. fake — examples from this project?
4. What is a sociable unit test and when is it better than mocking?
5. How do you test time-dependent code?
6. How does `WebApplicationFactory` work? How do you replace a service for a test?
7. Why use a real SQL Server for integration tests instead of EF InMemory?
8. How do you keep integration tests independent when they share one database?
9. Why might configuration set with `ConfigureAppConfiguration` not be visible to `Program.cs`?
10. What does code coverage tell you — and what doesn't it?
11. How would you mock `UserManager<T>`?
12. How would you run these tests in CI?

## 8. Exercises
1. Add an integration test that fires 10 parallel `POST /api/categories` with the same name and asserts exactly one 201 and no 500 (covers `DbExceptionHelper`).
2. Add unit tests for `IdentityErrorMapper` (each error code → field key).
3. Switch the integration database to **Testcontainers for SQL Server** (`Testcontainers.MsSql`) so tests run on any machine with Docker.
4. Use **Respawn** to reset data between test classes instead of unique emails.
5. Add a GitHub Actions workflow running unit tests on every push (integration tests with a SQL Server service container).
