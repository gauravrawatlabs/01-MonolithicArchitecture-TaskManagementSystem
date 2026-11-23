# Phase 3 — Entities, DbContext, Relationships, Migrations & Seed Data

> **Phase status:** ✅ Implemented & verified (2026-11-23) — awaiting user review
> **Goal:** Turn the Phase 1 ER design into C# entities + EF Core configuration, generate the first migration, create all tables in SQL Server, and seed reference data (roles, categories).

---

## 1. Concepts

### Code First
We describe the database **in C#** (entity classes + configuration). EF Core compares that model with the last snapshot and generates a **migration** — C# code that creates/alters tables. The database is a *result* of the code, never edited by hand.

```
Entities + Configurations ──(dotnet ef migrations add)──► Migration (C#)
                                                            │
                                    (dotnet ef database update)
                                                            ▼
                                                  SQL Server tables
```

### Entity vs. table vs. DTO
| | Purpose | Lives in |
|---|---|---|
| **Entity** | Shape of a row in the DB, with navigation properties | `Models/Entities/` |
| **Table** | Physical storage | SQL Server |
| **DTO** | Shape of data crossing the API boundary (Phase 4+) | `Models/DTOs/` |

Entities never leave the API — they contain things like `PasswordHash` and navigation cycles.

### Navigation properties
- **Reference navigation** — `TaskItem.Project` (points to one).
- **Collection navigation** — `Project.Tasks` (points to many).
- The **foreign key property** (`TaskItem.ProjectId`) is the actual column. Having both FK + navigation lets us set `ProjectId = 5` without loading the project.

### Fluent API vs. Data Annotations
We use **Fluent API** (`IEntityTypeConfiguration<T>`), not attributes like `[MaxLength]`:
- Keeps entities clean (no persistence attributes).
- Can express everything (composite keys, delete behaviors, check constraints, filtered indexes) — annotations cannot.
- One configuration class per entity → easy to find.

### IdentityDbContext
`ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>` adds all Identity tables to **our** database. One database for the whole monolith.

`base.OnModelCreating(modelBuilder)` **must run first** — otherwise Identity tables have no keys and you get *"The entity type 'IdentityUserLogin<Guid>' requires a primary key"*.

### Delete behaviors
| EF Core `DeleteBehavior` | SQL generated | Meaning |
|---|---|---|
| `Cascade` | `ON DELETE CASCADE` | Delete children too |
| `SetNull` | `ON DELETE SET NULL` | Keep children, clear the FK |
| `Restrict` | `ON DELETE NO ACTION` | Block the delete if children exist |
| `NoAction` | (no clause = `NO ACTION`) | Same in SQL Server; differs only in EF's in-memory tracking behaviour |

> `Restrict` shows up as `NO ACTION` in SQL — that's normal. SQL Server has no separate `RESTRICT` keyword; `NO ACTION` already blocks the delete.

### Why "multiple cascade paths" didn't happen
SQL Server refuses a FK if a delete on one table could reach another table through **two cascade paths**. If `AspNetUsers → Tasks` were Cascade, deleting a user could reach `Comments` via `Users → Comments` **and** `Users → Tasks → Comments`. All user-owned FKs are `Restrict` (users are deactivated, never deleted), so every table has only one cascade path.

### Enums stored as strings
`ConfigureConventions` stores `TaskItemStatus`, `TaskPriority`, `ProjectMemberRole` as `nvarchar(20)` (`"InProgress"`, not `1`):
- Readable in SSMS and reports.
- Reordering enum members cannot silently change stored meaning.
- CHECK constraints (`CK_Tasks_Status`) are generated **from the enum names** (`CheckConstraintSql.EnumValues<T>()`), so code and DB stay in sync.

### Three ways to seed data
| Technique | Good for | Used here |
|---|---|---|
| `HasData()` in configuration → inserted by the migration | Static reference data with fixed keys | ✅ Roles, default categories |
| `UseSeeding` / `UseAsyncSeeding` (EF 9+) | Data that needs logic at startup | — |
| Startup seeder using `UserManager` | Users (password must be hashed by Identity) | Phase 5 (admin user) |

**HasData values must be constants** — `Guid.NewGuid()` or `DateTime.UtcNow` would change the model on every build and EF would think a new migration is needed. That's why `Data/Seed/SeedData.cs` holds fixed GUIDs, concurrency stamps and a fixed timestamp.

---

## 2. Files created / changed

```
src/TaskManagementSystem/
├── Constants/Roles.cs                         ← "Admin", "Manager", "User" constants
├── Enums/
│   ├── TaskItemStatus.cs                      ← Todo, InProgress, InReview, Done, Cancelled
│   ├── TaskPriority.cs                        ← Low, Medium, High, Critical
│   └── ProjectMemberRole.cs                   ← Owner, Member
├── Models/Entities/
│   ├── ApplicationUser.cs                     ← : IdentityUser<Guid> + profile fields
│   ├── RefreshToken.cs                        ← hashed token, rotation chain
│   ├── Category.cs
│   ├── Project.cs
│   ├── ProjectMember.cs                       ← explicit many-to-many join entity
│   ├── TaskItem.cs                            ← table "Tasks"
│   └── Comment.cs
├── Data/
│   ├── ApplicationDbContext.cs                ← now IdentityDbContext + DbSets + enum conventions
│   ├── Configurations/
│   │   ├── CheckConstraintSql.cs              ← builds CHECK SQL from enum names
│   │   ├── ApplicationUserConfiguration.cs
│   │   ├── RoleConfiguration.cs               ← seeds 3 roles (HasData)
│   │   ├── RefreshTokenConfiguration.cs       ← 1:N to user + 1:1 self-reference
│   │   ├── CategoryConfiguration.cs           ← seeds 5 categories (HasData)
│   │   ├── ProjectConfiguration.cs
│   │   ├── ProjectMemberConfiguration.cs
│   │   ├── TaskItemConfiguration.cs
│   │   └── CommentConfiguration.cs
│   └── Seed/SeedData.cs                       ← fixed seed IDs / stamps / timestamp
├── Migrations/
│   ├── 20261123171020_InitialCreate.cs        ← Up() creates everything, Down() drops it
│   ├── 20261123171020_InitialCreate.Designer.cs ← model snapshot at this migration (metadata)
│   └── ApplicationDbContextModelSnapshot.cs   ← CURRENT model; diffed for the next migration
└── TaskManagementSystem.csproj                ← + Microsoft.AspNetCore.Identity.EntityFrameworkCore 10.0.15
```

Every file contains XML/inline comments explaining **why** each rule exists — read the code alongside this doc.

### Key class explanations

| Class | What to notice |
|---|---|
| `ApplicationUser` | Inherits Email, PasswordHash, SecurityStamp, lockout fields from `IdentityUser<Guid>`. `IsActive = true` set in C# (not a DB default) to avoid EF's bool-default sentinel warning. Six navigation collections — one per relationship. |
| `RefreshToken` | `IsRevoked` / `IsActiveAt(now)` are computed — read-only members are **not** mapped to columns. `IsActiveAt` takes the time as a parameter so tests can control "now". |
| `ProjectMember` | Composite key `(ProjectId, UserId)`; explicit because it carries `MemberRole` and `JoinedAtUtc`. |
| `TaskItem` | Two FKs to the same table (`CreatedById`, `AssignedToId`) → EF can't guess pairing, so both are configured explicitly in `TaskItemConfiguration`. |
| `ApplicationDbContext` | `DbSet<T> X => Set<T>()` — expression-bodied DbSets avoid nullable warnings and are the recommended modern style. |
| `RoleConfiguration` | Seeds `NormalizedName` ("ADMIN") because Identity looks roles up by the normalized value. |

---

## 3. Relationships implemented

| Type | Relationship | Delete behavior |
|---|---|---|
| **1 : 1** | `RefreshToken.ReplacedByTokenId → RefreshToken` (self) | NoAction (SQL Server forbids cascade on self-reference). EF created a **unique filtered index** `WHERE ReplacedByTokenId IS NOT NULL` to enforce "one". |
| 1 : N | User → RefreshTokens | Cascade |
| 1 : N | User (owner) → Projects | Restrict |
| 1 : N | Project → Tasks | Cascade |
| 1 : N | Task → Comments | Cascade |
| 1 : N | Category → Tasks (optional) | SetNull |
| 1 : N | User → Tasks (CreatedBy) | Restrict |
| 1 : N | User → Tasks (AssignedTo, optional) | Restrict |
| 1 : N | User → Comments | Restrict |
| **M : N** | Users ↔ Projects via `ProjectMembers` | Project side Cascade, User side Restrict |
| **M : N** | Users ↔ Roles via `AspNetUserRoles` (Identity) | Cascade (Identity default) |

## 4. Indexes & constraints created (from the generated SQL)

```
UNIQUE  IX_Categories_Name                   Categories(Name)
UNIQUE  IX_Projects_OwnerId_Name             Projects(OwnerId, Name)
UNIQUE  IX_RefreshTokens_TokenHash           RefreshTokens(TokenHash)
UNIQUE  IX_RefreshTokens_ReplacedByTokenId   RefreshTokens(ReplacedByTokenId) WHERE NOT NULL
        IX_RefreshTokens_UserId / _ExpiresAtUtc
        IX_Tasks_ProjectId_Status            Tasks(ProjectId, Status)
        IX_Tasks_AssignedToId_Status         Tasks(AssignedToId, Status)
        IX_Tasks_DueDateUtc / _CreatedById / _CategoryId
        IX_Comments_TaskId_CreatedAtUtc      Comments(TaskId, CreatedAtUtc)
        IX_Comments_AuthorId
        IX_ProjectMembers_UserId
        IX_AspNetUsers_IsActive
CHECK   CK_Tasks_Status          [Status]   IN (N'Todo', N'InProgress', N'InReview', N'Done', N'Cancelled')
CHECK   CK_Tasks_Priority        [Priority] IN (N'Low', N'Medium', N'High', N'Critical')
CHECK   CK_ProjectMembers_MemberRole  [MemberRole] IN (N'Owner', N'Member')
CHECK   CK_Projects_EndDate_OnOrAfter_StartDate
```

**FK index rule:** EF Core automatically indexes every FK column — *unless* an existing index already starts with that column. `IX_Tasks_ProjectId_Status` starts with `ProjectId`, so no separate `IX_Tasks_ProjectId` was created.

---

## 5. Migration commands

```powershell
# Create a migration from model changes
dotnet ef migrations add InitialCreate --project src/TaskManagementSystem --output-dir Migrations

# Preview the SQL (idempotent = safe to run on any DB state) — great for code review / production
dotnet ef migrations script --project src/TaskManagementSystem --idempotent -o migration.sql

# Apply to the database
dotnet ef database update --project src/TaskManagementSystem

# Check that code and migrations are in sync (useful in CI)
dotnet ef migrations has-pending-model-changes --project src/TaskManagementSystem

# Undo the LAST migration (only if NOT yet applied / pushed)
dotnet ef migrations remove --project src/TaskManagementSystem

# Roll the database back to a specific migration (0 = empty)
dotnet ef database update 0 --project src/TaskManagementSystem
```

---

## 6. Verification results (2026-11-23)

| # | Check | Result |
|---|---|---|
| 1 | `dotnet build` | ✅ 0 warnings, 0 errors |
| 2 | `dotnet ef migrations add InitialCreate` | ✅ 3 files generated |
| 3 | SQL review (FKs, checks, indexes) | ✅ Matches Phase 1 design exactly |
| 4 | `dotnet ef database update` | ✅ 14 tables created |
| 5 | Seed data | ✅ Roles: Admin, Manager, User · Categories: Bug, Feature, Improvement, Research, Documentation |
| 6 | `has-pending-model-changes` | ✅ "No changes have been made to the model since the last migration." |
| 7 | `GET /health` | ✅ 200 Healthy |

### Database rules test (inside a transaction, rolled back afterwards)

| # | Attempt | Expected | Result |
|---|---|---|---|
| 1 | Insert task with `Status = 'Started'` | CHECK violation | ✅ blocked by `CK_Tasks_Status` |
| 2 | Project with EndDate < StartDate | CHECK violation | ✅ blocked |
| 3 | Duplicate category "Bug" | UNIQUE violation | ✅ blocked by `IX_Categories_Name` |
| 4 | Delete a user who owns data | FK violation (Restrict) | ✅ blocked by `FK_Tasks_AspNetUsers_CreatedById` |
| 5 | Delete category used by a task | Task kept, `CategoryId = NULL` | ✅ SET NULL |
| 6 | Delete project | Tasks, comments, members deleted | ✅ 0 / 0 / 0 left (cascade) |

**Gotcha found while testing:** running the test with plain `sqlcmd` failed with *"INSERT failed because the following SET options have incorrect settings: 'QUOTED_IDENTIFIER'"*. `sqlcmd` turns `QUOTED_IDENTIFIER` **off** by default, and inserts into tables with **filtered indexes** (Identity's `UserNameIndex`, our `IX_RefreshTokens_ReplacedByTokenId`) require it **on**. Fix: `sqlcmd -I`. EF Core and SSMS turn it on automatically, so the application is not affected.

---

## 7. What's NOT in this phase (by design)
- **Admin user seeding** → Phase 5 (needs `UserManager` to hash the password; the password comes from User Secrets).
- **Timestamps (`CreatedAtUtc`, `UpdatedAtUtc`)** are plain columns; services set them using `TimeProvider` (Phase 4).

## 8. Common mistakes
- Naming the entity `Task` → endless `System.Threading.Tasks.Task` ambiguity errors.
- Forgetting `base.OnModelCreating()` in an `IdentityDbContext`.
- Cascade everywhere → "multiple cascade paths" error or accidental mass deletes.
- `HasData` with `Guid.NewGuid()` / `DateTime.Now` → a new migration every time.
- Editing a migration that has already been applied/pushed instead of adding a new one.
- Deleting `ApplicationDbContextModelSnapshot.cs` — EF then can't compute the next diff.
- Exposing entities directly from controllers (cycles like `Project.Tasks[0].Project...` break JSON serialization).

## 9. Interview questions
1. What is the difference between Code First and Database First?
2. Why use Fluent API instead of Data Annotations?
3. What are the three files a migration produces, and what is the model snapshot for?
4. Explain `Cascade`, `SetNull`, `Restrict` and `NoAction`. Why is `Restrict` shown as `NO ACTION` in SQL Server?
5. What causes the "multiple cascade paths" error and how do you fix it?
6. When do you model many-to-many with an explicit join entity instead of a skip navigation?
7. How is a one-to-one relationship enforced at the database level?
8. Why must `HasData` use constant values?
9. Why store enums as strings? What's the trade-off?
10. Why does EF Core not create an index on `Tasks.ProjectId` here?

## 10. Exercises
1. Add a `Color` (`nvarchar(7)`, optional, e.g. `#FF0000`) property to `Category`. Run `dotnet ef migrations add AddCategoryColor`, read the generated `Up()`/`Down()`, apply it, then roll back with `dotnet ef database update InitialCreate` and `dotnet ef migrations remove`.
2. In SSMS, open *Database Diagrams* for `TaskManagementDb` and compare it to the Phase 1 ER diagram.
3. Change `TaskItem → AssignedTo` to `SetNull` in a throwaway branch and run `migrations add`. Does SQL Server accept it when you `database update`? Explain why (or why not), then revert.
4. Add a new enum member `Blocked` to `TaskItemStatus` and run `has-pending-model-changes`. What changed, and why? (Revert afterwards.)
