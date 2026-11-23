# Phase 5 — Authentication with ASP.NET Core Identity & JWT

> **Phase status:** ✅ Implemented & verified (2026-11-23) — awaiting user review
> **Goal:** Users can register and log in; the API issues a signed JWT access token; protected endpoints validate it. Admin account seeded at startup. Swagger gets an **Authorize** button.

---

## 1. Concepts

### Authentication vs. Authorization
| | Question | Fails with | Phase |
|---|---|---|---|
| **Authentication** | *Who are you?* | **401 Unauthorized** | 5 (this phase) |
| **Authorization** | *What are you allowed to do?* | **403 Forbidden** | 7 |

### What ASP.NET Core Identity gives us
- User & role storage (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`) through our `ApplicationDbContext`.
- **Password hashing** — PBKDF2 with HMAC-SHA512, a random salt per user and 100,000 iterations (format "Identity v3"; hashes start with `AQAAAAIAAYag`). The plain password is never stored and cannot be recovered.
- Password policy, unique email, **lockout** after repeated failures, security stamps, and token providers for email confirmation / password reset (Phase 6).

`AddIdentityCore<T>()` vs `AddIdentity<T, TRole>()`: `AddIdentity` also registers **cookie** authentication and makes it the default — right for MVC/Razor sites, wrong for a JWT API. We use `AddIdentityCore` + `AddRoles` + `AddSignInManager`.

### What a JWT is
```
eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9 . eyJzdWIiOiI0ZDNm...  . SflKxwRJSMeKKF2QT4fw...
└────────── header ─────────────────┘   └──── payload ────┘   └──── signature ────┘
{"alg":"HS256","typ":"JWT"}            claims (JSON)          HMACSHA256(header.payload, key)
```
Real payload issued by this API (decoded):
```json
{
  "aud": "TaskManagementSystem.Clients",
  "iss": "TaskManagementSystem",
  "exp": 1795456960, "iat": 1795456060, "nbf": 1795456060,
  "sub": "4d3ffd23-9ddd-4e96-73e8-08df4989dcf1",
  "email": "phase5-test-user@example.com",
  "given_name": "Test", "family_name": "User",
  "jti": "42118592-a8e5-4f75-871e-6aac8e02adb4",
  "email_verified": false,
  "role": "User"
}
```
- The payload is **Base64Url-encoded, not encrypted** → anyone can read it. Never put secrets in claims.
- The **signature** proves the token came from us and wasn't modified. Change one character of the payload → signature check fails → 401 (verified in test 9).
- **Stateless:** the server doesn't store issued access tokens; any server instance with the key can validate them. That's why monoliths behind a load balancer scale easily with JWT.

### Why access tokens are short-lived (15 minutes)
A JWT stays valid until `exp` — the server can't "delete" it. Short lifetime limits the damage of a stolen token. **Refresh tokens** (Phase 6) let clients get new access tokens without re-entering the password, and *those* can be revoked (logout).

### Login & request flow
```
REGISTER   POST /api/auth/register ─► ValidationFilter (FluentValidation)
                                    ─► AuthService: BEGIN TRAN; UserManager.CreateAsync (hash);
                                       AddToRoleAsync("User"); COMMIT ─► 201

LOGIN      POST /api/auth/login ─► AuthService
              FindByEmailAsync ─► null/inactive? ─► 401 "Invalid email or password."
              SignInManager.CheckPasswordSignInAsync(lockoutOnFailure: true)
                 locked / wrong? ─► 401 (same generic message)
              GetRolesAsync ─► TokenService.CreateAccessToken ─► 200 { accessToken, expiresAtUtc, user }

PROTECTED  GET /api/auth/me   Authorization: Bearer eyJ...
              UseAuthentication: JwtBearer validates signature, iss, aud, exp, alg
                 invalid/missing ─► 401 (empty) ─► UseStatusCodePages ─► ProblemDetails
                 valid ─► HttpContext.User = ClaimsPrincipal(sub, email, role, ...)
              UseAuthorization: [Authorize] ─► user authenticated? ✔
              AuthController.Me ─► User.GetUserId() ─► AuthService.GetCurrentUserAsync ─► 200
```

### Token validation parameters (what JwtBearer checks)
| Check | Setting | Protects against |
|---|---|---|
| Signature | `IssuerSigningKey`, `ValidateIssuerSigningKey` | Forged / modified tokens |
| Algorithm | `ValidAlgorithms = [HS256]` | `alg: none` and algorithm-confusion attacks |
| Issuer | `ValidIssuer` | Tokens issued by another system |
| Audience | `ValidAudience` | Tokens meant for another API |
| Lifetime | `ValidateLifetime`, `ClockSkew = 30s` | Expired tokens (default skew is 5 min — too generous) |

`MapInboundClaims = false` keeps claim names as in the token (`sub`, `role`). Otherwise ASP.NET Core renames them to long URIs like `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`.

### Secrets: Options pattern + User Secrets
- Non-secret config (`Issuer`, `Audience`, lifetime) → `appsettings.json`.
- **Secret** config (`Jwt:SigningKey`, `SeedAdmin:Password`) → **User Secrets** in Development, stored outside the repo in `%APPDATA%\Microsoft\UserSecrets\df3cdc2a-b840-45be-8712-9cf281fac4bc\secrets.json`. Production uses environment variables (`Jwt__SigningKey`).
- `JwtSettings` is bound and validated with `ValidateDataAnnotations().ValidateOnStart()` → the app refuses to start with a missing or short (< 32 chars) key.

### Validation pipeline (FluentValidation)
```
JSON body ─► model binding ─► ValidationFilter ─► finds IValidator<RegisterRequest>
         ─► errors? throw RequestValidationException ─► middleware ─► 400 { errors: { field: [msgs] } }
         ─► no errors ─► controller action runs
```
`SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true` makes FluentValidation the single source of rules (otherwise MVC adds its own "field is required" messages).

### Security decisions in the login flow
| Decision | Why |
|---|---|
| Same message for unknown email / wrong password / locked / inactive | Prevents **account enumeration** |
| Lockout after 5 failures for 15 min | Slows brute-force and credential-stuffing attacks |
| Role always `User` on register | Clients must never choose their own role (privilege escalation) |
| Password policy not checked at login | Policy changes must not lock out existing users; avoids hints to attackers |
| User re-checked in `/me` (exists & active) | A deactivated user's token is still valid for up to 15 min |
| Registration in a transaction | User creation + role assignment succeed or fail together |

---

## 2. Files created / changed

```
src/TaskManagementSystem/
├── Configuration/
│   ├── JwtSettings.cs                  NEW  Issuer, Audience, SigningKey (validated), lifetime, CreateSigningKey()
│   └── AdminSeedSettings.cs            NEW  SeedAdmin:Email/Password/FirstName/LastName
├── Constants/
│   ├── AppClaimTypes.cs                NEW  sub, email, role, email_verified, ...
│   └── PasswordRules.cs                NEW  shared by Identity options + validator
├── Controllers/AuthController.cs       NEW  POST register, POST login, GET me
├── Data/Seed/DbSeeder.cs               NEW  idempotent admin seeding via UserManager
├── Exceptions/
│   ├── UnauthorizedException.cs        NEW  401
│   └── RequestValidationException.cs   NEW  400 + errors dictionary
├── Extensions/
│   ├── ClaimsPrincipalExtensions.cs    NEW  User.GetUserId()
│   ├── ServiceCollectionExtensions.cs  CHG  + AddIdentityAndJwtAuthentication, Bearer scheme in OpenAPI
│   └── WebApplicationExtensions.cs     CHG  + SeedDatabaseAsync, Swagger persist authorization
├── Filters/ValidationFilter.cs         NEW  global FluentValidation action filter
├── Interfaces/Services/
│   ├── IAuthService.cs                 NEW
│   └── ITokenService.cs                NEW  (+ AccessToken record)
├── Mappings/UserMappings.cs            NEW  ApplicationUser → UserProfileResponse
├── Middleware/ExceptionHandlingMiddleware.cs  CHG  "errors" extension for validation failures
├── Models/DTOs/
│   ├── Auth/RegisterRequest.cs, LoginRequest.cs, AuthResponse.cs   NEW
│   └── Users/UserProfileResponse.cs    NEW
├── Services/
│   ├── AuthService.cs                  NEW
│   └── TokenService.cs                 NEW  (Singleton)
├── Validators/
│   ├── RuleBuilderExtensions.cs        NEW  .StrongPassword()
│   └── Auth/RegisterRequestValidator.cs, LoginRequestValidator.cs   NEW
├── Program.cs                          CHG  ValidationFilter, auth registration, UseAuthentication, seeding
├── appsettings.json                    CHG  + Jwt section (no key!)
└── TaskManagementSystem.csproj         CHG  + JwtBearer 10.0.15, FluentValidation 12.2.0, UserSecretsId
```

### New packages
| Package | Version | Purpose |
|---|---|---|
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.0.15 | Validates Bearer tokens on each request (brings `Microsoft.IdentityModel.JsonWebTokens`) |
| `FluentValidation` | 12.2.0 | Validation rules |
| `FluentValidation.DependencyInjectionExtensions` | 12.2.0 | `AddValidatorsFromAssemblyContaining` |

### DI lifetimes added
| Service | Lifetime | Why |
|---|---|---|
| `TokenService` | Singleton | Stateless; depends only on `IOptions` + `TimeProvider` (singletons) |
| `AuthService` | Scoped | Uses `UserManager`/`SignInManager`/`DbContext` (scoped) |
| `DbSeeder` | Scoped | Uses `UserManager`; resolved from a manually created scope at startup |
| Validators | **Transient** | Tiny, stateless; first Transient registration in the project |

### Middleware order now
```
ExceptionHandling → StatusCodePages → (Swagger, Dev) → HttpsRedirection
  → UseAuthentication → UseAuthorization → Controllers / Health
```

---

## 3. Setup for a fresh clone (secrets)

```powershell
dotnet user-secrets set "Jwt:SigningKey" "<at least 32 random characters>" --project src/TaskManagementSystem
dotnet user-secrets set "SeedAdmin:Email" "admin@taskmanagement.local" --project src/TaskManagementSystem
dotnet user-secrets set "SeedAdmin:Password" "<strong password>" --project src/TaskManagementSystem
dotnet user-secrets list --project src/TaskManagementSystem
```
Generate a strong key in PowerShell:
```powershell
$b = New-Object byte[] 64; [Security.Cryptography.RandomNumberGenerator]::Fill($b); [Convert]::ToBase64String($b)
```

**Your local admin credentials** are whatever you stored in User Secrets — view them any time with `dotnet user-secrets list --project src/TaskManagementSystem`. (They are intentionally not written in this public document.)

---

## 4. Testing in Swagger

1. `dotnet run --project src/TaskManagementSystem --launch-profile https` → Swagger opens.
2. **POST /api/auth/register** → Try it out → body:
   ```json
   { "email": "me@example.com", "password": "Str0ng!Pass", "firstName": "Your", "lastName": "Name" }
   ```
3. **POST /api/auth/login** with the same email/password → copy `accessToken`.
4. Click **Authorize** (top right) → paste the token (without "Bearer") → Authorize → Close.
5. **GET /api/auth/me** (has a 🔒 icon) → Execute → 200 with your profile.
6. Click Authorize → Logout → call `/me` again → 401.
7. Paste the token into https://jwt.io to see its claims (fine for dev tokens; never paste production tokens into websites).

---

## 5. Verification results (2026-11-23)

| # | Test | Result |
|---|---|---|
| 1 | Register valid user | ✅ 201, role `User`, `emailConfirmed:false` |
| 2 | Register same email | ✅ 409 "An account with this email address already exists." |
| 3 | Register invalid input | ✅ 400 with `errors` for email, password (4 rules), firstName, lastName |
| 4 | Register `{}` | ✅ 400, "X is required" for all four fields |
| 5 | No body / malformed JSON | ✅ 400 ProblemDetails |
| 6 | Login wrong password | ✅ 401 "Invalid email or password." |
| 7 | Login unknown email | ✅ 401 — **identical** message |
| 8 | Login correct | ✅ 200, JWT with sub/email/role/email_verified/iss/aud/exp |
| 9 | `/me` without token | ✅ 401 ProblemDetails |
| 10 | `/me` with token | ✅ 200, `lastLoginAtUtc` updated |
| 11 | `/me` with **tampered** token (role → Admin, old signature) | ✅ 401 |
| 12 | `/me` with garbage token | ✅ 401 |
| 13 | Seeded admin login | ✅ 200, `role: Admin`, `email_verified: true` |
| 14 | 5 wrong passwords, then correct one | ✅ 401; `LockoutEnd` set 15 min ahead in DB; log "user … is locked out" |
| 15 | OpenAPI | ✅ `Bearer` security scheme; 🔒 only on `/api/auth/me`, not on login/register |
| 16 | DB password storage | ✅ Hashes `AQAAAAIAAYag…` (84 chars), no plain text |
| 17 | Logs | ✅ No passwords or tokens found in log output |
| 18 | Cleanup | ✅ Test users deleted; only the admin remains |

### Bug found & fixed during verification
The first 400 response had **no `errors` field**. `IProblemDetailsService` serializes using the declared `ProblemDetails` type, so the `Errors` property of the derived `ValidationProblemDetails` was silently dropped. Fix: always create `ProblemDetails` and add errors via `problemDetails.Extensions["errors"]` (same JSON shape).

---

## 6. Logout with JWT — preview of Phase 6
A JWT can't be "deleted". Logout therefore means:
1. The client discards its tokens.
2. The server **revokes the refresh token** (stored hashed in `RefreshTokens`) → no new access tokens can be obtained.
3. The current access token keeps working until it expires (≤ 15 min) — the accepted trade-off of stateless tokens. (Stricter options: token deny-list by `jti`, or checking the user's security stamp on each request — discussed in Phase 6.)

## 7. Common mistakes
- Using `AddIdentity` in an API → cookie auth becomes default, `[Authorize]` redirects to `/Account/Login` (302) instead of returning 401.
- Signing key in `appsettings.json` committed to Git.
- Short/guessable signing keys (`"secret"`), or not restricting algorithms.
- Long-lived access tokens (days) without refresh tokens.
- Accepting `role` from the registration request.
- Different error messages for "email not found" vs "wrong password".
- `UseAuthorization()` before `UseAuthentication()` → `User` is always anonymous → always 401.
- Forgetting `MapInboundClaims = false` and then wondering why `User.FindFirst("sub")` is null.
- Putting sensitive data (phone, address, permissions lists that change often) in the JWT.
- Logging request bodies on auth endpoints (passwords in logs).

## 8. Interview questions
1. Difference between authentication and authorization? Which returns 401 and which 403?
2. How does ASP.NET Core Identity store passwords? What is a salt and why iterations?
3. Explain the three parts of a JWT. Is the payload encrypted?
4. Which token validation parameters matter and what attacks do they prevent?
5. Why are access tokens short-lived? How do refresh tokens help?
6. How do you "log out" a user with stateless JWTs?
7. `AddIdentity` vs `AddIdentityCore` — when do you use each?
8. What is account enumeration and how do you prevent it at login and registration?
9. What does `MapInboundClaims` do?
10. Where do you store secrets in development and production?
11. Why validate options with `ValidateOnStart()`?
12. How does lockout work, and what's the risk of an attacker deliberately locking out users?

## 9. Exercises
1. Log in, wait 15+ minutes (or temporarily set `AccessTokenLifetimeMinutes` to 1), call `/me` again. What happens and why?
2. Change `Jwt:SigningKey` in User Secrets, restart the app, and use an old token. Explain the result.
3. Remove `options.MapInboundClaims = false`, log in, and call `/me`. Why does it fail? Restore it.
4. Move `app.UseAuthentication()` below `app.UseAuthorization()` and test `/me`. Restore it.
5. Add `"jobTitle"` to the registration request through all layers (DTO → validator max 100 → service).
