# Phase 16 — Frontend Support Endpoints

> Status: ✅ Verified (2026-11-26) · 151 unit + 37 integration tests passing.
> Why: designing the React frontend (frontend repo, Phase 1, gaps **G1** and **G2**) showed two things the UI
> needs that the API could not provide. Both are small, additive changes; the backend stays one monolith and
> no existing contract was removed.

## G1 — `GET /api/users/lookup`: find users to add as project members

**Problem.** `POST /api/projects/{id}/members` needs a `userId`, but the only way to search users
(`GET /api/users`) is Admin-only. A Manager who owns a project had no way to find a teammate.

**Solution.** A small, separate, read-only endpoint for *autocomplete*:

| | |
|---|---|
| Route | `GET /api/users/lookup?search=ja&limit=10` |
| Policy | `CanLookupUsers` = (Admin **or** Manager) **and** verified email |
| Query | `search` required, ≥ 2 characters (trimmed), ≤ 100; `limit` 1–20 (default 10) |
| Result | `UserLookupResponse[]` → `{ id, fullName, email, jobTitle }` (not paged) |
| Filter | **active** users only (inactive users can't be added to projects), ordered by name |

**Why not just open `GET /api/users` to Managers?** It returns roles, account status and login times:
admin data that a Manager doesn't need. The principle of *least privilege* says expose the smallest shape
that solves the problem. The minimum search length and result limit stop the endpoint being used to dump
the whole user list.

**Files:** `Constants/Policies.cs`, `Extensions/ServiceCollectionExtensions.cs` (policy),
`Models/DTOs/Users/UserLookupResponse.cs`, `Models/DTOs/Users/UserRequests.cs` (`UserLookupQueryParameters`),
`Validators/Users/UserValidators.cs`, `Interfaces/Repositories/IUserRepository.cs`, `Repositories/UserRepository.cs`,
`Interfaces/Services/IUserService.cs`, `Services/UserService.cs`, `Controllers/UsersController.cs`.

### Lesson learned: FluentValidation `.When()` scope
```csharp
RuleFor(x => x.Search)
    .NotEmpty()
    .Must(s => s!.Trim().Length >= 2)
        .When(x => !string.IsNullOrWhiteSpace(x.Search), ApplyConditionTo.CurrentValidator)
```
Without `ApplyConditionTo.CurrentValidator`, `.When()` applies to **every rule before it in the chain**.
That silently disabled `NotEmpty()`, so a request without `search` reached the repository and returned a **500**.
The new tests caught it (`UserLookup_InvalidQuery_Returns400ProblemDetails`).

## G2 — progress counts on `GET /api/projects`

**Problem.** Project cards should show a progress bar, but `ProjectSummaryResponse` only had `taskCount`.
The frontend would have needed one extra request per card (an N+1 problem over HTTP).

**Solution.** Three new fields on `ProjectSummaryResponse` (added after `taskCount`):

| Field | Meaning |
|---|---|
| `openTaskCount` | not Done and not Cancelled |
| `doneTaskCount` | Done |
| `completionPercent` | Done ÷ (Total − Cancelled) × 100, 1 decimal; 0 when there is nothing to do |

The counters are correlated `COUNT` sub-queries in the same SQL statement (no tasks are loaded). The percentage
is computed in C# by **`Helpers/ProjectProgress.CompletionPercent`**, which the dashboard now uses too. One formula
in one place means the dashboard and the project list can never disagree.

**Compatibility:** adding fields to a JSON response is a non-breaking change for clients that ignore unknown properties.

## Tests added (+21)

| Test | Type | Checks |
|---|---|---|
| `ProjectProgressTests` (6 cases) | unit | empty project, all done, cancelled excluded, rounding, only-cancelled |
| `UserLookup_*` validator tests (7 cases) | unit | missing/short/whitespace search, limit 0 and 21, valid query |
| `UserLookup_IsForVerifiedAdminsAndManagersOnly` | integration | User 403, unverified Manager 403, Manager 200, Admin 200, anonymous 401 |
| `UserLookup_FindsActiveUsersByName_ExcludesInactive_AndHonoursLimit` | integration | matching, inactive excluded, `limit`, empty result |
| `UserLookup_InvalidQuery_Returns400ProblemDetails` (4 cases) | integration | 400 + `errors` object |
| `UserLookup_ResultCanBeAddedAsProjectMember` | integration | lookup → add member end to end |
| `ProjectList_ReturnsTaskProgress_SameFormulaAsDashboard` | integration | 4 tasks, 1 done, 1 cancelled → 33.3 %; empty project → 0 |
| OpenAPI contract list | integration | `GET /api/users/lookup` documented |

## Interview questions
1. Why add a new endpoint instead of loosening the policy on `GET /api/users`?
2. Why is the percentage computed in C# rather than in SQL here?
3. Is adding a field to a response DTO a breaking change? When could it be one?
