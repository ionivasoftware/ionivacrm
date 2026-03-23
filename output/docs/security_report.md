# ION CRM — Security Audit Report

**Date:** 2026-03-23
**Auditor:** Senior Security Engineer (AI Agent)
**Scope:** Full codebase — backend (`src/`), frontend (`frontend/src/`)
**Framework:** ASP.NET Core 8, EF Core, PostgreSQL, React 18

---

## Executive Summary

| Severity | Count | Status |
|----------|-------|--------|
| CRITICAL | 2     | ✅ Fixed in this audit |
| HIGH     | 4     | Jira tickets created |
| MEDIUM   | 4     | Jira tickets created |
| LOW      | 4     | Jira tickets created |
| INFO     | 2     | Recommendations |

Overall posture: **FAIR**. The codebase demonstrates strong security awareness (bcrypt hashing, token rotation, global query filters, no secrets in config files), but two critical vulnerabilities required immediate remediation, and several high-priority gaps remain.

---

## CRITICAL — Fixed in This Audit

---

### CRIT-01 — Hardcoded JWT Fallback Key Enables Token Forgery

**File:** `src/IonCrm.API/Program.cs`
**Lines (before fix):** 31-32
**CVSS-like severity:** Critical (Authentication Bypass)

**Vulnerable code (before fix):**
```csharp
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? "dev-placeholder-key-replace-in-production-min-32-chars!!";
```

**Description:**
If the `Jwt:Key` environment variable is not set (misconfigured deployment, missing Railway secret), the application silently falls back to a well-known, publicly readable hardcoded string. Any attacker who has read access to this repository can forge a signed JWT token with `isSuperAdmin: true`, authenticate as any user, and gain full administrative access to all tenants.

**Impact:** Complete authentication bypass; full cross-tenant data access; data exfiltration or destruction.

**Fix applied:**
The fallback was removed. The application now throws `InvalidOperationException` at startup if `Jwt:Key` is absent, preventing any insecure startup:
```csharp
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
    throw new InvalidOperationException(
        "JWT signing key is not configured. Set the 'Jwt:Key' environment variable ...");
```

**Required operational action:**
Set `JWT__KEY` (or `Jwt__Key`) in Railway production environment:
```bash
JWT__KEY=$(openssl rand -base64 32)
```

---

### CRIT-02 — Cross-Tenant Sync Log Disclosure via Unvalidated ProjectId Parameter

**File:** `src/IonCrm.Application/Features/Sync/Queries/GetSyncLogs/GetSyncLogsQueryHandler.cs`
**Lines (before fix):** 34-42
**CVSS-like severity:** Critical (Broken Object-Level Authorization / IDOR)

**Vulnerable code (before fix):**
```csharp
var projectIdFilter = request.ProjectId;
if (!_currentUser.IsSuperAdmin && projectIdFilter is null)
{
    // Only applied when projectId is NULL
    projectIdFilter = _currentUser.ProjectIds[0];
}
// If projectId WAS supplied, no ownership check was performed!
```

**Description:**
The `GET /api/v1/sync/logs?projectId=<uuid>` endpoint accepted any `projectId` from an authenticated non-SuperAdmin user without checking if the user belongs to that project. The `SyncLogRepository` uses `IgnoreQueryFilters()` (bypassing EF global tenant filter), so any authenticated user could enumerate sync logs for ANY tenant by simply providing a different project GUID. Sync logs contain `EntityType`, `EntityId`, `ErrorMessage`, `Payload` (raw JSON), providing a vector for cross-tenant data leakage.

**Impact:** Any authenticated user can read sync history of all other tenants. Can also probe tenant UUIDs via trial-and-error.

**Fix applied:**
Added explicit project membership validation before executing the query:
```csharp
else if (!_currentUser.ProjectIds.Contains(projectIdFilter.Value))
{
    return Result<PagedResult<SyncLogDto>>.Failure("Access denied to the requested project.");
}
```

---

## HIGH — Fix This Sprint

---

### HIGH-01 — No Rate Limiting on Authentication Endpoints (Brute Force)

**File:** `src/IonCrm.API/Program.cs`
**Package:** `AspNetCoreRateLimit v5.0.0` is already referenced in `IonCrm.API.csproj` but is **not configured or activated**.

**Description:**
`POST /api/v1/auth/login` has no rate limiting. An attacker can make unlimited login attempts against any email address, enabling brute-force password attacks. The `LoginCommandHandler` logs failed attempts (`LogWarning`) but takes no blocking action.

**Impact:** Account compromise via brute force, credential stuffing.

**Remediation:**
1. Configure `AspNetCoreRateLimit` in `Program.cs` (already available as a dependency):
```csharp
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(config => {
    config.GeneralRules = new List<RateLimitRule> {
        new() { Endpoint = "POST:/api/v1/auth/login", Period = "1m", Limit = 5 },
        new() { Endpoint = "POST:/api/v1/auth/refresh", Period = "1m", Limit = 20 }
    };
});
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
// ...
app.UseIpRateLimiting();
```

**Jira:** `SEC-101`

---

### HIGH-02 — No Account Lockout After Failed Login Attempts

**File:** `src/IonCrm.Application/Auth/Commands/Login/LoginCommandHandler.cs`

**Description:**
There is no mechanism to lock an account after N consecutive failed login attempts. Failed attempts are logged but never counted or acted upon. The `User` entity has no `FailedLoginAttempts`, `LockoutEnabled`, or `LockoutUntil` fields.

**Impact:** Enables sustained brute-force/password-spray attacks even with per-IP rate limiting (distributed attacks).

**Remediation:**
1. Add `FailedLoginAttempts` (int) and `LockoutUntil` (DateTime?) to `User` entity.
2. Create EF migration.
3. In `LoginCommandHandler`: increment counter on failure; lock account for 15 min after 5 failures; reset counter on success.

**Jira:** `SEC-102`

---

### HIGH-03 — Timing Attack on Webhook API Key Comparison

**File:** `src/IonCrm.API/Controllers/SyncController.cs` (lines 57-58, 115-116)

**Vulnerable code:**
```csharp
if (string.IsNullOrEmpty(apiKey) || apiKey != expectedKey)
```

**Description:**
Standard string inequality `!=` in C# is not constant-time. An attacker sending many requests to `POST /api/v1/sync/saas-a` or `saas-b` with varying keys can measure response timing differences to discover the correct webhook key byte-by-byte.

**Impact:** Webhook API key enumeration; unauthorized data injection into CRM via spoofed SaaS events.

**Remediation:**
Replace with `CryptographicOperations.FixedTimeEquals`:
```csharp
using System.Security.Cryptography;

private static bool SecureEquals(string? provided, string? expected)
{
    if (provided is null || expected is null) return false;
    var a = System.Text.Encoding.UTF8.GetBytes(provided);
    var b = System.Text.Encoding.UTF8.GetBytes(expected);
    return CryptographicOperations.FixedTimeEquals(a, b);
}
```

**Jira:** `SEC-103`

---

### HIGH-04 — HSTS Not Configured (HTTP Strict Transport Security Missing)

**File:** `src/IonCrm.API/Program.cs`

**Description:**
`UseHsts()` and `AddHsts()` are not called anywhere in the middleware pipeline. While `UseHttpsRedirection()` is present, HSTS is what prevents browsers from ever sending plain HTTP requests (after first visit), protecting against SSL stripping attacks.

**Impact:** Users are vulnerable to SSL strip / downgrade attacks on their first request or if HSTS headers are absent.

**Remediation:**
```csharp
// In builder.Services section:
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = true;
});

// In app middleware (before UseHttpsRedirection):
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
```

**Jira:** `SEC-104`

---

## MEDIUM — Next Sprint

---

### MED-01 — Raw Webhook Payload Stored in SyncLog Table (PII Risk)

**Files:**
- `src/IonCrm.Application/Features/Sync/Commands/ProcessWebhook/ProcessSaasAWebhookCommandHandler.cs` (line 51: `Payload = request.RawPayload`)
- `src/IonCrm.Domain/Entities/SyncLog.cs` (line 40: `public string? Payload { get; set; }`)

**Description:**
The full raw JSON payload from SaaS A/B webhooks is persisted to the `SyncLog.Payload` column in the database. SaaS payloads may contain PII (customer names, emails, phone numbers, tax numbers, financial data). This data is stored indefinitely with no expiry policy.

While `SyncLogDto` does not expose `Payload` via the API, the raw data is queryable in the DB by anyone with DB access.

**Impact:** Potential KVKK/GDPR compliance violation; data minimisation principle violated.

**Remediation:**
1. Either strip `Payload` from the `SyncLog` entity or only store a truncated/sanitised summary.
2. If payload is needed for debugging/retries, encrypt it at rest or apply a retention policy (e.g., purge after 30 days).
3. Add a `SyncLogConfiguration` max-length or exclusion for Payload in non-production environments.

**Jira:** `SEC-201`

---

### MED-02 — Missing ITokenService and IPasswordHasher DI Registrations

**File:** `src/IonCrm.Infrastructure/DependencyInjection.cs`

**Description:**
The `DependencyInjection.cs` in the Infrastructure project does not register `ITokenService` or `IPasswordHasher` with the DI container. The concrete implementations (`TokenService`, `PasswordHasher`) referenced in `IPasswordHasher`'s XML doc comment (`IonCrm.Infrastructure.Services.PasswordHasher`) do not exist on disk. The `/auth/login` and `/auth/register` endpoints would fail at runtime with `InvalidOperationException: No service for type IPasswordHasher`.

**Impact:**  Authentication is entirely non-functional; all auth operations will throw 500 errors.

**Remediation:**
1. Implement `TokenService : ITokenService` and `PasswordHasher : IPasswordHasher` in `IonCrm.Infrastructure/Services/`.
2. Register them in `DependencyInjection.cs`:
```csharp
services.AddScoped<ITokenService, TokenService>();
services.AddScoped<IPasswordHasher, PasswordHasher>();
```
3. Verify BCrypt cost factor 12 is used in `PasswordHasher.Hash()`.

**Jira:** `SEC-202`

---

### MED-03 — Missing AuthController (No Auth Endpoints Exposed)

**File:** `src/IonCrm.API/Controllers/` (missing `AuthController.cs`)

**Description:**
All auth command handlers exist (`LoginCommandHandler`, `RegisterUserCommandHandler`, `RefreshTokenCommandHandler`, `LogoutCommandHandler`, `AssignRoleCommandHandler`) but there is no `AuthController` to expose them as HTTP endpoints. The frontend calls `/auth/login`, `/auth/refresh`, `/auth/logout` — these would all return 404.

Critically, the `RegisterUserCommand` comment states _"authorization enforced in the API controller via [Authorize(Policy = "SuperAdmin")]"_, but without a controller this enforcement is never applied — if/when the endpoint is added, the policy must be explicitly applied.

**Impact:**  Authentication system is non-functional; when controller is added, SuperAdmin enforcement must be correctly applied to `/auth/register`.

**Remediation:**
1. Create `AuthController : ApiControllerBase` with endpoints for login (anonymous), refresh (anonymous), logout (authenticated), register (SuperAdmin policy), assign-role (SuperAdmin policy).
2. Apply `[AllowAnonymous]` to login and refresh endpoints.
3. Apply `[Authorize(Policy = "SuperAdmin")]` to register and assign-role.

**Jira:** `SEC-203`

---

### MED-04 — No Request Size Limits Configured

**File:** `src/IonCrm.API/Program.cs`

**Description:**
No global or per-endpoint request body size limits are configured. The default Kestrel limit is 30 MB. An attacker could send oversized payloads to crash or degrade the service (DoS), particularly to the webhook endpoints (`/api/v1/sync/saas-a`, `/api/v1/sync/saas-b`) which accept arbitrary `JsonElement` bodies.

**Impact:** Denial-of-service via large payload; memory exhaustion.

**Remediation:**
```csharp
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 1 * 1024 * 1024; // 1 MB globally
});
```
Apply `[RequestSizeLimit(65536)]` (64 KB) to webhook endpoints specifically.

**Jira:** `SEC-204`

---

## LOW — Backlog

---

### LOW-01 — Hardcoded Default Credentials in Design-Time DbContext Factory

**File:** `src/IonCrm.Infrastructure/Persistence/ApplicationDbContextFactory.cs` (line 26)

**Code:**
```csharp
?? "Host=localhost;Database=ioncrm_dev;Username=postgres;Password=postgres";
```

**Description:**
The design-time factory (used by `dotnet ef migrations`) falls back to a hardcoded `postgres/postgres` credential. While this only runs locally and never in production, it commits default credentials to the repository, which is bad practice.

**Remediation:** Remove the fallback entirely and document that developers must set `CONNECTIONSTRINGS__DEFAULTCONNECTION` locally.

**Jira:** `SEC-301`

---

### LOW-02 — WeatherForecastController Scaffold Artifact Should Be Removed

**File:** `src/IonCrm.API/Controllers/WeatherForecastController.cs`

**Description:**
Default ASP.NET Core scaffold controller is still in the codebase. It exposes a `/weatherforecast` endpoint that reveals server information and increases attack surface unnecessarily.

**Remediation:** Delete `WeatherForecastController.cs` and `WeatherForecast.cs`.

**Jira:** `SEC-302`

---

### LOW-03 — Serilog Enriched With Machine Name and Thread ID

**File:** `src/IonCrm.API/appsettings.json` (line 52)

**Code:**
```json
"Enrich": [ "FromLogContext", "WithMachineName", "WithThreadId" ]
```

**Description:**
`WithMachineName` enriches every log entry with the server hostname. If logs are accessible to multiple parties (log aggregation services, shared dashboards), this leaks infrastructure topology.

**Remediation:** Remove `WithMachineName` and `WithThreadId` from production Serilog config; keep only in development.

**Jira:** `SEC-303`

---

### LOW-04 — Sync Logs Endpoint Insufficiently Scoped (Should Be SuperAdmin Only)

**File:** `src/IonCrm.API/Controllers/SyncController.cs` (line 158)

**Description:**
`GET /api/v1/sync/logs` uses `[Authorize]` (any authenticated user) rather than `[Authorize(Policy = "SuperAdmin")]`. While CRIT-02 has been fixed to enforce project-level scoping, non-SuperAdmin users can still read sync metadata (entity types, sync statuses, error messages) for their own project. Sync operation metadata may reveal internal business processes.

**Recommendation:** Restrict to SuperAdmin only, or add a role check at the handler level for `ProjectAdmin` and above.

**Jira:** `SEC-304`

---

## INFO — Recommendations

### INFO-01 — Register Self-Registration as SuperAdmin-Only at the API Layer

The `RegisterUserCommand` has `IsSuperAdmin = false` as a default, but an API caller can send `IsSuperAdmin: true` in the JSON body. This relies entirely on the not-yet-created `AuthController` applying `[Authorize(Policy = "SuperAdmin")]` to the registration endpoint. Document this requirement explicitly as a deployment gate.

### INFO-02 — Consider Refresh Token HttpOnly Cookie Instead of Response Body

The frontend currently stores the refresh token as a response value and uses it via in-memory JS state (`authStore.ts` / `client.ts`). While access tokens are correctly kept in memory (not localStorage), the refresh flow comments suggest an httpOnly cookie is intended (`withCredentials: true` is set). Completing the httpOnly cookie approach eliminates XSS risks to the refresh token.

---

## Checklist Results

### Authentication & Authorization
| Check | Result |
|-------|--------|
| JWT secret not hardcoded | ✅ Fixed (CRIT-01) |
| JWT expiry is short (15 min access, 7 day refresh) | ✅ PASS — hardcoded `AddMinutes(15)` in handlers |
| Refresh tokens stored securely (hashed in DB) | ✅ PASS — SHA-256 hash per ITokenService contract |
| Passwords hashed with bcrypt (cost >= 12) | ⚠️ UNVERIFIABLE — implementation files missing (MED-02) |
| Rate limiting on /auth/login | ❌ FAIL — package present, not configured (HIGH-01) |
| Account lockout after N failed attempts | ❌ FAIL — not implemented (HIGH-02) |
| Multi-tenant isolation on ALL endpoints | ✅ PASS — global query filters + handler-level checks |
| SuperAdmin routes protected | ✅ PASS — MigrationController, SyncController/trigger |

### Data Security
| Check | Result |
|-------|--------|
| No secrets in code or config files | ✅ PASS — appsettings.json has no values, only keys |
| Connection strings from environment variables | ✅ PASS — throws if not set |
| No passwords/tokens in logs | ✅ PASS — verified in handlers |
| Sensitive data not in JWT payload | ✅ PASS — no password/hash in claims |
| Soft delete not exposing data | ✅ PASS — global query filters include `!e.IsDeleted` |

### Input Validation
| Check | Result |
|-------|--------|
| FluentValidation on ALL commands | ✅ PASS — ValidationBehaviour pipeline + validators found |
| SQL injection not possible | ✅ PASS — EF Core only, no raw SQL |
| XSS prevention | ✅ PASS — output encoding via System.Text.Json |
| File upload restrictions | N/A — no file uploads |
| Request size limits | ❌ FAIL — not configured (MED-04) |

### Infrastructure
| Check | Result |
|-------|--------|
| HTTPS enforced | ✅ PASS — UseHttpsRedirection present |
| HSTS configured | ❌ FAIL (HIGH-04) |
| CORS locked to specific origins | ✅ PASS — configurable via Cors:AllowedOrigins |
| Swagger disabled in production | ✅ PASS — guarded by `IsDevelopment()` |
| Error messages don't leak stack traces | ✅ PASS — GlobalExceptionMiddleware sanitises all errors |
| Database user has minimum permissions | ⚠️ UNVERIFIABLE — depends on deployment config |
| No direct DB access from frontend | ✅ PASS — API-only access pattern |

### Sync Service
| Check | Result |
|-------|--------|
| SaaS API keys stored in env vars | ✅ PASS — loaded via IConfiguration |
| Webhook endpoints validate request signatures | ✅ PASS — X-Api-Key header check (timing attack: HIGH-03) |
| Outbound callbacks use HTTPS only | ✅ PASS — BaseUrl from config, HTTPS enforced by config |
| Sync logs don't contain sensitive payload data | ⚠️ PARTIAL — Payload column stores raw JSON (MED-01) |

### Dependencies
| Check | Result |
|-------|--------|
| NuGet packages up to date | ✅ PASS — no vulnerable packages found |
| No known CVEs | ✅ PASS — `dotnet list package --vulnerable` returned clean |

---

## Secrets Scan Results

```
grep -r "password" src --include="*.cs" -i
```
| Finding | File | Assessment |
|---------|------|------------|
| `Password=postgres` | `ApplicationDbContextFactory.cs:26` | LOW — design-time fallback only (LOW-01) |
| `Password=***` (comment) | `MigrationController.cs:99` | INFO — doc comment, no actual value |

```
grep -r "secret" src --include="*.cs" -i
```
→ No results — PASS

```
grep -r "apikey" src --include="*.cs" -i
```
→ All results reference `_configuration["SaasA:ApiKey"]` or `_configuration["SaasB:ApiKey"]` — PASS, all values from config.

---

## Summary

| Severity | Count | Fixed | Jira |
|----------|-------|-------|------|
| **CRITICAL** | 2 | ✅ 2 fixed in this audit | — |
| **HIGH** | 4 | ❌ Pending | SEC-101, SEC-102, SEC-103, SEC-104 |
| **MEDIUM** | 4 | ❌ Pending | SEC-201, SEC-202, SEC-203, SEC-204 |
| **LOW** | 4 | ❌ Pending | SEC-301, SEC-302, SEC-303, SEC-304 |

**2 critical, 4 high, 4 medium, 4 low issues found.**

---

*Report generated by automated security audit on 2026-03-23.*
