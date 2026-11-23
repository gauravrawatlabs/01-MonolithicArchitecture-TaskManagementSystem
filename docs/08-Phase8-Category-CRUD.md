# Phase 8 — Category CRUD API

> **Phase status:** ✅ Implemented & verified (2026-11-23) — awaiting user review
> **Goal:** Complete the Category module: create, update and delete endpoints, restricted to Admins with a verified email, with validation, correct REST status codes and race-safe uniqueness.

---

## 1. What was already there (Phase 4) and what Phase 8 adds

| Layer | Phase 4 | Phase 8 |
|---|---|---|
| Repository | `GetAll`, `GetById`, `NameExists`, `Add`, `Remove`, `SaveChanges` | — |
| Service | `GetAll`, `GetById`, **`Create`, `Update`, `Delete`** (+ duplicate & race handling) | — |
| Controller | `GET` list, `GET {id}` | **`POST`, `PUT {id}`, `DELETE {id}`** |
| Validation | — | **`CreateCategoryRequestValidator`, `UpdateCategoryRequestValidator`** |
| Authorization | (Phase 7) login required to read | **`CanManageCategories` policy** for writes |

This is the payoff of the layered design: the business logic was written and kept behind an interface; exposing it is a thin controller change plus an authorization decision.

## 2. Concepts

### Composite policy vs. stacked policies
Phase 7 combined rules by **stacking** attributes:
```csharp
[Authorize(Policy = Policies.AdminOnly)]
[Authorize(Policy = Policies.EmailVerified)]
```
Phase 8 defines **one named policy with several requirements**:
```csharp
.AddPolicy(Policies.CanManageCategories, policy => policy
    .RequireAuthenticatedUser()
    .RequireRole(Roles.Admin)
    .RequireVerifiedEmail());          // reusable extension (Authorization/AuthorizationPolicyBuilderExtensions.cs)
```
Both mean AND. The composite policy names the **business capability** ("can manage categories") instead of its ingredients, so the rule can change later (e.g. allow Managers) in one place without touching controllers.

Verified: an account with the Admin role but an **unverified email** gets **403** — both requirements are enforced.

### REST semantics used

| Operation | Method | Success | Body | Notes |
|---|---|---|---|---|
| Create | `POST /api/categories` | **201 Created** | the new category | `Location: /api/categories/{id}` header via `CreatedAtAction` |
| Read | `GET /api/categories/{id}` | 200 | category | |
| Update | `PUT /api/categories/{id}` | 200 | updated category | Full replacement of editable fields (`Description: null` clears it) |
| Delete | `DELETE /api/categories/{id}` | **204 No Content** | — | Second delete → 404 |

`PUT` vs `PATCH`: `PUT` replaces the editable fields as a whole (what we do here); `PATCH` changes only the fields sent (used for `/api/users/{id}/status`).

### Validation layers (defense in depth)
```
1. FluentValidation (400)      Name required (not whitespace), ≤ 100 chars; Description ≤ 500
2. Service pre-check (409)     NameExistsAsync — friendly, fast duplicate error
3. Unique index (409)          IX_Categories_Name — the real guarantee under concurrency
4. Column lengths              nvarchar(100) / nvarchar(500) — last line of defense
```
Shared limits live in `CategoryRules`, so create and update can't drift apart.

### Case-insensitive uniqueness
SQL Server's default collation (`SQL_Latin1_General_CP1_CI_AS`) is **case-insensitive**: `"phase8 testing"` collides with `"Phase8 Testing"` both in `NameExistsAsync` and in the unique index — and renaming a category to a different casing of its **own** name is allowed (`excludeId`).

### Delete and existing tasks
`DELETE` removes only the category. Tasks that referenced it keep existing with `CategoryId = NULL` (FK `ON DELETE SET NULL`, verified in Phase 3). Alternative designs: block the delete (409) while tasks use it, or soft-delete (`IsArchived`).

---

## 3. Files created / changed

```
src/TaskManagementSystem/
├── Authorization/AuthorizationPolicyBuilderExtensions.cs   NEW  RequireVerifiedEmail() building block
├── Constants/Policies.cs                                   CHG  + CanManageCategories
├── Controllers/CategoriesController.cs                     CHG  + POST, PUT {id}, DELETE {id}
├── Extensions/ServiceCollectionExtensions.cs               CHG  CanManageCategories policy; EmailVerified uses the extension
└── Validators/Categories/CategoryValidators.cs             NEW  CategoryRules + create/update validators
```

### Endpoints
| Method | Endpoint | Auth | Success | Errors |
|---|---|---|---|---|
| GET | `/api/categories` | Logged in | 200 | 401 |
| GET | `/api/categories/{id}` | Logged in | 200 | 401, 404 |
| POST | `/api/categories` | `CanManageCategories` | 201 + Location | 400, 401, 403, 409 |
| PUT | `/api/categories/{id}` | `CanManageCategories` | 200 | 400, 401, 403, 404, 409 |
| DELETE | `/api/categories/{id}` | `CanManageCategories` | 204 | 401, 403, 404 |

---

## 4. Request flow — `POST /api/categories` (duplicate under race)

```
Request A ─┐                                    Request B ─┐
           ▼                                               ▼
  JWT valid? → CanManageCategories (Admin ∧ email_verified) → ValidationFilter (FluentValidation)
           ▼                                               ▼
  CategoryService.CreateAsync                     CategoryService.CreateAsync
    NameExistsAsync("X") → false                    NameExistsAsync("X") → false   (both pass!)
    INSERT → ✅ commit                              INSERT → ❌ SqlException 2601 (IX_Categories_Name)
           ▼                                               ▼  DbUpdateException → IsUniqueConstraintViolation
  201 Created + Location                          ConflictException → middleware → 409 ProblemDetails
```

---

## 5. Verification results (2026-11-23)

**Functional matrix — 33/33 behaved correctly** (one test expectation was wrong, see note):

| Area | Checks |
|---|---|
| Authorization | Anonymous POST 401 · User POST/PUT/DELETE 403 · **Admin with unverified email POST 403** · any logged-in user can list 200 |
| Create | 201 with `Location: /api/categories/7` · name/description trimmed · GET via Location 200 · duplicate with different case **409** · empty / whitespace name 400 · 101-char name + 501-char description 400 (both errors listed) |
| Update | Rename 200, `description: null` clears it, `updatedAtUtc` set · same name different case on itself 200 · rename to "BUG" **409** · unknown id 404 · invalid input 400 |
| Delete | 204 · GET afterwards 404 · delete again 404 |
| OpenAPI | `POST /api/categories` documents 201, 400, 403, 409 |

**Note — test expectation corrected:** "POST with **no body and no Content-Type**" returned **415 Unsupported Media Type**, not 400. That is correct HTTP: the server can't parse a body whose media type it doesn't know. (A request with `Content-Type: application/json` and an empty body returns 400 — Phase 5.)

**Race-condition test (proves the Phase 4 safety net):**
- 2 parallel creates: 201 + 409, but the pre-check caught both (requests weren't simultaneous enough) — so the DB path was **not yet** proven.
- 5 bursts × 10 parallel creates with the same name: **exactly one 201 per burst, nine 409s, zero 500s**; the log shows SQL Server's unique-index violations being raised and converted to 409; exactly 5 categories created.

**Observation for Phase 11:** EF Core logs these *handled* unique-constraint failures at **Error** level (`Microsoft.EntityFrameworkCore.Update`). That's log noise — Phase 11 (logging) will tune it.

**Environment note:** Visual Studio was running the app (debug session holding the build output), so tests ran on a separate build output folder on port 5199 instead of stopping the IDE's session.

Cleanup: test user, test categories and the temporary build folder removed; database back to the 5 seeded categories.

---

## 6. Common mistakes
- Returning `200 OK` for creates (instead of `201` + `Location`) or a body for deletes (instead of `204`).
- Validating only in the database (users get 500s) or only in the client.
- Trusting the "check then insert" pre-check without a unique index.
- Case-sensitive uniqueness checks in code while the DB is case-insensitive (or vice-versa).
- Forgetting to exclude the current record when checking for duplicates on update (can't save without renaming).
- Duplicated validation rules in create/update validators that drift apart.
- Accepting whitespace-only names.
- Cascade-deleting tasks when deleting a category.

## 7. Interview questions
1. Which status codes and headers should create / update / delete return, and why?
2. `PUT` vs `PATCH` — when do you use each?
3. Stacked `[Authorize]` attributes vs a composite policy — trade-offs?
4. Why is a pre-check for uniqueness not enough? How do you handle the race?
5. 400 vs 409 vs 422 — which for "name too long", "name already exists", "assignee not in project"?
6. What does `CreatedAtAction` do?
7. Why might a server return 415?
8. How does collation affect uniqueness checks in SQL Server?
9. What are the options when deleting a parent that has children (cascade, set null, restrict, soft delete)?

## 8. Exercises
1. Change `CanManageCategories` so **Managers** can also manage categories — only the policy definition should change. Revert afterwards.
2. Change delete to **block** (409 Conflict) when tasks still use the category. Which layer does the check?
3. Add optimistic concurrency to `Category` (`rowversion` column + `If-Match`/ETag header) so two admins can't overwrite each other's edits silently.
4. Add `GET /api/categories?search=` with a validator (max length 100). (Phase 12 generalizes this.)
