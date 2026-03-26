# ION CRM — Security Audit Report

**Auditor:** Senior Security Engineer
**Date:** 2026-03-26
**Scope:** Full codebase — `/root/ems-team/output/src/`, `/root/ems-team/output/frontend/src/`, `/root/ems-team/.github/workflows/`
**Methodology:** Manual code review, static analysis, secrets scan, dependency audit

---

## Executive Summary

| Severity | Count |
|----------|-------|
| 🔴 CRITICAL | 1 |
| 🟠 HIGH     | 3 |
| 🟡 MEDIUM   | 5 |
| 🔵 LOW      | 5 |
| ℹ️ INFO     | 12 |

**1 critical issue was fixed directly in code.** All other issues are documented below for sprint planning.

---

## 🔴 CRITICAL Issues (Fixed)

### CRITICAL-001 — Refresh Token Exposed in HTTP Response Body

| Field | Detail |
|-------|--------|
| **Status** | ✅ FIXED in this audit |
| **File** | `src/IonCrm.API/Controllers/AuthController.cs` |
| **CVSSv3** | 8.8 (AV:N/AC:L/PR:N/UI:R/S:U/C:H/I:H/A:H) |

**Description:**
The `Login` endpoint returned the raw 7-day refresh token inside the JSON response body via `AuthResponseDto.RefreshToken`. Any JavaScript running on the page (e.g., via XSS) could read this value from the network response or from `localStorage` where `authStore.ts` also wrote the access token.

Additionally, the `Logout` endpoint accepted `RefreshToken` and `UserId` from the request body — meaning the client was expected to send sensitive credentials back to the server rather than having them managed server-side.

The `Refresh` endpoint already correctly read the token from a cookie, but `Login` never set that cookie, making token rotation silently broken (auto-refresh always failed after 15 minutes).

**Root Cause:**
`AuthController.Login()` did not call `Response.Cookies.Append(...)`. The `AuthResponseDto` propagated the raw token string all the way to the client JSON body.

**Impact:**
- XSS attack can steal a 7-day refresh token from any login response still in JS context
- Refresh token rotation was completely broken — all sessions expired after 15 minutes (access token TTL), forcing users to re-login constantly
- Users sending raw refresh tokens in logout request bodies unnecessarily exposed them in access logs and proxies

**Fix Applied (code-level):**

```csharp
// AuthController.cs — Login() — set HttpOnly cookie, strip from body
var dto = result.Value!;
SetRefreshTokenCookie(dto.RefreshToken);
dto.RefreshToken = string.Empty;
return Ok(ApiResponse<AuthResponseDto>.Ok(dto));

// AuthController.cs — Refresh() — rotate cookie, strip from body
SetRefreshTokenCookie(dto.RefreshToken);
dto.RefreshToken = string.Empty;

// AuthController.cs — Logout() — reads token from cookie, always clears cookie
var refreshToken = Request.Cookies[RefreshTokenCookieName] ?? string.Empty;
// ...
Response.Cookies.Delete(RefreshTokenCookieName);

// Helper method
private void SetRefreshTokenCookie(string rawToken)
{
    Response.Cookies.Append("refreshToken", rawToken, new CookieOptions
    {
        HttpOnly = true,                 // Not accessible via document.cookie / JS
        Secure   = true,                 // HTTPS only
        SameSite = SameSiteMode.Strict,  // No cross-site request inclusion
        Expires  = DateTimeOffset.UtcNow.AddDays(7)
    });
}
```

**Verification:**
`dotnet build --configuration Release` → `Build succeeded. 0 Warning(s). 0 Error(s)`

---

## 🟠 HIGH Issues (Fix This Sprint)

### HIGH-001 — Access Token Stored in `localStorage` (XSS Risk)

| Field | Detail |
|-------|--------|
| **Status** | 🎫 Jira: ION-SEC-001 |
| **File** | `frontend/src/stores/authStore.ts` lines 34–35 |

**Description:**
`authStore.ts` stores the JWT access token in `localStorage`:

```typescript
// authStore.ts line 34 — VULNERABLE
localStorage.setItem('accessToken', accessToken);
localStorage.setItem('user', JSON.stringify(user));
```

The `client.ts` comment explicitly states *"In-memory token store (NOT localStorage — security requirement)"*, but the Zustand store contradicts this. Any XSS payload can call `localStorage.getItem('accessToken')` to steal the JWT and impersonate the user for up to 15 minutes.

The `initializeAuth()` function (lines 72–98) also restores the token from `localStorage` on page reload, defeating the in-memory-only design.

**Recommendation:**
1. Remove `localStorage.setItem('accessToken', ...)` and `localStorage.setItem('user', ...)`
2. On page reload (`initializeAuth`), call `POST /api/v1/auth/refresh` instead of reading from localStorage — the browser will automatically send the HttpOnly `refreshToken` cookie, obtaining a new access token
3. Keep only non-sensitive display metadata in `sessionStorage` if needed for UX

---

### HIGH-002 — AutoMapper 14.0.0 Known High Severity CVE

| Field | Detail |
|-------|--------|
| **Status** | 🎫 Jira: ION-SEC-002 |
| **File** | `src/IonCrm.Application/IonCrm.Application.csproj` |
| **Advisory** | [GHSA-rvv3-g6hj-g44x](https://github.com/advisories/GHSA-rvv3-g6hj-g44x) |

**Description:**
`dotnet list package --vulnerable` reports:

```
Project IonCrm.Application
  Package: AutoMapper  Version: 14.0.0  Severity: High
  Advisory URL: https://github.com/advisories/GHSA-rvv3-g6hj-g44x
```

**Recommendation:**
Upgrade `AutoMapper` to the latest patched version. Run `dotnet outdated` to identify the safe target version, update the `.csproj`, restore, build, and run tests to confirm no mapping regressions.

---

### HIGH-003 — Webhook API Key Comparison Is Not Constant-Time (Timing Attack)

| Field | Detail |
|-------|--------|
| **Status** | 🎫 Jira: ION-SEC-003 |
| **File** | `src/IonCrm.API/Controllers/SyncController.cs` lines 57, 115 |

**Description:**
Both inbound webhook endpoints authenticate using a plain string comparison:

```csharp
// SyncController.cs line 57 (SaaS A) and line 115 (SaaS B)
if (string.IsNullOrEmpty(apiKey) || apiKey != expectedKey)
    return StatusCode(401, ApiResponse<object>.Fail("Invalid or missing API key.", 401));
```

`string` equality in .NET is not guaranteed to be constant-time. A well-positioned attacker on a low-latency channel could measure response times to enumerate the API key character by character (timing side-channel attack).

**Recommendation:**
Replace with `CryptographicOperations.FixedTimeEquals` (available since .NET 5):

```csharp
using System.Security.Cryptography;

var expected = System.Text.Encoding.UTF8.GetBytes(expectedKey);
var actual   = System.Text.Encoding.UTF8.GetBytes(apiKey ?? string.Empty);

if (!CryptographicOperations.FixedTimeEquals(actual, expected))
    return StatusCode(401, ApiResponse<object>.Fail("Invalid or missing API key.", 401));
```

---

## 🟡 MEDIUM Issues (Next Sprint)

### MEDIUM-001 — Raw Webhook Payload (PII) Stored Unencrypted in SyncLog

| Field | Detail |
|-------|--------|
| **Status** | 🎫 Jira: ION-SEC-004 |
| **Files** | `src/IonCrm.Application/Features/Sync/Commands/ProcessWebhook/ProcessSaasAWebhookCommandHandler.cs:51`<br>`src/IonCrm.Application/Features/Sync/Commands/ProcessWebhook/ProcessSaasBWebhookCommandHandler.cs`<br>`src/IonCrm.Application/Features/Sync/Commands/NotifySaas/NotifySaasCommandHandler.cs:71` |

**Description:**
All three sync handlers store the full raw JSON payload in `SyncLog.Payload`:

```csharp
var log = new SyncLog {
    ...
    Payload = request.RawPayload  // contains PII: emails, phones, tax IDs, addresses
};
```

This applies to both inbound webhooks (customer data pushed by SaaS A/B) and outbound notifications (CRM data sent to SaaS A/B). The data includes:
- Company names, contact names, email addresses, phone numbers, tax IDs, street addresses
- It is stored in plaintext in PostgreSQL
- It is not masked or truncated

**Recommendation:**
1. Strip PII fields from `Payload` before persisting, keeping only structural/metadata fields (event type, entity type, entity ID, status codes)
2. If full payloads are required for debugging, encrypt the `Payload` column at the database level (PostgreSQL `pgcrypto`) or at the application level (AES-256-GCM)
3. Add a TTL job to purge `SyncLog.Payload` values older than 30 days

---

### MEDIUM-002 — `/api/v1/sync/logs` Accessible to All Authenticated Users

| Field | Detail |
|-------|--------|
| **Status** | 🎫 Jira: ION-SEC-005 |
| **File** | `src/IonCrm.API/Controllers/SyncController.cs` line 157 |

**Description:**
The `GET /api/v1/sync/logs` endpoint uses `[Authorize]` (any authenticated user) rather than `[Authorize(Policy = "SuperAdmin")]`:

```csharp
[HttpGet("logs")]
[Authorize]   // ← should be SuperAdmin or at minimum a Manager role
public async Task<IActionResult> GetSyncLogs(...)
```

The handler enforces project-scoped filtering so cross-tenant leakage is prevented, but sync logs contain integration audit data (event types, entity IDs, error messages) that should not be visible to regular CRM users. Combined with MEDIUM-001, the Payload field could expose PII to any authenticated user.

**Recommendation:**
Restrict to `[Authorize(Policy = "SuperAdmin")]` or introduce a `Manager` role check. At minimum, verify permanently that `SyncLogDto` (mapped in the handler) never exposes the `Payload` field — currently true but fragile.

---

### MEDIUM-003 — `GetCustomers` Returns 200 with Empty Results for Foreign Tenant (Weak Oracle)

| Field | Detail |
|-------|--------|
| **Status** | 🎫 Jira: ION-SEC-006 |
| **File** | `src/IonCrm.Application/Customers/Queries/GetCustomers/GetCustomersQueryHandler.cs` |

**Description:**
`GetCustomersQueryHandler` delegates tenant isolation entirely to the EF Core global query filter without an explicit access check at the application layer:

```csharp
// No access check for request.ProjectId
var (items, totalCount) = await _customerRepository.GetPagedAsync(request.ProjectId, ...);
```

If a user supplies an arbitrary `?projectId=<foreign-guid>`, the EF filter silently returns `200 OK { items: [], totalCount: 0 }` instead of `403 Forbidden`. An attacker can use this as an oracle to distinguish "tenant has no customers" from "tenant GUID does not belong to me".

This contrasts with `GetCustomerById`, `DeleteCustomer`, `GetCustomerTaskById`, and `GetAllContactHistories` — all of which perform explicit checks.

**Recommendation:**
```csharp
if (request.ProjectId.HasValue
    && !_currentUser.IsSuperAdmin
    && !_currentUser.ProjectIds.Contains(request.ProjectId.Value))
{
    return Result<PagedResult<CustomerDto>>.Failure("Access denied to the requested project.");
}
```

---

### MEDIUM-004 — No Request Body Size Limits Configured

| Field | Detail |
|-------|--------|
| **Status** | 🎫 Jira: ION-SEC-007 |
| **File** | `src/IonCrm.API/Program.cs` |

**Description:**
No `MaxRequestBodySize` limit is configured. The webhook endpoints accept arbitrarily large `JsonElement rawBody` payloads. An attacker can:
- Send a 50 MB JSON payload to exhaust server memory
- Fill the `SyncLog.Payload` column with garbage data to bloat the database

The rate limiter restricts request *count* (10/min) but not request *size*.

**Recommendation:**
Configure in `Program.cs`:

```csharp
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 1_048_576; // 1 MB global limit
});
```

Apply stricter limits on webhook endpoints with `[RequestSizeLimit(524_288)]` (512 KB).

---

### MEDIUM-005 — MSSQL Connection String Passed in HTTP Request Body

| Field | Detail |
|-------|--------|
| **Status** | 🎫 Jira: ION-SEC-008 |
| **File** | `src/IonCrm.API/Controllers/MigrationController.cs` lines 92–102 |

**Description:**
The migration endpoint accepts a raw MSSQL connection string in the request body:

```csharp
public sealed record RunMigrationRequest(
    Guid   ProjectId,
    string MssqlConnectionString   // contains host, username, password
);
```

Even with application-level care to avoid logging it, this value is at risk from:
- HTTP access log middleware (if request body logging is ever added)
- APM/tracing tools that capture request payloads (e.g., Sentry, OpenTelemetry)
- Intermediate proxies or Railway's internal logging infrastructure

**Recommendation:**
Store the MSSQL connection string as a Railway secret / environment variable (`MigrationSettings__MssqlConnectionString`) and remove it from the API contract. The endpoint then reads from configuration, and the SuperAdmin only sends `{ "projectId": "..." }`.

---

## 🔵 LOW Issues (Backlog)

### LOW-001 — Hardcoded Fallback Credentials in Design-Time Factory

| Field | Detail |
|-------|--------|
| **Status** | 🎫 Jira: ION-SEC-009 |
| **File** | `src/IonCrm.Infrastructure/Persistence/ApplicationDbContextFactory.cs:26` |

**Description:**
```csharp
?? "Host=localhost;Database=ioncrm_dev;Username=postgres;Password=postgres";
```

Design-time only (used by `dotnet ef migrations`), never runs in production. Nevertheless, it flags on automated secret scanners and violates the clean-secrets policy.

**Recommendation:**
```csharp
?? throw new InvalidOperationException(
    "Set ConnectionStrings__DefaultConnection before running EF migrations.");
```

---

### LOW-002 — HSTS Not Configured at Application Level

| Field | Detail |
|-------|--------|
| **Status** | 🎫 Jira: ION-SEC-010 |
| **File** | `src/IonCrm.API/Program.cs:162` |

**Description:**
TLS is terminated by Railway's edge. The app itself emits no `Strict-Transport-Security` header. If the service is ever accessed directly or Railway's edge configuration changes, browsers will not enforce HTTPS.

**Recommendation:**
Add `app.UseHsts()` before `app.UseCors(...)`. Start with a short `max-age` and increase to 1 year after validation.

---

### LOW-003 — No Account Lockout After Repeated Failed Login Attempts

| Field | Detail |
|-------|--------|
| **Status** | 🎫 Jira: ION-SEC-011 |
| **File** | `src/IonCrm.Application/Auth/Commands/Login/LoginCommandHandler.cs` |

**Description:**
IP-based rate limiting (10 req/min) is configured, but there is no per-account lockout. An attacker with a distributed botnet (one attempt per IP per minute × many IPs) can perform credential stuffing or password-spray attacks at scale.

**Recommendation:**
Add `FailedLoginAttempts` (int) and `LockoutUntil` (DateTime?) columns to the `Users` table. Increment on failure; lock account for 15 minutes after 5 consecutive failures. Reset counter on successful login.

---

### LOW-004 — Railway Service IDs Hardcoded in CI/CD Workflow + Preview Uses Production Service

| Field | Detail |
|-------|--------|
| **Status** | 🎫 Jira: ION-SEC-012 |
| **Files** | `.github/workflows/deploy.yml:12-14`, `.github/workflows/preview.yml:90` |

**Description:**
Service UUIDs are hardcoded in workflow `env:` blocks. More critically, `preview.yml` line 90 uses the **production backend service ID** (`987799b6-18b9-4223-81c6-505ffc6717ba`) for preview environment deployments — this is the same ID as `BACKEND_SERVICE_ID` in `deploy.yml`. Preview deployments may be triggering production redeploys.

**Recommendation:**
1. Move service IDs to GitHub repository variables (`vars.BACKEND_SERVICE_ID`, etc.)
2. Create a dedicated Railway preview service and use its ID in `preview.yml`

---

### LOW-005 — Default SuperAdmin Credentials in Project Documentation

| Field | Detail |
|-------|--------|
| **Status** | 🎫 Jira: ION-SEC-013 |
| **File** | `/root/ems-team/CLAUDE.md:58` |

**Description:**
`CLAUDE.md` lists `admin@ems.com / Ems2024!` as SuperAdmin credentials. If these match seeded database credentials, they must be rotated immediately before any production deployment.

**Recommendation:**
1. Rotate `admin@ems.com` password in all deployed environments now
2. Replace with `<set-before-deploy>` placeholder in documentation
3. Verify no migration file contains this password as a seed value

---

## ℹ️ INFO — Items Verified as Secure

The following checklist items were explicitly verified and found to be correctly implemented:

| Item | Status | Evidence |
|------|--------|---------|
| JWT secret not hardcoded | ✅ PASS | `Program.cs:36-40` — throws `InvalidOperationException` at startup if missing |
| JWT access token expiry 15 min | ✅ PASS | `appsettings.json:15` + `TokenService.cs:46` |
| Refresh token expiry 7 days | ✅ PASS | `appsettings.json:16` + `TokenService.cs:97` |
| Refresh tokens hashed in DB | ✅ PASS | `TokenService.cs:102` — SHA-256 hex; raw value never stored |
| Refresh token rotation on use | ✅ PASS | `RefreshTokenCommandHandler.cs:51` — old token revoked before new issued |
| Passwords hashed with BCrypt ≥ cost 12 | ✅ PASS | `PasswordHasher.cs:12` — `WorkFactor = 12` |
| Rate limiting on /auth/login | ✅ PASS | `appsettings.json:59-63` — 10 req/min per IP |
| Rate limiting on /auth/refresh | ✅ PASS | `appsettings.json:64-68` |
| Multi-tenant global query filters | ✅ PASS | `ApplicationDbContext.cs:82-105` — all tenant entities filtered by current user's ProjectIds |
| Tenant check on by-ID lookups | ✅ PASS | `GetCustomerById`, `DeleteCustomer`, `GetCustomerTaskById`, `GetContactHistoryById` all have explicit `ProjectIds.Contains(...)` checks |
| SuperAdmin routes protected | ✅ PASS | `UsersController`, `MigrationController`, `SyncController.TriggerSync` all use `[Authorize(Policy = "SuperAdmin")]` |
| No secrets in appsettings.json | ✅ PASS | All secret fields are empty strings — values come from environment variables at runtime |
| Connection string from env var | ✅ PASS | `DependencyInjection.cs:34-36` — throws if missing |
| Swagger disabled in production | ✅ PASS | `Program.cs:152` — guarded by `!app.Environment.IsProduction()` |
| CORS locked to specific origins | ✅ PASS | `appsettings.json:21-26` — explicit allowlist (localhost + Railway domains) |
| Error messages don't leak stack traces | ✅ PASS | `GlobalExceptionMiddleware.cs:55` — generic "An unexpected error occurred" message |
| SaaS API keys from environment | ✅ PASS | `DependencyInjection.cs:130,154` — read from `IConfiguration`, never hardcoded |
| Webhook API key from environment | ✅ PASS | `SyncController.cs:56,114` — `_configuration["SaasA:WebhookApiKey"]` |
| SQL injection not possible | ✅ PASS | EF Core parameterized queries throughout; Dapper uses `@param` syntax in migration service |
| Soft delete not leaking data | ✅ PASS | Global query filter includes `!e.IsDeleted` on all entities |
| Hangfire dashboard protected | ✅ PASS | `HangfireAdminAuthFilter.cs` — JWT `isSuperAdmin` claim required in non-dev environments |
| No passwords/tokens in logs | ✅ PASS | `LoginCommandHandler`, `TokenService`, `DataMigrationService` explicitly avoid logging credentials |
| CI/CD secrets managed securely | ✅ PASS | All sensitive CI values use `${{ secrets.* }}` — never hardcoded in workflow files |

---

## Dependency Audit

```bash
dotnet list package --vulnerable --include-transitive
```

| Project | Package | Version | Severity | Advisory |
|---------|---------|---------|----------|---------|
| IonCrm.Application | AutoMapper | 14.0.0 | **High** | [GHSA-rvv3-g6hj-g44x](https://github.com/advisories/GHSA-rvv3-g6hj-g44x) |
| IonCrm.Domain | — | — | Clean | — |
| IonCrm.Infrastructure | — | — | Clean | — |
| IonCrm.API | — | — | Clean | — |
| IonCrm.Tests | — | — | Clean | — |

**Action required:** Upgrade `AutoMapper` → HIGH-002 / ION-SEC-002.

---

## Secrets Scan Summary

| Pattern | Files scanned | Result |
|---------|--------------|--------|
| `password\s*=\s*"[^"]+"` | `*.cs` | ⚠️ 1 hit — `ApplicationDbContextFactory.cs:26` (design-time fallback) |
| `secret\s*=\s*"[^"]+"` | `*.cs` | ✅ Clean |
| `apikey\s*=\s*"[^"]+"` | `*.cs` | ✅ Clean |
| `AKIA[0-9A-Z]{16}` | `*.cs, *.ts, *.json` | ✅ Clean |

---

## Summary

**1 critical, 3 high, 5 medium, 5 low issues found.**

| Severity | Count | Issues |
|----------|-------|--------|
| 🔴 CRITICAL | 1 | CRITICAL-001: Refresh token in response body — **FIXED in this audit** |
| 🟠 HIGH | 3 | HIGH-001: Token in localStorage · HIGH-002: AutoMapper CVE · HIGH-003: Webhook timing attack |
| 🟡 MEDIUM | 5 | MEDIUM-001: PII in SyncLog · MEDIUM-002: Sync log access control · MEDIUM-003: Silent tenant oracle · MEDIUM-004: No request size limit · MEDIUM-005: Connection string in request body |
| 🔵 LOW | 5 | LOW-001: Hardcoded dev password · LOW-002: No HSTS · LOW-003: No account lockout · LOW-004: Service IDs in CI + preview→production overlap · LOW-005: Default credentials in docs |

*Report generated 2026-03-26. CRITICAL fix is deployed and build-verified. HIGH issues must be resolved in the current sprint.*
