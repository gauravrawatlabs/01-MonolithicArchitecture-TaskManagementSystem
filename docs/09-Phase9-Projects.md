# Phase 9 — Projects & Project Members

> **Phase status:** ✅ Implemented & verified (2026-11-24) — awaiting user review
> **Goal:** Project CRUD and team management with resource-based authorization (owner / member / admin), row-level visibility in lists, and the business rules from the Phase 1 permission matrix.

---

## 1. Rules implemented

| Action | Admin | Owner | Member | Others |
|---|---|---|---|---|
| Create project | ✅ | ✅ (Manager + verified email) | — | ❌ **403** |
| List projects | all | own + member of | member of | none |
| View project | ✅ | ✅ | ✅ | ❌ **404** |
| Update / delete | ✅ | ✅ | ❌ **403** | ❌ **404** |
| Add / remove members | ✅ | ✅ | ❌ **403** | ❌ **404** |

**Business rules**
| Rule | Response |
|---|---|
| Creator becomes owner **and** first member (`MemberRole = Owner`) | — |
| Project names are unique **per owner** (two owners may both have "Apollo") | 409 |
| End date on or after start date (validator + DB check constraint) | 400 |
| A user can be a member only once | 409 |
| Only existing, active users can be added | 422 |
| The owner can't be removed from their project | 422 |
| Removing a member **unassigns their tasks** in that project | — |
| Deleting a project cascades to tasks → comments, and memberships | — |

## 2. Concepts

### Two-level authorization: 404 first, then 403
```
GetAuthorizedProjectAsync(id, user, operation)
  ├─ load project (+ Owner, Members.User)        missing           → 404
  ├─ authorize READ                              not allowed        → 404  (same response as "missing")
  └─ authorize OPERATION (update/delete/…)       not allowed        → 403  (user can see it, but may not do this)
```
Project ids are **guessable integers**. If an outsider got 403 for `/api/projects/7`, they'd learn project 7 exists. Returning **404** for "not visible" makes existing and non-existing projects indistinguishable (verified: identical responses). Members — who legitimately see the project — get an honest **403** when they try to edit it.

`AuthorizeOrNotFoundAsync` (new extension next to `AuthorizeOrThrowAsync`) implements the first check.

### Resource-based handler that fails closed
`ProjectAuthorizationHandler` decides from data on the loaded `Project`:
```csharp
var isOwner  = project.OwnerId == userId;
var isMember = isOwner || project.Members.Any(m => m.UserId == userId);
Read → isMember     Update / Delete / ManageMembers → isOwner     Admin → everything
```
It reads `project.Members`, so the service must load them (`Include`). If someone forgets, the collection is empty and members are **denied**, not accidentally allowed — *fail closed*.

### Row-level visibility in lists
Resource-based checks work on **one** loaded object. For lists, the same rule is applied **in the SQL query**:
```csharp
if (!isAdmin)
    projects = projects.Where(p => p.OwnerId == userId || p.Members.Any(m => m.UserId == userId));
```
Users never receive rows they may not see — filtering in memory after loading would leak data and break paging counts.

### Composite policy with OR inside AND
```csharp
.AddPolicy(Policies.CanCreateProjects, policy => policy
    .RequireAuthenticatedUser()
    .RequireRole(Roles.Admin, Roles.Manager)   // several roles in ONE RequireRole = OR
    .RequireVerifiedEmail());                  // chained requirement = AND
```

### Global role vs. project role
| | Stored in | Example |
|---|---|---|
| Global role | `AspNetUserRoles` (JWT `role` claim) | Manager may **create** projects |
| Project role | `ProjectMembers.MemberRole` | Owner/Member **of this project** |
A User can be a member of many projects; a Manager can be a plain member of someone else's project.

### Queries
| Query | Technique |
|---|---|
| List | `AsNoTracking`, visibility filter, search, `COUNT`, `OrderByDescending(CreatedAtUtc).ThenBy(Id)` for stable paging, projection to `ProjectSummaryResponse` with `Members.Count` / `Tasks.Count` as SQL sub-queries |
| Details | `Include(Owner)`, `Include(Members).ThenInclude(User)`, **`AsSplitQuery()`** (separate SQL per collection instead of one large joined result) |
| Unassign on member removal | `ExecuteUpdateAsync` — one bulk `UPDATE Tasks SET AssignedToId = NULL WHERE …` |

### Transactions
- **Create:** project + owner membership are inserted by **one `SaveChanges`** → EF wraps it in a transaction automatically.
- **Remove member:** a bulk `ExecuteUpdate` (runs immediately) + a `SaveChanges` → explicit transaction so both succeed or neither does.

---

## 3. Files created / changed

```
src/TaskManagementSystem/
├── Authorization/
│   ├── Operations/ProjectOperations.cs           NEW  Read, Update, Delete, ManageMembers
│   └── Handlers/
│       ├── ProjectAuthorizationHandler.cs        NEW  owner / member / admin rules (fails closed)
│       └── UserAuthorizationHandler.cs           CHG  uses TryGetUserId / IsAdmin helpers
├── Constants/Policies.cs                         CHG  + CanCreateProjects
├── Controllers/ProjectsController.cs             NEW  7 endpoints
├── Extensions/
│   ├── AuthorizationServiceExtensions.cs         CHG  + AuthorizeOrNotFoundAsync
│   ├── ClaimsPrincipalExtensions.cs              CHG  + TryGetUserId, IsAdmin
│   └── ServiceCollectionExtensions.cs            CHG  policy, handler, repository, service
├── Interfaces/Repositories/IProjectRepository.cs NEW
├── Interfaces/Services/IProjectService.cs        NEW
├── Mappings/ProjectMappings.cs                   NEW  details + member mapping (owner first)
├── Models/DTOs/Projects/ProjectDtos.cs           NEW  summary, details, member, create/update, add member, query params
├── Repositories/ProjectRepository.cs             NEW
├── Services/ProjectService.cs                    NEW
└── Validators/Projects/ProjectValidators.cs      NEW  name/description limits, end ≥ start, member id, paging
```
No migration — `Projects` and `ProjectMembers` were created in Phase 3.

### Endpoints
| Method | Endpoint | Who | Success |
|---|---|---|---|
| GET | `/api/projects?search=&page=&pageSize=` | Logged in (filtered by visibility) | 200 paged |
| GET | `/api/projects/{id}` | Members, Admin | 200 |
| POST | `/api/projects` | `CanCreateProjects` | 201 + Location |
| PUT | `/api/projects/{id}` | Owner, Admin | 200 |
| DELETE | `/api/projects/{id}` | Owner, Admin | 204 |
| POST | `/api/projects/{id}/members` | Owner, Admin | 200 (project with members) |
| DELETE | `/api/projects/{id}/members/{userId}` | Owner, Admin | 204 |

`DateOnly` fields serialize as `"2026-12-01"`.

---

## 4. Verification results (2026-11-24) — 34/34 passed

Users: **Admin**, **M** (Manager), **U** (User → member), **V** (outsider); all with verified emails.

| Area | Checks |
|---|---|
| Create | U → 403 · M → 201 (Location, trimmed name, dates, M listed as Owner member) · M duplicate "apollo" 409 · Admin "Apollo" (other owner) 201 · end < start 400 · empty name 400 |
| Visibility | M sees 1 · U (not member) sees 0 · Admin sees both · U GET M's project **404** · non-existent id **404** — identical responses |
| Members | Outsider adding himself 404 · M adds U 200 · again 409 · unknown user 422 · empty guid 400 · U reads 200 and now sees 1 (memberCount 2) · U update/delete/add member **403** · owner removal 422 · removing non-member 404 |
| Unassign | Task assigned to U (inserted via SQL) → after removing U: `AssignedToId = NULL` · U then gets 404 |
| Update | Owner 200 · Admin 200 · outsider 404 · invalid dates 400 |
| Delete | Admin deletes M's project 204: tasks 1→0, comments 1→0, members 2→0 (cascade) · GET afterwards 404 |
| Paging | 3 projects, page 2 size 2 → 1 item, totalPages 2, hasPrevious true, hasNext false · pageSize 0 → 400 |

Cleanup: only `phase9-*` test users and their projects removed. Note: SQL Server identity values are never reused — failed inserts (e.g. duplicate names) still consume ids, so ids have gaps. That's normal and harmless.

---

## 5. Common mistakes
- Returning 403 for resources the caller may not even see (leaks existence of guessable ids).
- Filtering lists in memory after loading all rows (data leak + wrong counts/paging).
- Authorization handlers that query the database for every check, or that **succeed** when data isn't loaded (fail open).
- Confusing global roles with per-project roles.
- Global unique project names (one company's "Website" blocks everyone else's) instead of per-owner uniqueness.
- Leaving removed members assigned to tasks.
- Two writes without a transaction.
- Paging without a deterministic `ORDER BY` (rows jump between pages).
- `Include` of several collections without `AsSplitQuery` (cartesian explosion on large teams).

## 6. Interview questions
1. Why return 404 instead of 403 when a user can't see a resource? When is 403 still right?
2. How do you apply authorization to a **list** endpoint? Why not filter in memory?
3. What does "fail closed" mean for an authorization handler?
4. How do you express (A OR B) AND C in ASP.NET Core policies?
5. Global roles vs. resource-level roles — how would you model "Owner of project X"?
6. What is a cartesian explosion and how does `AsSplitQuery` help?
7. When does EF Core use a transaction automatically, and when do you need an explicit one?
8. Why might identity column values have gaps?

## 7. Exercises
1. Add `POST /api/projects/{id}/transfer-ownership` (`{ newOwnerId }`): only the owner or an Admin; the new owner must be a member and a Manager/Admin; swap the `MemberRole` values in one transaction.
2. Allow members to **leave** a project themselves: `DELETE /api/projects/{id}/members/me`.
3. Add `?ownedByMe=true` to the project list.
4. Add a `ProjectAuthorizationHandler` unit test (Phase 13 preview): build a `Project` with members in memory and check each operation for Admin/Owner/Member/Outsider.
