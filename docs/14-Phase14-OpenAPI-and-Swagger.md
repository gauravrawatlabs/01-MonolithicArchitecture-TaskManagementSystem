# Phase 14 — OpenAPI & Swagger Documentation

> **Phase status:** ✅ Implemented & verified (2026-11-24) — awaiting user review
> **Goal:** Complete, consistent and *tested* API documentation: every endpoint, request/response DTO, status code and the JWT Bearer scheme — plus realistic examples so the API can be tried directly in Swagger UI.

---

## 1. Built up over the phases

| Phase | Documentation feature |
|---|---|
| 4 | OpenAPI document (`Microsoft.AspNetCore.OpenApi`) + Swagger UI (`Swashbuckle.AspNetCore.SwaggerUI`), XML comments as summaries |
| 5 | JWT **Bearer** security scheme → **Authorize** button, 🔒 on protected operations |
| 4–12 | `[ProducesResponseType<T>(status, "application/problem+json")]` on actions, `<response code>` XML docs, `[Tags]` |
| 10 | Enums documented as strings |
| **14** | Rule-based error responses, operationIds, request examples, tag descriptions, metadata, config flag, **contract tests** |

## 2. Concepts

### OpenAPI document vs. Swagger UI
| | What | Where |
|---|---|---|
| **OpenAPI document** | Machine-readable JSON description (OpenAPI **3.1.1**) of every operation, parameter, schema and response | `/openapi/v1.json` |
| **Swagger UI** | Web page that renders the document and lets you call the API ("Try it out") | `/swagger` |
| Clients / tools | Generate typed clients (NSwag, Kiota, openapi-generator), import into Postman, lint in CI | consume the JSON |

### How the document is produced
```
Controllers + routes + [ProducesResponseType] + XML comments + DTO types
        │  ApiExplorer (what endpoints exist, with which parameters/responses)
        ▼
Microsoft.AspNetCore.OpenApi builds the document
        │  Transformers refine it:
        ├─ Document transformer  → title, description, contact, tag descriptions, Bearer scheme
        └─ Operation transformer → operationId, request example, security + 401, 400, 500
        ▼
/openapi/v1.json  ──►  Swagger UI (/swagger)
```

### Documentation rules applied automatically (not by memory)
Attributes document the **specific** outcomes of an action (404, 409, 422 …). The operation transformer adds the **cross-cutting** ones by rule, so no endpoint can forget them:

| Rule | Added response |
|---|---|
| Operation has `[Authorize]` and no `[AllowAnonymous]` | Bearer security requirement + **401** |
| Operation has a request body or query parameters (model binding + FluentValidation) | **400** |
| Every operation | **500** — "no internal details are returned; quote the correlationId" |
| All of them | `application/problem+json` with the `ProblemDetails` schema |

Existing attribute-documented responses are never overwritten (`EnsureProblemResponse` only adds missing codes).

Example — `PATCH /api/tasks/{id}/status` now documents `200, 400, 403, 404, 409` (attributes) + `401, 500` (rules).

### operationId
`Controller_Action` (e.g. `Tasks_ChangeStatus`, `Auth_Login`) — unique, stable names that client generators turn into method names (`client.Tasks_ChangeStatusAsync(...)`) and Swagger UI uses for deep links.

### Request examples
`OpenApiExamples` maps each request DTO to a realistic JSON body (camelCase, string enums, valid values). The operation transformer puts it on the request body's media type (`requestBody.content["application/json"].example`), which Swagger UI pre-fills in **Try it out** — instead of `"string"` / `0` placeholders that fail validation.

> Why not on the schema? In Microsoft.OpenApi v2 (OpenAPI 3.1 / JSON Schema 2020-12) `OpenApiSchema.Example` is **obsolete** — the build warned (CS0618). The media-type example is the standard place for request examples and isn't deprecated.

A contract test proves the examples are *valid*: it posts the documented register and login examples to the real API → 201 and 200.

### Enabling docs by configuration
```json
// appsettings.json               // appsettings.Development.json
"ApiDocumentation": {             "ApiDocumentation": {
  "Enabled": false                  "Enabled": true
}                                 }
```
Production doesn't advertise its API surface; Development (and the integration tests, via `UseSetting`) turn it on. An internal staging server could enable it with an environment variable `ApiDocumentation__Enabled=true` — no code change. Both endpoints are `AllowAnonymous` (reading docs needs no token; the fallback policy would otherwise require one).

### Swagger UI options
`DocExpansion(List)` (operations listed, details collapsed) · `EnableFilter()` (search box) · `EnableTryItOutByDefault()` · `DisplayRequestDuration()` · `EnablePersistAuthorization()` (token survives a page refresh).

### Contract tests — documentation that can't rot
`ApiDocumentationTests` read `/openapi/v1.json` from the running app and assert:

| Test | Guarantees |
|---|---|
| Exact endpoint list (40 operations) | Adding/removing/renaming an endpoint is a **deliberate** change |
| Summary + unique operationId + described tag on every operation | No undocumented endpoints |
| Secured ⇔ Bearer + 401; the 8 anonymous auth/account endpoints have no security | Docs match the authorization rules |
| Every operation has a 2xx and a 500; every 4xx/5xx is `application/problem+json` | Consistent error contract |
| Every `$ref` resolves to a component | Swagger UI never shows "could not resolve reference" |
| `TaskItemStatus` enum is `Todo, InProgress, InReview, Done, Cancelled`; examples present | String enums + examples |
| Register/login examples accepted by the API | Examples are real, not decorative |
| Swagger UI served when enabled; not served when disabled | The config flag works |

---

## 3. Files created / changed

```
src/TaskManagementSystem/
├── Configuration/ApiDocumentationSettings.cs     NEW  "ApiDocumentation:Enabled"
├── Extensions/
│   ├── OpenApiExtensions.cs                      NEW  AddApiDocumentation, UseApiDocumentation, document & operation transformers
│   ├── OpenApiExamples.cs                        NEW  request examples per DTO
│   ├── ServiceCollectionExtensions.cs            CHG  old AddApiDocumentation removed (moved)
│   └── WebApplicationExtensions.cs               CHG  old UseApiDocumentation removed (moved); only seeding left
├── Program.cs                                    CHG  AddApiDocumentation(builder.Configuration)
├── appsettings.json                              CHG  ApiDocumentation:Enabled = false
└── appsettings.Development.json                  CHG  ApiDocumentation:Enabled = true
tests/TaskManagementSystem.IntegrationTests/
├── ApiDocumentationTests.cs                      NEW  9 contract tests
└── Infrastructure/CustomWebApplicationFactory.cs CHG  enables documentation for tests
```

## 4. Using Swagger

1. `dotnet run --project src/TaskManagementSystem --launch-profile https` → browser opens **https://localhost:7188/swagger**.
2. Expand **Authentication → POST /api/auth/register** → the body is pre-filled → change the email → **Execute** → 201.
3. **POST /api/auth/login** (pre-filled) → copy `accessToken`.
4. **Authorize** (top right) → paste the token → every 🔒 operation now sends it.
5. Use the **filter box** to find operations by tag (e.g. `Tasks`).
6. Each operation lists all documented status codes with their schemas; error responses show the ProblemDetails shape.
7. Raw document: **https://localhost:7188/openapi/v1.json** (import it into Postman: *Import → Link*).

---

## 5. Verification results (2026-11-24)

```
dotnet build → 0 warnings, 0 errors   (after replacing the obsolete OpenApiSchema.Example)
dotnet test  → Unit 138/138 · Integration 29/29 (20 existing + 9 contract tests)
```
Live document (Development): OpenAPI **3.1.1**, title + contact URL, 8 described tags,
`PATCH /api/tasks/{id}/status` → `operationId Tasks_ChangeStatus`, responses `200, 400, 403, 404, 409, 401, 500`, example `{"status":"InProgress"}`;
`POST /api/auth/login` → no security, responses `200, 400, 401, 500`; `/swagger` → 200.

---

## 6. Common mistakes
- Documenting status codes by hand on each action and forgetting 401/400/500 on some.
- No operationIds → generated clients get names like `ApiTasksIdStatusPatch`.
- Example values that fail the API's own validation.
- Exposing Swagger in production by accident (or hiding it in all environments, so nobody can use it).
- Docs that drift from the code — fix with contract tests.
- Documenting `application/json` for errors when the API actually returns `application/problem+json`.
- Requiring a token to read the documentation behind a fallback policy.
- Using obsolete OpenAPI model properties instead of reading the warnings.

## 7. Interview questions
1. What's the difference between OpenAPI and Swagger?
2. How does ASP.NET Core know which status codes an endpoint returns?
3. What are document, operation and schema transformers in `Microsoft.AspNetCore.OpenApi`?
4. Why add 401/400/500 by rule instead of attributes?
5. What is an operationId used for?
6. How do you document JWT Bearer authentication so Swagger UI can send the token?
7. Should Swagger be enabled in production? What are the options?
8. How can you test that API documentation stays correct?
9. What changed between OpenAPI 3.0 and 3.1 that affects examples?

## 8. Exercises
1. Generate a typed C# client from `/openapi/v1.json` with **Kiota** or **NSwag** and call `Auth_Login` from a console app.
2. Add response examples (e.g. a ProblemDetails 409 example) to the operation transformer.
3. Add API **versioning** (`/api/v2/...`) with a second document (`/openapi/v2.json`) and a version dropdown in Swagger UI.
4. Lint the document in CI with **Spectral** (`spectral lint openapi.json`).
5. Generate the document at **build time** (`Microsoft.Extensions.ApiDescription.Server`) and commit a snapshot; fail the build if it changes unexpectedly.
