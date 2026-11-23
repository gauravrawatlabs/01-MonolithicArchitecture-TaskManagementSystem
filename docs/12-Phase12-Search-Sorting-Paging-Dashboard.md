# Phase 12 — Search, Filtering, Sorting, Pagination & Dashboard

> **Phase status:** ✅ Implemented & verified (2026-11-24) — awaiting user review
> **Goal:** Make every list practical for real data volumes (search, filters, safe sorting, stable paging) and add a dashboard whose numbers respect each user's visibility — all computed by the database.

---

## 1. What each list supports now

| Endpoint | Search | Filters | `sortBy` (default) | Default direction |
|---|---|---|---|---|
| `GET /api/tasks` | title, description | `projectId`, `status`, `priority`, `categoryId`, `assignedToId`, `assignedToMe`, `dueFrom`, `dueTo`, `overdue` | `DueDate`, `Priority`, `Status`, `Title`, `CreatedAt`, `UpdatedAt` (DueDate) | Asc; Desc for Priority / CreatedAt / UpdatedAt |
| `GET /api/projects` | name, description | `ownedByMe` | `CreatedAt`, `Name`, `StartDate`, `EndDate` (CreatedAt) | Desc for CreatedAt, else Asc |
| `GET /api/users` (Admin) | email, names | `role`, `isActive` | `Email`, `Name`, `CreatedAt`, `LastLogin` (Email) | Desc for CreatedAt / LastLogin, else Asc |
| `GET /api/tasks/{id}/comments` | — | — | oldest first | — |

All accept `page` (≥ 1) and `pageSize` (1–100) and return `PagedResult<T>`: `items, page, pageSize, totalCount, totalPages, hasPreviousPage, hasNextPage`.

Examples:
```
GET /api/tasks?assignedToMe=true&overdue=true
GET /api/tasks?projectId=9&sortBy=Priority                       → Critical first
GET /api/tasks?dueFrom=2026-12-01T00:00:00Z&dueTo=2026-12-08T00:00:00Z&sortBy=DueDate
GET /api/projects?ownedByMe=true&sortBy=Name&sortDirection=Desc
GET /api/users?role=Manager&sortBy=LastLogin
```

## 2. Concepts

### Whitelisted sorting (no dynamic SQL)
`sortBy` is an **enum**, not a column name string. The client can only pick listed values (anything else → 400 from model binding), and each value maps to a compile-time-checked LINQ expression in a `switch`:
```csharp
TaskSortField.Title    => tasks.OrderByDirection(t => t.Title, direction),
TaskSortField.Priority => tasks.OrderByDirection(PriorityRank, direction),
```
Compare with "dynamic LINQ" (`OrderBy(sortByString)`): risk of exposing/ordering by any column, runtime errors, and injection-like abuse.

### Business order vs. alphabetical order
Enums are stored as **strings** (Phase 3 decision). `ORDER BY Priority` would therefore sort **alphabetically**: Critical, High, **Low, Medium** — wrong. A rank expression fixes it:
```csharp
private static readonly Expression<Func<TaskItem, int>> PriorityRank = t =>
    t.Priority == TaskPriority.Critical ? 4 : t.Priority == TaskPriority.High ? 3 :
    t.Priority == TaskPriority.Medium ? 2 : 1;
```
SQL Server receives (captured from the log):
```sql
ORDER BY CASE
    WHEN [t].[Priority] = N'Critical' THEN 4
    WHEN [t].[Priority] = N'High' THEN 3
    WHEN [t].[Priority] = N'Medium' THEN 2
    ELSE 1
END DESC, [t].[Id]
OFFSET @p ROWS FETCH NEXT @p4 ROWS ONLY
```
Status uses the workflow order (Todo → InProgress → InReview → Done → Cancelled). Trade-off of string enums: readable data, but sort-by-rank needs a `CASE` (not index-friendly; fine at this scale — an `int` rank column would be the next step).

### Stable paging needs a unique tiebreaker
Two tasks with the same priority have no defined order — SQL Server may return them differently on each query, so a row can appear on page 1 **and** page 2 (or on neither). Every sort ends with `.ThenBy(x => x.Id)`. Verified: pages 1–3 of size 2 concatenated = the full sorted list, no duplicates.

### Nulls last
Tasks without a due date go **last in both directions**: `OrderBy(t => t.DueDateUtc == null).ThenByDirection(t => t.DueDateUtc, direction)`. Same for project start/end dates, task `UpdatedAt`, user `LastLogin`.

### Offset paging and its limits
`OFFSET (page-1)*size ROWS FETCH NEXT size ROWS` + a separate `COUNT(*)` (same filters) for `totalCount`. Simple and supports "jump to page N"; cost grows with deep pages. For infinite scrolling over huge tables, **keyset (cursor) paging** (`WHERE (DueDate, Id) > (@lastDue, @lastId)`) is the alternative — see exercises.

### DRY paging
`PagedQueryParameters` (base class: `Page`, `PageSize`, `MaxPageSize = 100`) + `PagedQueryValidator<T>` (base validator) + `ApplyPaging()` (extension). Every query class/validator inherits them — the paging rules exist in exactly one place.

### Row-level security in one place
`Repositories/QueryFilters.cs`:
```csharp
public static IQueryable<TaskItem> VisibleTo(this IQueryable<TaskItem> tasks, Guid userId, bool isAdmin) =>
    isAdmin ? tasks : tasks.Where(t => t.Project.OwnerId == userId || t.Project.Members.Any(m => m.UserId == userId));
```
Task list, project list and **every dashboard query** start from these filters, so the dashboard can never count something the list wouldn't show. Verified: an outsider's search returns nothing and their dashboard is all zeros.

### Dashboard: aggregates in SQL
| Query | Technique |
|---|---|
| 8 counters (total, open, overdue, due ≤ 7 days, completed last 7 days, my open/overdue/due) | `GroupBy(_ => 1).Select(g => new(g.Count(), g.Count(cond), …))` → **one** `SELECT TOP(1) COUNT(*), COUNT(CASE WHEN … END), …` |
| By status / by priority | `GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() })` |
| Project progress (top 10 by open tasks) | correlated `COUNT` sub-queries per project |
| Project count | `COUNT(*)` on visible projects |

No task rows are loaded into memory — cost stays roughly constant whether there are 10 or 1,000,000 tasks.
- **Zero-filling:** `GROUP BY` returns only existing groups; `WithAllKeys` adds missing statuses/priorities with 0 (in enum order) so charts always have every bar.
- **One `now`** for all queries → consistent numbers within a response.
- **Sequential queries:** a `DbContext` isn't thread-safe; `Task.WhenAll` on one context would throw.
- **Completion %** = Done ÷ (Total − Cancelled) × 100 — cancelled work isn't "remaining work". Presentation math happens in C# on ≤ 10 rows.

---

## 3. Files created / changed

```
src/TaskManagementSystem/
├── Controllers/DashboardController.cs              NEW  GET /api/dashboard
├── Enums/SortOptions.cs                            NEW  SortDirection, TaskSortField, ProjectSortField, UserSortField
├── Helpers/QueryableExtensions.cs                  NEW  OrderByDirection, ThenByDirection, ApplyPaging
├── Interfaces/Repositories/IDashboardRepository.cs NEW  (+ TaskCounters record)
├── Interfaces/Services/IDashboardService.cs        NEW
├── Models/DTOs/
│   ├── Common/PagedQueryParameters.cs              NEW  base class for all list queries
│   ├── Dashboard/DashboardDtos.cs                  NEW  DashboardResponse, TaskTotals, MyWorkSummary, ProjectProgressResponse
│   ├── Tasks/TaskDtos.cs                           CHG  + categoryId, dueFrom, dueTo, overdue, sortBy, sortDirection
│   ├── Projects/ProjectDtos.cs                     CHG  + ownedByMe, sortBy, sortDirection
│   ├── Users/UserRequests.cs                       CHG  + sortBy, sortDirection
│   └── Comments/CommentDtos.cs                     CHG  inherits paging (default size 50)
├── Repositories/
│   ├── QueryFilters.cs                             NEW  VisibleTo() for projects and tasks
│   ├── DashboardRepository.cs                      NEW
│   ├── TaskRepository.cs                           CHG  filters, rank-based sorting, shared visibility
│   ├── ProjectRepository.cs                        CHG  ownedByMe, sorting, shared visibility
│   ├── UserRepository.cs                           CHG  sorting
│   └── CommentRepository.cs                        CHG  comment on tiebreaker
├── Services/DashboardService.cs                    NEW
├── Validators/
│   ├── Common/PagedQueryValidator.cs               NEW  shared paging rules
│   └── Users, Projects, Tasks, Comments            CHG  inherit PagedQueryValidator; sort enums; dueFrom ≤ dueTo
└── Extensions/ServiceCollectionExtensions.cs       CHG  dashboard registrations
```

---

## 4. Verification results (2026-11-24) — 32/32 passed

Dataset (project A: owner M, member U; B: owner M; C: owner Admin):

| Task | Project | Title | Priority | Status | Due | Assignee |
|---|---|---|---|---|---|---|
| T1 | A | Delta | Critical | Todo | +3 days | U |
| T2 | A | Alpha | High | InProgress | −2 days (overdue) | U |
| T3 | A | Echo | Medium | Done (now) | −1 day | — |
| T4 | A | Bravo | Low | Cancelled | — | — |
| T5 | A | Charlie | Medium | Todo | — (category Bug) | — |
| T6 | B | … | High | Todo | +20 days | — |
| T7 | C | … | Low | Todo | −5 days (overdue) | — |

| Area | Results |
|---|---|
| Sorting | Priority Desc **T1,T2,T3,T5,T4** · Priority Asc T4,T3,T5,T2,T1 · Status T1,T5,T2,T3,T4 · Title Alpha…Echo · DueDate Asc T2,T3,T1 then no-due T4,T5 · DueDate Desc T1,T3,T2 then T4,T5 · 3 pages × 2 = full list, no duplicates · page 4 empty with correct metadata · invalid sortBy / sortDirection → 400 |
| Filters | overdue=true → T2 only (T3 is past due but Done) · overdue=false → 4 · categoryId=1 → T5 · next 7 days → T1 · overdue+assignedToMe → T2 · dueFrom > dueTo → 400 · outsider search → nothing |
| Projects/Users | ownedByMe: Manager 2, member 0 · Name Asc/Desc · users CreatedAt default Desc → newest first |
| Dashboard (member U) | 1 project · 5 tasks · open 3 · overdue 1 · due ≤ 7d 1 · completed 1 · my work 2/1/1 · byStatus `Todo=2, InProgress=1, InReview=0, Done=1, Cancelled=1` · byPriority `Low=1, Medium=2, High=1, Critical=1` · project A 5/3/1/1, **25 %** |
| Dashboard (others) | Manager: 2 projects, 6 tasks, 4 open, A before B (more open tasks) · Outsider: all zeros, all 5 status keys · Admin: scope AllProjects, +3 projects / +7 tasks / +5 open / +2 overdue vs. baseline · anonymous → 401 |
| SQL | counters = one `SELECT TOP(1) COUNT(*), COUNT(CASE …)` statement · priority sort = `ORDER BY CASE … END DESC, [Id]` + `OFFSET/FETCH` |

Cleanup: test users, projects and tasks removed (0 projects, 0 tasks, your 2 accounts).

---

## 5. Common mistakes
- Sorting by a client-supplied column name string (dynamic SQL / dynamic LINQ).
- Sorting string-stored enums alphabetically.
- Paging without a unique tiebreaker → duplicates/missing rows across pages.
- Counting **after** paging (totalCount = page size) or with different filters than the page.
- Unbounded `pageSize` (`?pageSize=1000000` = denial of service).
- Computing dashboards by loading all tasks and counting in C#.
- Dashboard and list using different visibility rules (data leak or "numbers don't match").
- Missing zero buckets in GROUP BY results → broken charts.
- Several `DateTime.UtcNow` calls in one report → slightly inconsistent numbers.
- `Task.WhenAll` over queries on the same `DbContext`.

## 6. Interview questions
1. How do you implement sorting safely? Why not accept a column name?
2. Why can sorting by an enum column give the wrong order? How do you fix it?
3. Why do you need a unique tiebreaker for paging?
4. Offset vs. keyset (cursor) paging — trade-offs?
5. How do you return `totalCount` efficiently and correctly?
6. How would you compute dashboard counters without loading rows? What SQL does `g.Count(condition)` produce?
7. How do you make sure a dashboard respects authorization?
8. Why can't you run several EF Core queries in parallel on one `DbContext`?
9. How would you cache the dashboard, and what would invalidate the cache?
10. Which indexes would you add for `?assignedToMe=true&overdue=true&sortBy=DueDate`?

## 7. Exercises
1. Add `GET /api/tasks/cursor?after=…&size=…` using **keyset paging** on `(DueDateUtc, Id)` and compare the SQL with offset paging.
2. Cache the dashboard per user for 60 seconds with `HybridCache` / `IMemoryCache`; discuss staleness.
3. Add a persisted `PriorityRank` int column (computed or set on save) with an index, and sort by it instead of `CASE`.
4. Add `GET /api/categories?search=` and `sortBy=Name|CreatedAt` using the shared paging/sorting helpers.
5. Add `?projectId=` to the dashboard to scope the statistics to one project (respecting 404 for non-members).
