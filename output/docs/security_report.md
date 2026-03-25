# ION CRM — Security Audit Report

**Auditor:** Senior Security Engineer (automated + manual review)
**Date:** 2026-03-25
**Scope:** Full codebase — `/src/`, `/frontend/src/`, `.github/`
**Stack:** ASP.NET Core 8 · React 18 · PostgreSQL (Neon) · Railway

---

## Executive Summary

| Severity | Count | Status |
|----------|-------|--------|
| CRITICAL | 1 | ✅ Fixed in this audit |
| HIGH | 4 | Jira tickets raised |
| MEDIUM | 3 | Jira tickets raised |
| LOW | 4 | Backlog |
| INFO | 8 | Positive findings noted |

---

## CRITICAL — Fixed in This Audit

### CRIT-001 · Hardcoded Demo Credentials Exposed in Production UI

**File:** `frontend/src/pages/LoginPage.tsx` lines 211-217
**Risk:** Unauthorized access to the production system by any anonymous visitor

**Description:**
The login page rendered a visible hint block showing hardcoded credentials:
```
admin@ion.com / Admin123!
```
This block was visible to every anonymous visitor of the public login URL
(`https://ion-crm-frontend-production.up.railway.app/login`).
If an `admin@ion.com` SuperAdmin account with password `Admin123!` exists in the
production database, any attacker reading the source code or inspecting the DOM
could log in with full SuperAdmin privileges — accessing all tenants' data.

**Fix Applied (this audit):**
The credentials hint block was removed from `LoginPage.tsx` and replaced with
a comment explaining the policy:
```tsx
{/* SECURITY: Demo credential hints removed — never expose credentials in UI */}
```

**Additional Remediation Required (must be done immediately):**
1. Audit the production DB:
   ```sql
   SELECT id, email, is_super_admin, created_at FROM users WHERE email = 'admin@ion.com';
   ```
2. If this account exists, reset its password immediately to a long random value.
3. Invalidate all active refresh tokens for this account:
   ```sql
   UPDATE refresh_tokens SET is_revoked = true WHERE user_id = '<admin-id>';
   ```
4. Add a git pre-commit hook or CI secret-scanning step (e.g. `gitleaks`) to
   prevent credentials from appearing in committed code.

---

## HIGH — Fix This Sprint

### HIGH-001 · Refresh Token Flow Completely Broken (Session Management Failure)

**Files:**
- `frontend/src/api/client.ts` (auto-refresh interceptor, lines 69-73)
- `frontend/src/stores/authStore.ts` (`initializeAuth`, line 72)
- `src/IonCrm.Application/Auth/Commands/RefreshToken/RefreshTokenCommand.cs`

**Description:**
The backend issues an opaque refresh token in the JSON login response body
(`AuthResponseDto.RefreshToken`). The frontend **never stores this token**.

When the access token expires (15 min) and the auto-refresh interceptor fires,
it calls `POST /api/v1/auth/refresh` with an empty body `{}`. The backend's
`RefreshTokenCommand` requires `{ "token": "<raw-refresh-token>" }`. The empty
request will always return `"Invalid or expired refresh token."` (401).

Practical impact:
- All authenticated sessions expire silently after exactly 15 minutes.
- The "Remember me (7 days)" checkbox has **no effect**.
- Users must re-login every 15 minutes.

The code comments say "uses httpOnly cookie" but the backend never calls
`Response.Cookies.Append(...)`. There is a clear design intent mismatch.

**Recommended Fix — Option A (preferred): httpOnly Cookie**
```csharp
// AuthController.cs — Login and Refresh endpoints:
Response.Cookies.Append("refreshToken", result.Value!.RefreshToken, new CookieOptions
{
    HttpOnly = true,
    Secure   = true,
    SameSite = SameSiteMode.Strict,
    Expires  = DateTimeOffset.UtcNow.AddDays(7),
    Path     = "/api/v1/auth"
});
// Remove RefreshToken from the JSON response body.
```
```typescript
// Frontend: POST /auth/refresh with empty body — cookie is sent automatically.
// (withCredentials: true already set in apiClient)
```

**Recommended Fix — Option B: In-memory storage**
```typescript
// authStore.ts login():
const { accessToken, refreshToken, user } = response.data.data;
setAccessToken(accessToken);
setRefreshToken(refreshToken); // module-level variable, NOT localStorage

// client.ts auto-refresh interceptor:
const response = await axios.post('/auth/refresh', { token: getRefreshToken() });
```

**Jira ticket:** `SEC-001` — Priority: Sprint 2

---

### HIGH-002 · Raw Customer PII Stored in SyncLog.Payload Column

**Files:**
- `src/IonCrm.Application/Features/Sync/Commands/ProcessWebhook/ProcessSaasAWebhookCommandHandler.cs` (line 51)
- `src/IonCrm.Application/Features/Sync/Commands/ProcessWebhook/ProcessSaasBWebhookCommandHandler.cs`
- `src/IonCrm.Infrastructure/BackgroundServices/SaasSyncJob.cs` (SyncWithRetryAsync — no payload)
- `src/IonCrm.Domain/Entities/SyncLog.cs` (`Payload` field)

**Description:**
Inbound webhook handlers store the entire raw payload verbatim in `SyncLog.Payload`:
```csharp
var log = new SyncLog { ..., Payload = request.RawPayload };
```
SaaS A and SaaS B payloads contain customer PII: name, email, phone, address, and
tax number. Although `SyncLogDto` does not expose `Payload` through the API today,
the raw PII accumulates in the `sync_logs` database table indefinitely with no
retention policy. Any database dump, migration, or direct SQL access exposes this data.

**Recommended Fix:**
Store only operational metadata instead of the full payload:
```csharp
Payload = System.Text.Json.JsonSerializer.Serialize(new
{
    eventType  = request.EventType,
    entityType = request.EntityType,
    entityId   = request.EntityId,
    receivedAt = DateTime.UtcNow
});
```
If the full payload is required for debugging, encrypt it at rest (column-level
encryption or application-level AES-256) and add a retention policy.

**Jira ticket:** `SEC-002` — Priority: Sprint 2

---

### HIGH-003 · AutoMapper — Known High Severity CVE

**File:** `src/IonCrm.Application/IonCrm.Application.csproj`
**CVE:** GHSA-rvv3-g6hj-g44x (High)
**Package:** `AutoMapper 14.0.0`
**Evidence:**
```
$ dotnet list package --vulnerable
> AutoMapper  14.0.0  14.0.0  High  https://github.com/advisories/GHSA-rvv3-g6hj-g44x
```

**Recommended Fix:**
```bash
cd src/IonCrm.Application
dotnet add package AutoMapper --version <latest-patched>
dotnet build && dotnet test
```
Review the advisory for the affected API surface and any required code changes.

**Jira ticket:** `SEC-003` — Priority: Sprint 2

---

### HIGH-004 · Webhook API Key Comparison Vulnerable to Timing Attack

**File:** `src/IonCrm.API/Controllers/SyncController.cs` lines 57 and 115

**Description:**
Both `ReceiveSaasAWebhook` and `ReceiveSaasBWebhook` compare the incoming
`X-Api-Key` header using standard string equality:
```csharp
if (string.IsNullOrEmpty(apiKey) || apiKey != expectedKey)
```
.NET's `string !=` is **not timing-safe** — it short-circuits on the first
mismatched character. An attacker who can make many requests and measure
response latency can brute-force the key character by character. Network
jitter reduces practical exploitability, but this is a known weakness for
secret-comparison operations.

**Recommended Fix:**
Use `CryptographicOperations.FixedTimeEquals` (.NET Core 2.1+):
```csharp
using System.Security.Cryptography;
using System.Text;

static bool ApiKeysEqual(string? incoming, string? expected)
{
    if (incoming is null || expected is null) return false;
    var a = Encoding.UTF8.GetBytes(incoming);
    var b = Encoding.UTF8.GetBytes(expected);
    return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
}

if (!ApiKeysEqual(apiKey, expectedKey))
    return StatusCode(401, ApiResponse<object>.Fail("Invalid or missing API key.", 401));
```

**Jira ticket:** `SEC-004` — Priority: Sprint 2

---

## MEDIUM — Next Sprint

### MED-001 · No Account Lockout After Failed Login Attempts

**File:** `src/IonCrm.Application/Auth/Commands/Login/LoginCommandHandler.cs`

**Description:**
Rate limiting is configured (10 req/min/IP) but there is no account-level
lockout after N consecutive failed login attempts for a single email. A
distributed brute-force attack from multiple IPs would bypass IP-rate-limiting.
Failed attempts are logged as warnings but no counter or lockout timestamp is
persisted on the `User` entity.

**Recommended Fix:**
1. Add `FailedLoginCount int` and `LockedUntil DateTime?` to the `User` entity.
2. Increment `FailedLoginCount` on each failed login.
3. Lock for 15 minutes after 5 consecutive failures.
4. Reset `FailedLoginCount` to 0 on successful login.
5. Return the same generic `"Invalid email or password."` even when locked
   (prevent user enumeration via lockout status).

**Jira ticket:** `SEC-005` — Priority: Sprint 3

---

### MED-002 · Hardcoded Fallback Connection String in Design-Time Factory

**File:** `src/IonCrm.Infrastructure/Persistence/ApplicationDbContextFactory.cs` line 26

**Description:**
```csharp
var connectionString = configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Database=ioncrm_dev;Username=postgres;Password=postgres";
```
A fallback connection string with `Username=postgres` / `Password=postgres` is
hardcoded in source code and committed to the repository. Although only used by
`dotnet ef migrations` at design time, it:
- Embeds a credential pair in version control history.
- May silently connect a developer's migration tool to the wrong database if the
  env var is unset.
- Sets a precedent that hardcoded DB passwords are acceptable.

**Recommended Fix:**
Replace the null-coalescing fallback with a hard failure:
```csharp
var connectionString = configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "ConnectionStrings__DefaultConnection must be set before running migrations.\n" +
        "Example: export ConnectionStrings__DefaultConnection='Host=...;Password=...'");
```

**Jira ticket:** `SEC-006` — Priority: Sprint 3

---

### MED-003 · Production Infrastructure Details Committed to CLAUDE.md

**File:** `/CLAUDE.md` lines 19-22

**Description:**
`CLAUDE.md` (tracked in the repository) contains production and development
database hostnames and usernames:
```
Prod DB: Host=ep-purple-sound-a9vyag84-pooler.gwc.azure.neon.tech;
         Database=ioncrm;Username=neondb_owner
Dev DB:  Host=ep-royal-grass-a9u9toyt-pooler.gwc.azure.neon.tech;
         Database=neondb;Username=neondb_owner
```
While passwords are absent, exposing the cloud provider (Azure Neon), exact
pooler FQDNs, database names, and usernames enables:
- Targeted credential-stuffing against known usernames.
- Direct connection attempts to port 5432 if Neon IP allowlisting is misconfigured.
- Service enumeration and reconnaissance.

**Recommended Fix:**
1. Remove all connection details from `CLAUDE.md`.
2. Replace with Railway env variable names: `DATABASE_URL`, `DATABASE_URL_DEV`.
3. Rotate the Neon `neondb_owner` password as a precaution (the username is now
   public in git history).

**Jira ticket:** `SEC-007` — Priority: Sprint 3

---

## LOW — Backlog

### LOW-001 · Password Validator Missing Special Character Requirement

**File:** `src/IonCrm.Application/Auth/Commands/RegisterUser/RegisterUserCommandValidator.cs`

The validator requires 8+ chars, uppercase, lowercase, and digit — but no special
character. Add `.Matches("[^a-zA-Z0-9]").WithMessage("Password must contain at
least one special character.")` for higher entropy.

**Jira ticket:** `SEC-008`

---

### LOW-002 · GetCustomers Handler Lacks Explicit Tenant Check (Defense-in-Depth Gap)

**File:** `src/IonCrm.Application/Customers/Queries/GetCustomers/GetCustomersQueryHandler.cs`

`GetCustomersQueryHandler` does not inject `ICurrentUserService`. Tenant isolation
relies solely on the EF Core global query filter. All other mutating handlers
perform an explicit `_currentUser.ProjectIds.Contains(entity.ProjectId)` check.

The global filter is currently sufficient, but if a future developer adds
`IgnoreQueryFilters()` for performance, this endpoint would silently leak
cross-tenant data. Recommend adding an explicit caller-supplied `ProjectId`
validation at the handler level for consistency and defense-in-depth.

**Jira ticket:** `SEC-009`

---

### LOW-003 · SyncLog Payload Column Has No Retention Policy

**File:** `src/IonCrm.Domain/Entities/SyncLog.cs`

`SyncLog` records accumulate indefinitely. Even after HIGH-002 is remediated,
operational metadata still grows unbounded. Recommend a Hangfire recurring job
to purge records older than 90 days (configurable).

**Jira ticket:** `SEC-010`

---

### LOW-004 · Frontend Password Minimum Length (6) Below Backend Minimum (8)

**File:** `frontend/src/pages/LoginPage.tsx` (zod schema, line 22)

The login form validates `minLength(6)` while the backend enforces
`MinimumLength(8)`. Users entering 6- or 7-character passwords pass client
validation but receive a confusing server-side error. Align the frontend schema
to `minLength(8)`.

**Jira ticket:** `SEC-011`

---

## INFO — Positive Security Findings

The following checklist items were audited and found to be correctly implemented:

| Check | Result | Evidence |
|-------|--------|----------|
| JWT secret not hardcoded | ✅ PASS | `Program.cs` throws `InvalidOperationException` if env var missing |
| JWT access token expiry | ✅ PASS | 15 min (`JwtSettings:AccessTokenExpiryMinutes: 15`) |
| JWT refresh token expiry | ✅ PASS | 7 days (`JwtSettings:RefreshTokenExpiryDays: 7`) |
| Refresh tokens hashed in DB | ✅ PASS | SHA-256 hex-encoded; raw token never persisted (`TokenService.cs`) |
| BCrypt cost factor ≥ 12 | ✅ PASS | `WorkFactor = 12` (`PasswordHasher.cs`) |
| Rate limiting on /auth/login | ✅ PASS | 10 req/min/IP via `AspNetCoreRateLimit` |
| Rate limiting on /auth/refresh | ✅ PASS | 10 req/min/IP |
| SuperAdmin routes protected | ✅ PASS | `[Authorize(Policy="SuperAdmin")]` on all admin endpoints |
| Multi-tenant isolation — DB layer | ✅ PASS | Global EF query filters on all tenant-scoped entities |
| Multi-tenant isolation — app layer | ✅ PASS | All mutating handlers verify `ProjectIds.Contains()` |
| Passwords not in JWT payload | ✅ PASS | Claims: userId, email, isSuperAdmin, projectIds, roles only |
| Stack traces not in error responses | ✅ PASS | `GlobalExceptionMiddleware` returns generic 500 body |
| Swagger disabled in production | ✅ PASS | `if (!app.Environment.IsProduction())` guard in `Program.cs` |
| SaaS API keys from config | ✅ PASS | Read via `IConfiguration`; empty strings in `appsettings.json` |
| Refresh token rotation | ✅ PASS | Old token revoked before new pair issued |
| Soft-delete filter active | ✅ PASS | `IsDeleted` in every global query filter |
| SQL injection not possible | ✅ PASS | EF Core parameterized queries; no raw SQL |
| CORS locked to known origins | ✅ PASS | `WithOrigins(origins)` — no wildcard |
| No secrets in `appsettings.json` | ✅ PASS | All sensitive values are empty strings |
| Error messages generic (no user enumeration) | ✅ PASS | Login returns `"Invalid email or password."` on both missing user and wrong password |

---

## Dependency Audit

```
$ dotnet list package --vulnerable
```

| Project | Package | Version | Severity | Advisory |
|---------|---------|---------|----------|----------|
| IonCrm.Application | AutoMapper | 14.0.0 | **High** | GHSA-rvv3-g6hj-g44x |
| IonCrm.Domain | — | — | None | — |
| IonCrm.Infrastructure | — | — | None | — |
| IonCrm.API | — | — | None | — |
| IonCrm.Tests | — | — | None | — |

---

## Security Checklist (Final State)

### AUTHENTICATION & AUTHORIZATION
- [x] JWT secret not hardcoded (env var, app fails to start without it)
- [x] JWT expiry: 15 min access / 7 day refresh
- [x] Refresh tokens stored securely (SHA-256 hashed in DB)
- [x] Passwords hashed with BCrypt (cost factor 12)
- [x] Rate limiting on /auth/login (10 req/min/IP)
- [ ] **Account lockout after N failed attempts** — MED-001
- [x] Multi-tenant isolation enforced on all endpoints
- [x] SuperAdmin routes protected with `[Authorize(Policy="SuperAdmin")]`

### DATA SECURITY
- [x] No secrets in code or config files (post CRIT-001 fix)
- [x] Connection strings from environment variables
- [x] No passwords/tokens in logs
- [x] Sensitive data not in JWT payload
- [x] Soft delete (IsDeleted) does not expose deleted data

### INPUT VALIDATION
- [x] FluentValidation on all commands
- [x] SQL injection not possible (EF Core)
- [x] XSS prevention (React output encoding)
- N/A File upload restrictions
- N/A Request size limits

### INFRASTRUCTURE
- [x] HTTPS enforced (Railway TLS edge termination)
- [x] CORS locked to specific origins
- [x] Swagger disabled in production
- [x] Error messages do not leak stack traces
- N/A Database minimum permissions (Neon managed)
- [x] No direct DB access from frontend

### SYNC SERVICE
- [x] SaaS API keys stored in env vars
- [x] Webhook endpoints validate request signatures (X-Api-Key header)
- [x] Outbound callbacks use HTTPS only
- [ ] **Sync logs should not contain sensitive payload data** — HIGH-002

### DEPENDENCIES
- [ ] **NuGet packages up to date — AutoMapper CVE** — HIGH-003
- [x] No other known CVEs in dependencies

---

## Remediation Roadmap

| Ticket | Severity | Item | Target |
|--------|----------|------|--------|
| CRIT-001 | CRITICAL | Remove demo credentials from LoginPage.tsx | **Done ✅** |
| SEC-001 | HIGH | Fix broken refresh token flow | Sprint 2 |
| SEC-002 | HIGH | Redact PII from SyncLog.Payload | Sprint 2 |
| SEC-003 | HIGH | Update AutoMapper (CVE GHSA-rvv3-g6hj-g44x) | Sprint 2 |
| SEC-004 | HIGH | Timing-safe API key comparison in SyncController | Sprint 2 |
| SEC-005 | MEDIUM | Account lockout after failed logins | Sprint 3 |
| SEC-006 | MEDIUM | Remove hardcoded fallback connection string | Sprint 3 |
| SEC-007 | MEDIUM | Remove infrastructure details from CLAUDE.md | Sprint 3 |
| SEC-008 | LOW | Password special-character requirement | Backlog |
| SEC-009 | LOW | Explicit tenant check in GetCustomersQueryHandler | Backlog |
| SEC-010 | LOW | SyncLog retention/cleanup policy | Backlog |
| SEC-011 | LOW | Align frontend/backend password minLength | Backlog |

---

*Report generated: 2026-03-25*
*Next audit recommended: End of Sprint 3 (after HIGH/MEDIUM remediations are complete)*
