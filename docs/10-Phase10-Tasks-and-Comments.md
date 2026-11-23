# Phase 10 — Tasks & Comments

> **Phase status:** ✅ Implemented & verified (2026-11-24) — awaiting user review
> **Goal:** The core of the system: tasks with priorities, due dates, categories, assignment and a status workflow; comments on tasks — all with fine-grained, resource-based authorization.

---

## 1. Rules implemented

### Task permissions (only project members and Admins; outsiders → 404; writes need a verified email)
| Operation | Admin | Project owner | Task creator | Assignee | Other member |
|---|---|---|---|---|---|
| Read / list / comment | ✅ | ✅ | ✅ | ✅ | ✅ |
| Create in project | ✅ | ✅ | — | — | ✅ |
| Update details / delete | ✅ | ✅ | ✅ | ❌ | ❌ |
| Assign / unassign | ✅ | ✅ | ❌ | ❌ | ❌ |
| Change status | ✅ | ✅ | ✅ | ✅ | ❌ |
| Reopen Done / restore Cancelled | ✅ | ✅ | ❌ | ❌ | ❌ |

A creator or assignee who is **removed from the project loses all rights** — membership is checked first.

### Comment permissions
| Operation | Admin | Project owner | Author | Other member |
|---|---|---|---|---|
| Edit | ❌ | ❌ | ✅ | ❌ |
| Delete | ✅ | ✅ (moderation) | ✅ | ❌ |

Nobody — not even an Admin — may edit someone else's words; they may only remove them.

### Status workflow (state machine)
```
            ┌──────────────┐        ┌──────────────┐
   Todo ───►│  InProgress  │───────►│   InReview   │───► Done
    ▲  ◄────│              │◄───────│              │       │
    │       └──────┬───────┘        └──────┬───────┘       │ reopen (owner/Admin)
    │              ▼                       ▼               ▼
    │          Cancelled ◄───────────── (Todo too)     InProgress
    └── restore (owner/Admin) ──┘
```
| From → To | Allowed? |
|---|---|
| Todo → InProgress, Cancelled | ✅ |
| InProgress → Todo, InReview, Cancelled | ✅ |
| InReview → InProgress, Done, Cancelled | ✅ |
| Done → InProgress | owner/Admin only (403 otherwise) |
| Cancelled → Todo | owner/Admin only (403 otherwise) |
| Same status | no-op, 200 (idempotent) |
| anything else (e.g. Todo → InReview, Todo → Done) | **409** with the allowed next statuses |

`CompletedAtUtc` is set when moving to Done and cleared when leaving it.

### Business rules
| Rule | Where | Response |
|---|---|---|
| Title required ≤ 200, description ≤ 4000, valid enums | Validator | 400 |
| New due date must be in the future | Validator (create) / service (update, only if **changed**) | 400 |
| Due date not after the project's end date | Service (needs the project) | 422 |
| Category must exist | Service (needs the DB) | 422 |
| Assignee must be an **active member** of the task's project | Service | 422 |
| Removing a project member unassigns their tasks | ProjectService (Phase 9) | — |
| Deleting a task deletes its comments | DB cascade (Phase 3) | — |

**Validator vs. service:** rules that only need the request (lengths, enum values, "in the future") live in validators. Rules that need **data** (project end date, membership, category existence) live in the service.

## 2. Concepts

### Modules cooperating inside a monolith
`TaskService` uses `IProjectRepository` (membership/end date), `ICategoryRepository` (category exists) and `UserManager` (assignee active) — **plain in-process calls**, one database, no network. In a microservice architecture each of these would be an HTTP/gRPC call with its own failure modes; here it's a method call and a SQL join.

### Status workflow as a pure state machine
`Services/TaskStatusWorkflow.cs` is a static dictionary of `(from, to) → requiresOwner`. No dependencies → trivially unit-testable (Phase 13), and the API, error messages and potential UI hints all read the same table.

### One projection for lists and details
`TaskRepository.ProjectToResponse(IQueryable<TaskItem>, now)` is the **only** code that shapes a `TaskResponse` — used by the list and by single-item reads after authorization. Names (project, category, creator, assignee) come from SQL joins, and **`IsOverdue` is computed by SQL Server** (`DueDateUtc < @now AND Status NOT IN ('Done','Cancelled')`). Comments use the same pattern.

### Write path vs. read path
```
WRITE: load ENTITY (+ Project + Members, tracked) → authorize (handler) → mutate → SaveChanges
READ : project straight to DTO (AsNoTracking, joins) — after the write, the response is re-read the same way
```
The entity graph needed for authorization isn't the same shape as the response — mixing them leads to over-fetching or missing data.

### Authorization handlers that fail closed
`TaskAuthorizationHandler` reads `task.Project.Members`; `CommentAuthorizationHandler` reads `comment.Task.Project.Members`. If a navigation isn't loaded the handler **denies** (null-check), never throws or allows.

### Dates: `AsUtc()`
JSON `"2027-01-15T10:00:00"` (no offset) arrives as `DateTimeKind.Unspecified`. Converting it with `ToUniversalTime()` would treat it as **server-local time** and shift it. Our fields are named `...Utc`, so `AsUtc()` marks Unspecified values as UTC — verified: returned as `"2027-01-15T10:00:00Z"`.

### Enums as strings (global change)
`JsonStringEnumConverter` in MVC JSON options (requests/responses) **and** in HTTP JSON options (what the OpenAPI generator reads), so clients send/receive `"InProgress"` and the schema lists `Todo, InProgress, InReview, Done, Cancelled`.

### Validators with dependencies
`CreateTaskRequestValidator(TimeProvider timeProvider)` — validators are resolved from DI, so they can take services. With a fake `TimeProvider` the "in the future" rule becomes deterministic in tests.

### REST shape
- Sub-resources for actions with their own rules: `PATCH /api/tasks/{id}/assignee`, `PATCH /api/tasks/{id}/status` (different permissions than `PUT /api/tasks/{id}`).
- Comments: collection nested under the task (`/api/tasks/{taskId}/comments`), individual comments addressed directly (`/api/comments/{id}`).

---

## 3. Files created / changed

```
src/TaskManagementSystem/
├── Authorization/
│   ├── Operations/TaskOperations.cs              NEW  Task: Read, Update, Delete, Assign, ChangeStatus, Reopen, AddComment; Comment: Update, Delete
│   └── Handlers/
│       ├── TaskAuthorizationHandler.cs           NEW
│       └── CommentAuthorizationHandler.cs        NEW
├── Controllers/
│   ├── TasksController.cs                        NEW  7 endpoints
│   └── CommentsController.cs                     NEW  4 endpoints
├── Helpers/DateTimeExtensions.cs                 NEW  AsUtc()
├── Interfaces/Repositories/ITaskRepository.cs, ICommentRepository.cs   NEW
├── Interfaces/Services/ITaskService.cs, ICommentService.cs             NEW
├── Models/DTOs/Tasks/TaskDtos.cs                 NEW  TaskResponse, create/update/assign/status requests, query params
├── Models/DTOs/Comments/CommentDtos.cs           NEW
├── Repositories/TaskRepository.cs, CommentRepository.cs                NEW
├── Services/
│   ├── TaskService.cs                            NEW
│   ├── CommentService.cs                         NEW
│   └── TaskStatusWorkflow.cs                     NEW  state machine
├── Validators/Tasks/TaskValidators.cs            NEW
├── Validators/Comments/CommentValidators.cs      NEW
├── Extensions/ServiceCollectionExtensions.cs     CHG  repositories, services, 2 handlers
└── Program.cs                                    CHG  JsonStringEnumConverter (MVC + OpenAPI)
```
No migration — `Tasks` and `Comments` exist since Phase 3.

### Endpoints
| Method | Endpoint | Success |
|---|---|---|
| GET | `/api/tasks?projectId=&status=&priority=&assignedToId=&assignedToMe=&search=&page=&pageSize=` | 200 paged |
| GET | `/api/tasks/{id}` | 200 |
| POST | `/api/tasks` | 201 + Location |
| PUT | `/api/tasks/{id}` | 200 |
| PATCH | `/api/tasks/{id}/assignee` | 200 |
| PATCH | `/api/tasks/{id}/status` | 200 |
| DELETE | `/api/tasks/{id}` | 204 |
| GET | `/api/tasks/{taskId}/comments?page=&pageSize=` | 200 paged (oldest first) |
| POST | `/api/tasks/{taskId}/comments` | 201 |
| PUT | `/api/comments/{id}` | 200 |
| DELETE | `/api/comments/{id}` | 204 |

---

## 4. Verification results (2026-11-24) — 51/51 passed

Users: Admin · **owner** (Manager) · **creator** · **assignee** · **member** · **outsider** (not in project) · **unverified** (member, email not confirmed). Project ends 2027-06-30.

| Area | Checks |
|---|---|
| Create | outsider 404 · unverified 403 · creator 201 (Todo, "High", category name, creator name, not overdue, unassigned) · no-offset due date stored as UTC · default priority Medium · past due 400 · after project end 422 · unknown category 422 · empty title + projectId 0 → 400 (both errors) · priority "Urgent" 400 · unknown project 404 |
| Read/list | member 200 · outsider 404 · member/admin list 2 · outsider list 0 |
| Assign | creator 403 · owner 200 · outsider as assignee 422 · unknown user 422 · Guid.Empty 400 |
| Workflow | member 403 · Todo→InReview **409** (lists allowed) · Todo→InProgress→(no-op)→InReview→Done 200 with CompletedAtUtc · assignee reopen **403** · owner reopen 200 (CompletedAtUtc cleared) · creator cancel 200 · creator restore 403 · admin restore 200 · "Started" 400 |
| Update | member 403 · assignee 403 · creator 200 · changed due date in past 400 · **overdue task (isOverdue=true) still editable** when due date unchanged 200 |
| Filters | assignedToMe 1 · status=Todo 2 · priority=High 1 · search 1 · invalid status 400 |
| Comments | outsider 404 · member add 201 (trimmed) · unverified 403 · empty 400 · list oldest first · author edit 200 (UpdatedAtUtc set) · other member edit 403 · **Admin edit 403** · creator delete other's 403 · project owner delete 204 · unknown 404 · **removed member**: list 404, edit own old comment 404 · Admin deletes it 204 |
| Delete | assignee 403 · creator 204 (comments 1 → 0, cascade) · GET afterwards 404 · admin delete 204 |
| OpenAPI | `TaskItemStatus` enum documented as strings |

Cleanup: only `phase10-*` users and their projects removed → 0 projects, 0 tasks, 0 comments, your 2 accounts.

### Open items for Phase 11 (found during testing)
1. JSON conversion errors (e.g. `"priority": "Urgent"`) include the internal .NET type name in the message — minor information disclosure. Fix: `JsonOptions.AllowInputFormatterExceptionMessages = false`.
2. Error keys from **query-string** binding use PascalCase (`"Status"`) while body errors use camelCase (`"dueDateUtc"`).
3. (From Phase 8) EF Core logs handled unique-constraint violations at Error level.

---

## 5. Common mistakes
- Letting any member edit/delete any task, or letting the assignee re-assign the task.
- Allowing assignment to users outside the project.
- Free-form status updates (any → any) instead of a state machine.
- "Due date must be in the future" applied on every update → overdue tasks become uneditable.
- Treating offset-less timestamps as server-local time.
- Computing `IsOverdue` in C# after loading all rows (instead of in SQL for lists) — or storing it (it goes stale).
- Two different mapping code paths for list vs. details that drift apart.
- Admins editing other users' comments.
- Forgetting that removed members keep their old comments/created tasks — decide what rights remain (here: none until re-added).

## 6. Interview questions
1. How would you model and enforce a status workflow? Where does the logic live?
2. Which validations belong in a validator and which in the service? Why?
3. Why is "due date in the future" checked only when the due date changes on update?
4. 409 vs 422 vs 403 for: invalid transition, assignee not a member, assignee reopening a task?
5. How do modules in a monolith collaborate? What would change in microservices?
6. Why compute `IsOverdue` in the database query rather than storing it?
7. How do you handle date/time values without an offset in an API?
8. Nested vs. flat routes for sub-resources — when do you use each?
9. Why can a project owner delete but not edit a member's comment?

## 7. Exercises
1. Add `GET /api/tasks/{id}/allowed-statuses` returning `TaskStatusWorkflow.GetNextStatuses` filtered by the caller's rights (UI can show only valid buttons).
2. Let members **self-assign** unassigned tasks (`PATCH /api/tasks/{id}/assignee` with their own id) — extend `TaskAuthorizationHandler` with a new operation.
3. Add an `ActivityLog` table and record every status change (who, from, to, when) in the same transaction.
4. Add optimistic concurrency (`rowversion`) to `TaskItem` so two people changing the status at the same time get a 409 instead of last-write-wins.
5. Write unit tests for `TaskStatusWorkflow` covering every (from, to) pair (Phase 13 preview).
