# Phase 7 — Authorization: Roles, Claims, Policies & Resource-Based

> **Phase status:** ✅ Implemented & verified (2026-11-23) — awaiting user review
> **Goal:** Decide *what* an authenticated user may do — enforced on the server, secure by default — and add the User module (own profile + admin user management) to exercise every authorization style.

---

## 1. Scope decision
Authorization needs real endpoints to be proven. The **User module** (profile update, admin user management) had no phase assigned, and it is all about roles — so it lives here. Project / Task / Comment ownership handlers follow the same pattern in Phases 9–10, together with their endpoints.

## 2. Concepts

### The four styles

| Style | Question it answers | Example here | Where it's checked |
|---|---|---|---|
| **Role-based** | Is the user in role X? | `Policies.AdminOnly` → `RequireRole("Admin")` | Before the action (attribute) |
| **Claims-based** | Does the token carry claim X with value Y? | `Policies.EmailVerified` → `email_verified == true` | Before the action (attribute) |
| **Policy-based** | Named bundle of requirements | `[Authorize(Policy = Policies.AdminOnly)]` | Before the action |
| **Resource-based** | May THIS user do Y to THIS specific object? | `GET /api/users/{id}` → self or Admin | Inside the service, after loading the resource |

Roles and claims are *data in the token*; a **policy** is the *rule* that evaluates them. Controllers name the policy; `AddAuthorizationPolicies()` defines it in one place.

### How a policy is evaluated
```
Request ─► UseAuthentication: token → HttpContext.User (claims: sub, email, role, email_verified)
        ─► UseAuthorization: collect [Authorize] data on the endpoint
              ├─ no attributes ............ FallbackPolicy (RequireAuthenticatedUser)
              ├─ [AllowAnonymous] ......... skip
              └─ [Authorize(Policy=…)] x N  ALL policies must succeed (AND)
        ─► not authenticated → 401 (challenge)  │  authenticated but fails → 403 (forbid)
        ─► controller action
              └─ service loads resource ─► IAuthorizationService.AuthorizeAsync(user, resource, operation)
                                            └─ UserAuthorizationHandler → Succeed / (no succeed) → ForbiddenException → 403
```

### Secure by default: the fallback policy
```csharp
.SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
```
Every endpoint without `[Authorize]`/`[AllowAnonymous]` now requires login. A forgotten attribute **fails closed** (401) instead of leaking data. Public endpoints must opt out explicitly: register/login/refresh/logout and the account email flows (`[AllowAnonymous]`), `/health` and `/openapi/v1.json` (`.AllowAnonymous()`).

Side effect (verified): an **anonymous** request to an unknown URL gets **401**, not 404 — anonymous callers can't probe which routes exist.

### Combining policies (AND)
```csharp
[Authorize(Policy = Policies.AdminOnly)]
[Authorize(Policy = Policies.EmailVerified)]
```
Both must pass. For OR logic, write one policy with `RequireAssertion(ctx => a || b)` or a custom requirement handler.

### Why `RequireAssertion` for the email claim?
`RequireClaim("email_verified", "true")` does an **exact, case-sensitive** string comparison. The claim is emitted as a JSON boolean, and different serializers/handlers may surface it as `true` or `True`. `bool.TryParse` accepts both — robust instead of brittle.

### Resource-based authorization
A role can't express "users may read **their own** profile" — the answer depends on *which* profile, which is only known after loading it. Pattern:

```csharp
// Authorization/Operations/UserOperations.cs
public static readonly OperationAuthorizationRequirement Read = new() { Name = nameof(Read) };

// Authorization/Handlers/UserAuthorizationHandler.cs
class UserAuthorizationHandler : AuthorizationHandler<OperationAuthorizationRequirement, ApplicationUser>
    → Succeed if User.IsInRole("Admin") || User.sub == resource.Id

// Services/UserService.cs
var user = await FindUserAsync(id);                                        // 404 if missing
await authorizationService.AuthorizeOrThrowAsync(currentUser, user, UserOperations.Read);  // 403 if not allowed
```
- Handlers **don't call `Fail()`** — simply not succeeding denies, and leaves room for other handlers to grant.
- The handler is a **Singleton** (pure logic, no dependencies).
- `AuthorizeOrThrowAsync` (extension) turns a failed result into `ForbiddenException` → 403 ProblemDetails.
- **404 vs 403:** we return 404 for non-existent ids and 403 for existing-but-not-yours. That reveals existence; acceptable here because ids are unguessable GUIDs. For guessable ids you'd return 404 in both cases.

### Authorization is enforced on the server
Hiding a button in the UI is UX, not security. Every rule above is enforced by the API — verified by calling endpoints directly with tokens of different roles.

### When do role/status changes take effect?
| Change | Existing access token | Next refresh | Login |
|---|---|---|---|
| Role change | Still old roles (≤ 15 min) | ✅ new roles (read from DB) | ✅ |
| Deactivation | Valid ≤ 15 min, **but** endpoints that load the user (e.g. `/me`) reject it | ❌ refresh tokens revoked | ❌ blocked |

Deactivation also rotates the **security stamp**, so outstanding email-confirmation and password-reset links stop working.

---

## 3. Business rules (422 Unprocessable Entity)
| Rule | Why |
|---|---|
| An Admin cannot remove their own Admin role | Prevents locking yourself out; guarantees at least one Admin remains |
| An Admin cannot deactivate their own account | Same reason |

---

## 4. Files created / changed

```
src/TaskManagementSystem/
├── Authorization/
│   ├── Operations/UserOperations.cs              NEW  OperationAuthorizationRequirement "Read"
│   └── Handlers/UserAuthorizationHandler.cs      NEW  self-or-Admin rule
├── Constants/Policies.cs                         NEW  AdminOnly, EmailVerified
├── Controllers/
│   ├── UsersController.cs                        NEW  list, get by id, update me, roles, status
│   └── CategoriesController.cs                   CHG  [Authorize] (no longer public)
├── Extensions/
│   ├── AuthorizationServiceExtensions.cs         NEW  AuthorizeOrThrowAsync
│   ├── ServiceCollectionExtensions.cs            CHG  AddAuthorizationPolicies (fallback + policies + handler), user registrations
│   └── WebApplicationExtensions.cs               CHG  MapOpenApi().AllowAnonymous()
├── Interfaces/Repositories/IUserRepository.cs    NEW
├── Interfaces/Services/IUserService.cs           NEW
├── Mappings/UserMappings.cs                      CHG  + IsActive
├── Models/DTOs/
│   ├── Common/PagedResult.cs                     NEW  Items, Page, PageSize, TotalCount, TotalPages, HasNext/Previous
│   ├── Users/UserProfileResponse.cs              CHG  + IsActive
│   └── Users/UserRequests.cs                     NEW  UserQueryParameters, UpdateProfileRequest, UpdateUserRolesRequest, UpdateUserStatusRequest
├── Repositories/UserRepository.cs                NEW  filtered + paged query with roles in ONE SQL statement
├── Services/UserService.cs                       NEW
├── Validators/Users/UserValidators.cs            NEW  query params, profile, roles
└── Program.cs                                    CHG  AddAuthorizationPolicies(), /health AllowAnonymous
```

### Endpoints added
| Method | Endpoint | Authorization |
|---|---|---|
| GET | `/api/users?search=&role=&isActive=&page=&pageSize=` | AdminOnly **and** EmailVerified |
| GET | `/api/users/{id}` | Authenticated + resource-based (self or Admin) |
| PUT | `/api/users/me` | EmailVerified |
| PUT | `/api/users/{id}/roles` | AdminOnly **and** EmailVerified |
| PATCH | `/api/users/{id}/status` | AdminOnly **and** EmailVerified |

### Read-model query (UserRepository)
`UserManager` would need one query per user to get roles (N+1). `GetPagedAsync` instead filters, counts, pages and projects **directly into `UserProfileResponse`**, with roles as a correlated sub-query — one round-trip for the page plus one for the count. Writes still go through `UserManager` (normalization, security stamps, concurrency stamps).

---

## 5. Testing in Swagger
1. Log in as the admin (credentials in your User Secrets) → **Authorize**.
2. `GET /api/users` → all users. Try `?role=Admin`, `?search=...`, `?isActive=false`.
3. Register a second user in another browser tab / private window, then as admin `PUT /api/users/{id}/roles` with `{ "roles": ["Manager"] }`.
4. As that user: refresh → decode the new access token at jwt.io → `"role": "Manager"`.
5. As that user: `GET /api/users` → 403; `GET /api/users/{own id}` → 200; `GET /api/users/{admin id}` → 403.

---

## 6. Verification results (2026-11-23) — 34/34 passed, 0 failures

| Area | Checks |
|---|---|
| Secure by default | Anonymous `/api/categories` 401 · `/health` 200 · `/openapi/v1.json` 200 · `/swagger` 200 · unknown URL 401 · login still anonymous |
| Claims-based | Unverified user: read categories 200, `PUT /users/me` **403** · after confirm + refresh: 200 · invalid profile 400 |
| Role-based | User → `GET /api/users` 403 · Manager → change roles 403 |
| Resource-based | User → own profile 200 · admin's profile **403** · random id 404 · Admin → any profile 200 |
| Admin list | `search=phase7` → 1 result with roles + jobTitle · `role=admin` → 1 · `pageSize=500` 400 · `role=Boss` 400 · `isActive=false` → 1 |
| Roles | Promote to Manager 200 (case-insensitive "manager") · old token still `User`, **refreshed token `Manager`** · unknown / empty / duplicate roles 400 · admin removing own Admin role **422** |
| Status | Admin self-deactivation **422** · deactivate user 200 → refresh 401, login 401, `/me` with old token 401 · reactivate → login 200 |
| OpenAPI | 🔒 on `/api/users` and `/api/categories`, none on `/api/auth/login` |
| Hygiene | No tokens/passwords in logs; test user deleted |

---

## 7. Common mistakes
- Relying on the UI to hide admin features — the API must enforce everything.
- Forgetting `[Authorize]` on a new controller (prevented here by the fallback policy).
- Checking roles with string comparisons scattered across services instead of named policies.
- `[Authorize(Roles = "Admin,Manager")]` thinking it means AND — it means **OR**. Two separate attributes mean AND.
- Putting ownership checks in controllers before the resource is loaded (or skipping them: **IDOR** — Insecure Direct Object Reference, e.g. `GET /api/users/{someone-else}`).
- Calling `context.Fail()` in every handler — blocks other handlers that could grant access.
- Trusting role claims after a demotion without short token lifetimes / refresh re-reads.
- Letting users assign their own roles, or letting the last Admin demote themselves.
- N+1 queries when listing users with roles.

## 8. Interview questions
1. Difference between role-based, claims-based, policy-based and resource-based authorization?
2. What is a fallback policy and why "secure by default"?
3. 401 vs 403 — which does the framework return when, and why?
4. How do you require two policies at once? How do you express OR?
5. What is IDOR and how does resource-based authorization prevent it?
6. Why can't attributes do resource-based checks? Where should the check happen?
7. Why shouldn't authorization handlers call `Fail()` by default?
8. How quickly does a role change reach an already-issued JWT? How would you make it immediate?
9. When would you return 404 instead of 403 for a resource the user can't access?
10. How do you avoid N+1 queries when listing users with their roles?

## 9. Exercises
1. Add `Policies.ManagerOrAdmin` (`RequireRole(Roles.Admin, Roles.Manager)`) and protect a test endpoint with it. (It's needed in Phase 9 for creating projects.)
2. Extend `UserAuthorizationHandler` with an `Update` operation and implement `PUT /api/users/{id}` for Admins (self edits keep using `/me`).
3. Replace `RequireAssertion` in `EmailVerified` with a custom `EmailVerifiedRequirement` + `AuthorizationHandler<EmailVerifiedRequirement>`.
4. Make deactivation take effect immediately: in `JwtBearerEvents.OnTokenValidated`, load the user (cache it for 1 minute) and call `context.Fail()` if inactive. Measure the cost.
5. Add a rule: an Admin cannot remove the Admin role from the **last** active Admin (even someone else).
