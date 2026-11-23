# Phase 6 — Refresh Tokens, Logout, Email Verification & Password Flows

> **Phase status:** ✅ Implemented & verified (2026-11-23) — awaiting user review
> **Goal:** Long sessions without long-lived access tokens (refresh-token rotation with reuse detection), real logout, email verification, forgot/reset password and change password.

---

## 1. Access token vs. refresh token

| | Access token | Refresh token |
|---|---|---|
| Format | JWT (signed, readable claims) | Opaque random string (512 bits), no meaning on its own |
| Lifetime | 15 minutes | 7 days |
| Sent to | Every protected API call (`Authorization: Bearer`) | Only `POST /api/auth/refresh` and `/logout` |
| Stored on server? | **No** (stateless) | **Yes** — as a SHA-256 hash in `RefreshTokens` |
| Revocable? | **No** — valid until `exp` | **Yes** — set `RevokedAtUtc` |
| If stolen | Usable ≤ 15 min | Detected on reuse; all sessions revoked |

The combination gives **short exposure** (access tokens) plus **long sessions** and **real logout** (refresh tokens).

```
Client                                   API                                  DB (RefreshTokens)
  │ POST /login (email, password) ───────►│ verify password                       │
  │◄──── accessToken(15m) + refreshToken ─│ store SHA256(refreshToken) ──────────►│ R1 active
  │                                        │                                       │
  │ GET /api/... Bearer accessToken ──────►│ validate JWT signature (no DB)        │
  │          ... 15 minutes later: 401 ... │                                       │
  │ POST /refresh { R1 } ─────────────────►│ hash → find R1 → active?              │
  │◄──── new accessToken + R2 ─────────────│ insert R2; revoke R1 → R2 ───────────►│ R1 revoked(→R2), R2 active
  │ POST /logout { R2 } ──────────────────►│ revoke R2 ───────────────────────────►│ R2 revoked
```

## 2. How logout works with JWT (the key question)

A JWT cannot be "deleted" — the server never stored it and validates it purely by signature and expiry. So **logout** means:

1. The client deletes its access + refresh tokens.
2. The server **revokes the refresh token** → no new access tokens can ever be obtained from it.
3. The current access token keeps working until it expires (**≤ 15 minutes**) — verified in the tests ("Access token still valid after logout").

That residual window is the accepted trade-off of stateless JWTs. If it isn't acceptable:

| Option | How | Cost |
|---|---|---|
| Shorter access tokens (e.g. 5 min) | Config only | More refresh calls |
| Deny-list by `jti` | Store revoked `jti`s (e.g. in Redis) until their `exp`; check on every request | A lookup per request (loses statelessness) |
| Security-stamp check | Put Identity's `SecurityStamp` in the token; compare on each request (cached) | DB/cache lookup per request |

**Logout variants implemented:**
- `POST /api/auth/logout` — revokes one refresh token (this device). Anonymous: owning the refresh token is the proof, so logout works even after the access token expired. Idempotent.
- `POST /api/auth/logout-all` — revokes all active refresh tokens of the authenticated user (every device).
- Password **change** and **reset** also revoke all sessions automatically.

## 3. Refresh-token rotation & reuse detection

**Rotation:** every refresh token is single-use. `/refresh` returns a **new** refresh token and revokes the old one, linking it to its successor (`ReplacedByTokenId` — the self-referencing 1:1 relationship from Phase 3).

**Reuse detection:** if a token that was already rotated is presented again, two parties hold copies of it — the real user and an attacker. The server can't tell which is which, so it **revokes every active session of that user** and logs a security warning. The attacker's stolen token (and their successor tokens) become useless; the real user just logs in again.

```
Attacker steals R1 ───────────────────────────────┐
User:     R1 ─refresh─► R2 ─refresh─► R3           │
Attacker:                                 R1 ─refresh─► 💥 R1 already rotated
                                                   → revoke R3 (all active) → both must log in
```

### Race-safe rotation (compare-and-set)
Two requests with the same token at the same moment could both pass the "is it revoked?" check. Rotation is therefore done inside a transaction with an **atomic conditional UPDATE**:

```sql
UPDATE RefreshTokens SET RevokedAtUtc = @now, ReplacedByTokenId = @newId, ...
WHERE Id = @oldId AND RevokedAtUtc IS NULL      -- only ONE request can win
```
`ExecuteUpdateAsync` returns the affected row count: `1` → we won, commit; `0` → someone else already rotated it → roll back our new token and treat it as reuse. Verified: two simultaneous requests returned **200 and 401**.

### Why store only a hash?
If the database leaks, the attacker gets SHA-256 hashes — not usable as tokens. A fast hash (SHA-256) is fine here because the token is 512 random bits: unlike passwords, there's nothing to brute-force, so slow hashing (PBKDF2) isn't needed.

### Where should a client store the refresh token?
| Client | Recommendation |
|---|---|
| Mobile / desktop | Secure OS storage (Keychain, Android Keystore, DPAPI) |
| Browser SPA | Ideally an **httpOnly, Secure, SameSite** cookie set by the server (JavaScript can't read it → XSS-safe); otherwise in memory. Avoid `localStorage`. This API returns it in the body for simplicity; switching to a cookie is an exercise. |

## 4. Email verification

```
Register ─► AccountEmailService: token = GenerateEmailConfirmationTokenAsync(user)
         ─► Base64Url-encode ─► link  http://localhost:3000/confirm-email?userId=…&token=…
         ─► IEmailSender (dev: .html file in src/TaskManagementSystem/emails/)
User clicks link ─► client page ─► POST /api/account/confirm-email { userId, token }
         ─► UserManager.ConfirmEmailAsync ─► EmailConfirmed = 1
Next login/refresh ─► JWT claim email_verified = true   (Phase 7 policy uses it)
```
- Identity tokens are **data-protection based** (encrypted + signed by the app's data-protection keys) and bound to the user's **security stamp**. Lifetime set to **3 hours** (`DataProtectionTokenProviderOptions`).
- Tokens are **Base64Url-encoded** because raw tokens contain `+`, `/`, `=` which break in URLs.
- Email sending failures don't fail registration — the user can call `resend-confirmation`.
- Unverified users can log in (`RequireConfirmedEmail = false`); Phase 7 restricts what they may do.

**Development email delivery:** `PickupDirectoryEmailSender` writes each email to `src/TaskManagementSystem/emails/*.html` (gitignored). Open it in a browser. Each email also shows the exact JSON to paste into Swagger, since there's no front-end yet. Emails are written to files, **not logs**, because they contain one-time tokens.

## 5. Password flows

| Flow | Endpoint | Auth | Key behaviours |
|---|---|---|---|
| Forgot | `POST /api/account/forgot-password` | Anonymous | **Always 202** — same response for unknown emails (no enumeration). Email only sent if the account exists. |
| Reset | `POST /api/account/reset-password` | Anonymous | Token single-use (security stamp changes). Unknown email → same "invalid link" error. Clears lockout. **Revokes all sessions.** |
| Change | `POST /api/account/change-password` | Bearer | Requires current password; new must differ and satisfy policy. **Revokes all sessions.** |
| Resend confirmation | `POST /api/account/resend-confirmation` | Anonymous | Always 202. |

Why single-use? `ResetPasswordAsync` changes the user's **security stamp**; all Identity tokens issued before (reset, confirmation) embed the old stamp and stop validating.

---

## 6. Files created / changed

```
src/TaskManagementSystem/
├── Configuration/
│   ├── EmailSettings.cs                  NEW  FromAddress, PickupDirectory, ClientAppBaseUrl (validated)
│   └── JwtSettings.cs                    CHG  + RefreshTokenLifetimeDays (7)
├── Controllers/
│   ├── AuthController.cs                 CHG  + refresh, logout, logout-all; login passes client IP
│   └── AccountController.cs              NEW  confirm-email, resend-confirmation, forgot/reset/change password
├── Extensions/
│   ├── HttpContextExtensions.cs          NEW  GetClientIpAddress()
│   └── ServiceCollectionExtensions.cs    CHG  EmailSettings, token lifespan 3h, new registrations
├── Helpers/
│   ├── SecureTokenGenerator.cs           NEW  512-bit random token + SHA-256 hash
│   └── IdentityErrorMapper.cs            NEW  Identity errors → field-keyed 400
├── Interfaces/
│   ├── Repositories/IRefreshTokenRepository.cs   NEW
│   └── Services/IAccountService.cs, IAccountEmailService.cs, IEmailSender.cs  NEW;  IAuthService.cs, ITokenService.cs  CHG
├── Models/DTOs/
│   ├── Auth/AuthResponse.cs              CHG  + refreshToken, refreshTokenExpiresAtUtc
│   ├── Auth/RefreshTokenRequest.cs       NEW
│   └── Account/AccountRequests.cs        NEW  5 request records
├── Repositories/RefreshTokenRepository.cs  NEW  GetByHash, Add, TryRevoke (compare-and-set), RevokeAllActiveForUser
├── Services/
│   ├── AuthService.cs                    CHG  refresh rotation, reuse detection, logout(s), confirmation email on register
│   ├── AccountService.cs                 NEW
│   ├── AccountEmailService.cs            NEW  token generation + email content
│   ├── PickupDirectoryEmailSender.cs     NEW  dev email delivery (Singleton)
│   └── TokenService.cs                   CHG  + CreateRefreshToken (GUID v7 ids), HashRefreshToken
├── Validators/
│   ├── Auth/RefreshTokenRequestValidator.cs      NEW
│   └── Account/AccountRequestValidators.cs       NEW  5 validators
├── appsettings.json                      CHG  + RefreshTokenLifetimeDays, Email section
.gitignore                                CHG  + **/emails/
```
No migration — the `RefreshTokens` table was designed in Phase 1 and created in Phase 3.

### Endpoints added
| Method | Endpoint | Auth | Success |
|---|---|---|---|
| POST | `/api/auth/refresh` | Anonymous | 200 new token pair |
| POST | `/api/auth/logout` | Anonymous (refresh token = proof) | 204 |
| POST | `/api/auth/logout-all` | Bearer | 204 |
| POST | `/api/account/confirm-email` | Anonymous | 204 |
| POST | `/api/account/resend-confirmation` | Anonymous | 202 |
| POST | `/api/account/forgot-password` | Anonymous | 202 |
| POST | `/api/account/reset-password` | Anonymous | 204 |
| POST | `/api/account/change-password` | Bearer | 204 |

---

## 7. Testing in Swagger
1. **Register** → open the newest file in `src/TaskManagementSystem/emails/` in a browser → copy the JSON under "Developer testing".
2. **POST /api/account/confirm-email** with that JSON → 204.
3. **Login** → note `accessToken` and `refreshToken`.
4. **POST /api/auth/refresh** with `{ "refreshToken": "<value>" }` → new pair. Call it again with the **old** value → 401, and now the new one fails too (reuse detection).
5. **Forgot password** → new email file → **reset-password** with the token and a new password → log in with it.

---

## 8. Verification results (2026-11-23) — 36/36 passed

| Area | Checks |
|---|---|
| Email verification | Register writes email file with link · wrong token 400 · garbage token 400 · correct token 204 · confirm twice 204 · `email_verified` true after **refresh** (no re-login) · resend for confirmed account sends nothing |
| Rotation & reuse | Refresh returns new, different token · reusing R1 → 401 · R2 then also 401 (family revoked) · unknown token 401 · empty token 400 |
| Race | Two **simultaneous** refreshes with the same token → **200 + 401** (used once) |
| Logout | Logout 204 · refresh after logout 401 · logout twice 204 · access token still valid ≤ 15 min (documented trade-off) · logout-all without token 401 · logout-all revokes other device |
| Change password | Wrong current 400 (`currentPassword`) · same password 400 · success 204 · old session 401 · old password 401 · new password 200 |
| Forgot / reset | Unknown email 202 + no email · known email 202 + email · wrong token 400 · weak password 400 · unknown email 400 (same message) · success 204 · same token again 400 (single-use) · old session 401 · new password 200 |
| Storage & logs | DB holds only 64-char SHA-256 hashes, IP, revocation reason and replacement chain · no JWTs, refresh/reset tokens or passwords in logs · 2 `SECURITY: reuse…` warnings logged (reuse test + race) |
| Cleanup | Test user deleted → its refresh tokens cascade-deleted (0 left) · emails folder removed |

---

## 9. Production notes (not in scope of this phase)
- **Real email delivery:** register an SMTP/provider implementation of `IEmailSender` (e.g. MailKit, SendGrid) in place of `PickupDirectoryEmailSender` — business code doesn't change.
- **Data-protection keys** must be persisted and shared across instances (e.g. file share, Azure Blob, Redis), otherwise email/reset tokens issued by one instance fail on another and after restarts.
- **Forwarded headers** behind a proxy, so `CreatedByIp` is the real client IP.
- **Rate limiting** on login/refresh/forgot-password (`builder.Services.AddRateLimiter`).
- **Cleanup job** deleting refresh tokens that expired long ago.

## 10. Common mistakes
- Storing refresh tokens in plain text.
- Refresh tokens that never expire or are never rotated.
- Rotation without reuse detection (a stolen token keeps working in parallel).
- Check-then-update rotation without an atomic condition (race lets one token be used twice).
- Different responses for known vs. unknown emails on forgot-password / resend.
- Putting tokens in logs ("Sending email with link …").
- Not URL-encoding Identity tokens in links.
- Not revoking sessions after a password change/reset.
- Long-lived access tokens "because logout is hard".
- Using `System.Random` for tokens.

## 11. Interview questions
1. Why have both access and refresh tokens? Why not one long-lived JWT?
2. How does logout work with JWT? What happens to the access token after logout?
3. What is refresh-token rotation? What is reuse detection and why revoke all sessions?
4. How do you make rotation safe under concurrent requests?
5. Why store a hash of the refresh token? Why is SHA-256 acceptable here but not for passwords?
6. Where should a browser SPA store tokens? Trade-offs of `localStorage` vs httpOnly cookies?
7. How are Identity email-confirmation / password-reset tokens generated and validated? Why are they single-use?
8. How do you prevent account enumeration in forgot-password?
9. What should happen to existing sessions after a password reset?
10. How can you invalidate access tokens immediately if required?
11. Why must data-protection keys be shared in a multi-instance deployment?

## 12. Exercises
1. Set `AccessTokenLifetimeMinutes` to 1, log in, wait 2 minutes, call `/me` (401), then refresh and call `/me` again (200).
2. In SSMS, follow one rotation chain: `SELECT Id, ReplacedByTokenId, RevokedReason FROM RefreshTokens ORDER BY CreatedAtUtc`.
3. Add `GET /api/auth/sessions` that lists the current user's active refresh tokens (created date, IP — never the hash) and `DELETE /api/auth/sessions/{id}` to revoke one.
4. Change `/refresh` and `/logout` to read/write the refresh token from an httpOnly cookie instead of the body.
5. Add the built-in rate limiter: max 5 requests/minute per IP on `/api/auth/login` and `/api/account/forgot-password`.
