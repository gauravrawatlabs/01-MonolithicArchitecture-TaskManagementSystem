# Phase 11 — Middleware, Validation, Exception Handling & Logging

> **Phase status:** ✅ Implemented & verified (2026-11-24) — awaiting user review
> **Goal:** Production-grade cross-cutting concerns: Serilog structured logging, correlation IDs, request logging, CORS, HSTS, one consistent error format for every failure, and no secrets in logs.

Validation (FluentValidation + `ValidationFilter`) and exception handling (`ExceptionHandlingMiddleware` + ProblemDetails) were introduced in Phases 4–5 and used by every module since. This phase **finishes** them and adds logging/observability.

---

## 1. The final middleware pipeline

```
Request ──► ① CorrelationIdMiddleware ─ reads/creates X-Correlation-ID, LogContext.Push("CorrelationId")
            ② Serilog request logging ── timer starts … logs ONE line with the FINAL status at the end
            ③ ExceptionHandlingMiddleware  try { next } catch → ProblemDetails (+ log once)
            ④ UseStatusCodePages ──────── empty 401/403/404 → ProblemDetails
            ⑤ UseHsts (non-Development) ─ Strict-Transport-Security header
            ⑥ UseHttpsRedirection ─────── http → https
            ⑦ Swagger / OpenAPI (Development only)
            ⑧ UseRouting ──────────────── pick the endpoint
            ⑨ UseCors("Frontend") ─────── origin check, preflight answers (no token needed)
            ⑩ UseAuthentication ───────── JWT → HttpContext.User
            ⑪ UseAuthorization ────────── fallback / role / claims policies → 401 / 403
            ⑫ Endpoints ───────────────── controllers (+ ModelState → 400, ValidationFilter → 400),
                                            /health (anonymous)
Response ◄── travels back out through ⑪ … ① (headers like X-Correlation-ID are added via OnStarting)
```

### Why this order (and why it changed from Phase 1)
Phase 1 planned `ExceptionHandling → CorrelationId → RequestLogging`. Implementing it showed a flaw: if the exception handler is **outside** the correlation scope, the most important log line of all — the unhandled exception — has **no CorrelationId**. So:

| Position | Reason |
|---|---|
| CorrelationId **first** | Everything after it (including exception logs) runs inside `LogContext.PushProperty("CorrelationId", …)` |
| Request logging **before** the exception handler | It observes the response *after* the handler turned an exception into a 500, so the summary line has the real status code |
| Exception handler before everything else | Catches exceptions from routing, auth, filters, controllers, services, EF Core |
| `UseRouting → UseCors → UseAuthentication → UseAuthorization` | CORS needs the endpoint; CORS preflight (OPTIONS) must succeed **without** a token; authentication must populate `User` before authorization reads it |

## 2. Serilog: structured logging

**Structured** = each event is a template + named properties, not a string:
```csharp
logger.LogInformation("User {UserId} logged in", user.Id);
// → message "User f2d5… logged in"  +  property UserId = f2d5…   (searchable, filterable, aggregatable)
```

| Environment | Console output |
|---|---|
| Development | `[00:28:20 INF] trace-login-001 TaskManagementSystem.Services.AuthService` / `User f2d5… logged in` |
| Production | One JSON object per line (`RenderedCompactJsonFormatter`) for Seq / Elasticsearch / Azure Monitor / Datadog |

Real Production event captured during testing:
```json
{"@t":"2026-11-23T18:59:50.8358852Z","@m":"HTTP \"POST\" \"/api/auth/login\" responded 500 in 994.8 ms",
 "@l":"Error","@tr":"8d7ee812cf2914c63ea0d9d74b009d12","ClientIp":"127.0.0.1","RequestMethod":"POST",
 "RequestPath":"/api/auth/login","StatusCode":500,"Elapsed":994.7843,
 "SourceContext":"Serilog.AspNetCore.RequestLoggingMiddleware","CorrelationId":"prod-incident-43",
 "Application":"TaskManagementSystem","Environment":"Production"}
```

**Configuration split**
- *Levels* (`MinimumLevel`, per-namespace `Override`) → `appsettings*.json` → changeable without recompiling.
- *Sinks/format* → chosen in code by environment (`LoggingExtensions.AddSerilogLogging`) → no fragile JSON-array merging between appsettings files.
- Startup logger (`Log.Logger = …CreateLogger()`) catches failures **before** configuration is loaded (missing secret, bad config); `Log.CloseAndFlushAsync()` in `finally` flushes buffered events on exit.
- `catch (Exception) when (exception is not HostAbortedException)` — `dotnet ef` intentionally aborts the host after building it; that isn't a crash (verified: `dotnet ef migrations has-pending-model-changes` still works).

### Log levels used
| Level | Used for |
|---|---|
| Information | Business events (user registered, task created, status changed) and the request summary |
| Warning | Expected failures: 4xx from `AppException`s, failed logins, revoked-token reuse (`SECURITY: …`) |
| Error | Unhandled exceptions (once, with stack trace) and 5xx request summaries |
| Debug | EF Core's own copies of failures that our code already logs (see §5) |
| Verbose | `/health` request summaries (below the minimum level → not written) |

## 3. Correlation IDs

```
Client ──X-Correlation-ID: trace-login-001──► API
  every log line of that request: CorrelationId = trace-login-001
  response header:                X-Correlation-ID: trace-login-001
  every error body:               "correlationId": "trace-login-001"
```
- A valid incoming id is **reused** (front-end, API gateway or calling service can trace a whole flow); otherwise one is generated (`Guid` "N" format).
- **Log injection defence:** incoming values must be ≤ 64 chars of `[A-Za-z0-9-_.]`. `"FAKE LOG LINE <script>"` and 100-char values are replaced (verified).
- `traceId` (W3C trace context, from `Activity`) is also in every ProblemDetails and every JSON log event (`@tr`) — useful once distributed tracing (OpenTelemetry) is added.
- CORS exposes `X-Correlation-ID`, so browser code can read it and show "Error reference: …" to users.

**Support workflow:** user reports "it failed, reference prod-incident-43" → search logs for `CorrelationId = "prod-incident-43"` → the exception with stack trace appears, while the user only ever saw a generic message.

## 4. One error format for every failure

| Source of the error | How it reaches the client |
|---|---|
| FluentValidation (`ValidationFilter`) | `RequestValidationException` → middleware |
| **Model binding** (malformed JSON, `"Urgent"` for an enum, `page=abc`) | `InvalidModelStateResponseFactory` → **throws** `RequestValidationException` → middleware (new) |
| Identity errors (password policy, invalid token) | `RequestValidationException` → middleware |
| `NotFound/Conflict/Forbidden/BusinessRule/Unauthorized` exceptions | middleware |
| JwtBearer 401/403, unknown routes 404 | empty response → `UseStatusCodePages` → `IProblemDetailsService` |
| Any other exception | middleware → 500, generic message outside Development |

All go through `IProblemDetailsService`, and `AddProblemDetailsWithCorrelationId()` adds `correlationId` to **all** of them:
```json
{ "type": "...rfc9110#section-15.5.1", "title": "One or more validation errors occurred.", "status": 400,
  "detail": "One or more validation errors occurred.", "instance": "POST /api/tasks",
  "traceId": "00-…", "correlationId": "0a482eee…",
  "errors": { "priority": ["The input was not valid."] } }
```

**Fixes from Phase 10's open items**
1. `AllowInputFormatterExceptionMessages = false` → no more `TaskManagementSystem.Models.DTOs…` type names or byte positions in 400s.
2. `ValidationErrorKeys.Normalize` → keys always match what the client sent: `"$.priority"` → `priority`, `"Status"` → `status`, `""` → `body`.

## 5. Log noise and "log each failure exactly once"
EF Core logs failed connections, queries, commands and `SaveChanges` at **Error** — even when our code handles them (duplicate name → 409). Before the fix, one unreachable-database request produced **four** Error events (two from EF with full stack traces). After:
```csharp
options.ConfigureWarnings(w => w.Log(
    (CoreEventId.SaveChangesFailed, LogLevel.Debug), (CoreEventId.QueryIterationFailed, LogLevel.Debug),
    (RelationalEventId.CommandError, LogLevel.Debug), (RelationalEventId.ConnectionError, LogLevel.Debug)));
```
Safe because every such exception still either gets handled (and logged by us at the right level) or reaches `ExceptionHandlingMiddleware` (logged once as Error with stack trace and correlation id). Verified: 1 Error + 1 request summary per failure; 10 parallel duplicate creates → **0** Error lines, 9 Warnings ("failed with 409").

## 6. Secrets never reach the logs — by design
| Risk | Mitigation |
|---|---|
| Passwords, tokens in request bodies | Request logging never logs bodies |
| `Authorization: Bearer …` header | Headers are never logged |
| Tokens in URLs | Query strings aren't logged (`RequestPath` only) and the API never puts tokens in URLs |
| SQL parameter values | `EnableSensitiveDataLogging()` is never called → EF logs `@normalizedEmail='?'` |
| Email confirmation / reset tokens | Written to the dev pickup folder, never logged |
| Log injection via headers | Correlation id whitelist |

Verified: after register/login/refresh/validation tests, logs contain no password, no `eyJhbGci…` JWT and no `Bearer `.

## 7. CORS & HSTS
- **CORS** — `Cors:AllowedOrigins` (`http://localhost:3000`). Exact origins (validated at startup; no paths/trailing slash), any header/method, exposes `X-Correlation-ID`, preflight cached 10 min, no credentials (tokens are in headers, not cookies). CORS is a **browser** protection — it doesn't stop curl/Postman/servers; authorization does.
- **HSTS** (`UseHsts`, non-Development) — tells browsers to use HTTPS only for this host for a period; combined with `UseHttpsRedirection`.

---

## 8. Files created / changed

```
src/TaskManagementSystem/
├── Configuration/CorsSettings.cs                 NEW  allowed origins, policy name
├── Extensions/
│   ├── LoggingExtensions.cs                      NEW  AddSerilogLogging, UseSerilogRequestSummary, AddProblemDetailsWithCorrelationId
│   ├── HttpContextExtensions.cs                  CHG  + GetCorrelationId()
│   └── ServiceCollectionExtensions.cs            CHG  + AddCorsPolicy; EF failure events → Debug
├── Helpers/ValidationErrorKeys.cs                NEW  key normalization + ModelState → errors
├── Middleware/CorrelationIdMiddleware.cs         NEW
├── Filters/ValidationFilter.cs                   CHG  uses ValidationErrorKeys
├── Program.cs                                    CHG  startup logger, try/catch/finally, final pipeline,
│                                                      model-binding errors → RequestValidationException,
│                                                      AllowInputFormatterExceptionMessages = false
├── appsettings.json                              CHG  "Logging" → "Serilog" levels; + "Cors"
├── appsettings.Development.json                  CHG  Serilog override shows EF SQL in Development
└── TaskManagementSystem.csproj                   CHG  + Serilog.AspNetCore 10.0.0
README.md, docs/05-Phase5-…md                     FIX  secret-generation command works in Windows PowerShell 5.1
```

---

## 9. Verification results (2026-11-24)

**Run A — Development (19/19 passed)**
| Area | Result |
|---|---|
| Correlation id | generated (32 hex) · valid incoming `client-abc-123` reused · `FAKE LOG LINE <script>` replaced · 100-char value replaced |
| Error format | 401 body `correlationId` = response header · 404 unknown route has it · bad enum → `{"priority":["The input was not valid."]}`, no type names, has `instance` · query errors `status`, `page` (camelCase) · malformed JSON: no byte positions · empty body → `body` · FluentValidation 400 has the same shape |
| CORS | preflight from `localhost:3000` → 204, ACAO set, max-age 600 · from `evil.example` → no ACAO · real response exposes `X-Correlation-ID` |
| Log noise | 10 parallel duplicate creates → 1×201, 9×409, **0 `[ERR]` lines**, 9 warnings |
| Log content | 8 log lines carry the client's id `trace-login-001` (SQL, "User … logged in", request summary) · `/health` not logged · no password/JWT/`Bearer` in logs |

**Run B — Production mode, database unreachable**
- Client: `500` with only *"An unexpected error occurred. Please try again later."* + `correlationId: prod-incident-43` + `X-Correlation-ID` header.
- Server: JSON events with `Application`, `Environment`, `ClientIp`, `StatusCode`, `Elapsed`, `CorrelationId`, `@tr`. After the EF fix: **exactly 2 events** — middleware Error (with exception) + request summary (Error, 500).
- Missing `SeedAdmin` secrets → a clear Warning, app still starts.

**Bugs found & fixed in this phase**
1. EF logged each DB failure 2–3 extra times at Error → downgraded EF's own failure events.
2. The README/Phase 5 key-generation command used `RandomNumberGenerator.Fill`, which doesn't exist in Windows PowerShell 5.1 → replaced with `RandomNumberGenerator.Create().GetBytes($b)` (verified).

`dotnet ef migrations has-pending-model-changes` → "No changes" (EF tooling unaffected by the new `try/catch`).

---

## 10. Common mistakes
- String-concatenated log messages (`$"User {id} logged in"`) — lose structure; use templates.
- Logging request bodies or headers "for debugging" → passwords and tokens in log stores.
- Trusting incoming correlation ids blindly (log/header injection).
- Exception handler outside the correlation scope → the crash log has no correlation id.
- Several components logging the same exception → alert noise, wrong error counts.
- Returning exception messages/stack traces to clients in production.
- Different error formats for model-binding errors vs. validation errors vs. exceptions.
- `AllowAnyOrigin()` on an authenticated API; `UseCors` after `UseAuthorization` (preflights fail with 401).
- Logging at Information inside hot loops / health checks → cost and noise.
- Not flushing the logger on shutdown (last events lost).

## 11. Interview questions
1. What is structured logging and why is it better than plain text logs?
2. What is a correlation id? How is it different from a trace id?
3. How do you make sure every log line of a request carries the correlation id?
4. Why must the correlation middleware run before the exception handler?
5. How do you prevent passwords/tokens from appearing in logs?
6. What is log injection and how do you prevent it?
7. How do you give model-binding errors and FluentValidation errors the same format?
8. Why would you downgrade a framework's error logs? When is that dangerous?
9. Explain the middleware order around routing, CORS, authentication and authorization.
10. What does HSTS do, and why only outside Development?
11. Does CORS protect an API from unauthorized access? Why not?
12. Why use a separate startup (bootstrap) logger?

## 12. Exercises
1. Add a rolling-file sink in Development (`logs/log-.txt`, daily, 7 files kept) and gitignore the folder.
2. Run Seq locally (`docker run -e ACCEPT_EULA=Y -p 5341:80 datalust/seq`), add `Serilog.Sinks.Seq`, and search by `CorrelationId`.
3. Add `UserId` to the LogContext for **all** log lines of authenticated requests (small middleware after `UseAuthentication`).
4. Add the built-in rate limiter to `/api/auth/login` and log rejected requests as warnings with the client IP.
5. Replace `ExceptionHandlingMiddleware` with an `IExceptionHandler` implementation + `app.UseExceptionHandler()` and compare.
