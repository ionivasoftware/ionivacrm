# ION CRM — Security Audit Report

**Auditor:** Senior Security Engineer
**Audit Date:** 2026-03-25
**Scope:** Full codebase — `src/`, `frontend/src/`
**Tech Stack:** ASP.NET Core 8 · React 18 · PostgreSQL (Neon) · JWT · Railway

---

## Executive Summary

| Severity | Count | Status |
|----------|-------|--------|
| 🔴 CRITICAL | 2 | **FIXED** in this audit |
| 🟠 HIGH | 4 | Jira tickets created |
| 🟡 MEDIUM | 4 | Jira tickets created |
| 🔵 LOW | 3 | Jira tickets created |
| ℹ️ INFO | 3 | Recommendations |

---

## Security Checklist Results

### Authentication & Authorization
| Check | Result | Notes |
|-------|--------|-------|
| JWT secret not hardcoded | ❌ FIXED | Was in `appsettings.Development.json` — cleared (CRITICAL-001) |
| JWT expiry short (15 min access, 7 day refresh) | ✅ PASS | Correctly configured in `TokenService` |
| Refresh tokens hashed in DB (SHA-256) | ✅ PASS | `TokenService.HashToken()` uses `SHA256.HashData` |
| Refresh token rotation (one-time-use) | ✅ PASS | Old token revoked on each use in `RefreshTokenCommandHandler` |
| Passwords hashed with bcrypt cost >= 12 | ✅ PASS | `PasswordHasher` uses `WorkFactor = 12` |
| Rate limiting on `/auth/login` | ✅ PASS | 10 req/min per IP via `AspNetCoreRateLimit` |
| Account lockout after N failed attempts | ❌ MISSING | No lockout mechanism — HIGH-001 |
| Multi-tenant isolation enforced on ALL endpoints | ✅ PASS | EF Core global query filters + per-handler ProjectId checks |
| SuperAdmin routes protected | ✅ PASS | `[Authorize(Policy = "SuperAdmin")]` on all elevated routes |

### Data Security
| Check | Result | Notes |
|-------|--------|-------|
| No secrets in code or config files | ❌ FIXED | DB password + JWT secret were in `appsettings.Development.json` (CRITICAL-001) |
| Connection strings from environment variables | ✅ PASS | `DependencyInjection` throws `InvalidOperationException` if missing |
| No passwords/tokens in logs | ✅ PASS | Handlers explicitly avoid logging credentials; `LoginCommandHandler` logs only userId |
| Sensitive data not in JWT payload | ✅ PASS | Payload contains only userId, email, projectIds, roles (no passwords) |
| Soft delete (IsDeleted) not exposing data | ✅ PASS | EF global query filter excludes `IsDeleted=true` records universally |

### Input Validation
| Check | Result | Notes |
|-------|--------|-------|
| FluentValidation on ALL commands | ✅ PASS | Login, Register, CreateCustomer, UpdateCustomer, CreateTask, UpdateTaskStatus, CreateContactHistory, RunMigration all have validators |
| SQL injection not possible | ✅ PASS | EF Core parameterized queries throughout; no raw SQL |
| XSS prevention | ✅ PASS | ASP.NET Core JSON serializer encodes output; no raw HTML rendering |
| File upload restrictions | N/A | No file uploads implemented |
| Request size limits configured | ⚠️ NOT SET | Default ASP.NET limits (~30 MB) apply — LOW-001 |

### Infrastructure
| Check | Result | Notes |
|-------|--------|-------|
| HTTPS enforced (HSTS) | ⚠️ BY DESIGN | Railway terminates TLS at the edge; documented in `Program.cs` |
| CORS locked to specific origins | ✅ PASS | `Cors:AllowedOrigins` whitelist configured in `appsettings.json` |
| Swagger disabled in production | ❌ FIXED | Was unconditionally enabled; now gated behind `!IsProduction()` (CRITICAL-002) |
| Error messages don't leak stack traces | ✅ PASS | `GlobalExceptionMiddleware` returns generic message on 500 |
| Database user has minimum permissions | ⚠️ CONCERN | App connects as `neondb_owner` (DB owner) — MEDIUM-001 |
| No direct DB access from frontend | ✅ PASS | All DB access goes through API layer |

### Sync Service
| Check | Result | Notes |
|-------|--------|-------|
| SaaS API keys stored in env vars | ✅ PASS | Read from `configuration["SaasA:ApiKey"]` / `configuration["SaasB:ApiKey"]` |
| Webhook endpoints validate request signatures | ⚠️ PARTIAL | API key checked but via plain `!=` comparison — HIGH-002 (timing attack) |
| Outbound callbacks use HTTPS only | ⚠️ ASSUMED | `BaseUrl` from config; no HTTPS-only enforcement in code |
| Sync logs don't contain sensitive payload data | ❌ CONCERN | Full raw JSON payload stored in `SyncLog.Payload` — HIGH-003 |

### Dependencies
| Check | Result | Notes |
|-------|--------|-------|
| NuGet packages up to date | ❌ CVE FOUND | AutoMapper 14.0.0 — GHSA-rvv3-g6hj-g44x (High severity) — HIGH-004 |
| dotnet-outdated check | ⚠️ RUN MANUALLY | `dotnet list package --vulnerable` confirmed one High CVE |

---

## CRITICAL Issues (Fixed in this audit)

### CRITICAL-001 — Hardcoded Credentials Committed to Version Control

**File:** `src/IonCrm.API/appsettings.Development.json`
**Status:** FIXED — Credentials wiped, `.gitignore` entry added.

**What was exposed:**
```
Database:  Host=db.ggygdevvkxycrirymiyh.supabase.co; Password=rFsD0rSMuprRfx4e
JWT Secret: fYxPg1WpWyPCjuBWlNB1gif30yS3dl9S//IfGhs/D+Q=
```

**Impact:**
- Anyone with repository access could directly connect to the Supabase dev database and read, modify, or delete all CRM data (639 customers, 892 contact history records).
- The exposed JWT signing secret allows an attacker to forge arbitrary JWT tokens for any user including SuperAdmin — full platform takeover without needing credentials.

**Immediate Actions Required:**
1. **Rotate the Supabase dev database password immediately** — treat as fully compromised.
2. **Rotate the JWT signing secret for all environments** — all existing tokens are untrustworthy.
3. **Audit git history** to determine exposure window:
   ```bash
   git log --all --oneline -- "*/appsettings.Development.json"
   ```
4. Use **.NET User Secrets** for local development going forward:
   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=...;Password=<new>"
   dotnet user-secrets set "JwtSettings:Secret" "$(openssl rand -base64 32)"
   ```
5. Evaluate `git filter-repo` to rewrite history and purge credentials if repo is externally accessible.

---

### CRITICAL-002 — Swagger UI Enabled in Production

**File:** `src/IonCrm.API/Program.cs`
**Status:** FIXED — `UseSwagger()` / `UseSwaggerUI()` now gated behind `!app.Environment.IsProduction()`.

**Before (vulnerable):**
```csharp
// ── Swagger UI (all environments) ──────────────────
app.UseSwagger();
app.UseSwaggerUI(c => { c.SwaggerEndpoint(...); });
```

**After (fixed):**
```csharp
if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => { c.SwaggerEndpoint(...); });
}
```

**Impact of original issue:**
The full OpenAPI schema (`/swagger/v1/swagger.json`) was publicly accessible on the production API at `https://ion-crm-api-production.up.railway.app/swagger`. This exposed all endpoint paths, HTTP methods, request/response shapes, and authentication requirements — acting as an attacker reconnaissance guide.

---

## HIGH Issues (Jira tickets created)

### HIGH-001 — No Account Lockout After Failed Login Attempts
**Jira:** ION-SEC-001
**File:** `src/IonCrm.Application/Auth/Commands/Login/LoginCommandHandler.cs`

Rate limiting allows 10 requests/minute per IP. An attacker with multiple IP addresses (e.g., via VPN, residential proxy, or botnet) can attempt unlimited passwords against any account indefinitely. There is no account-level lockout mechanism.

**Recommendation:**
- Add `FailedLoginAttempts` (int) and `LockoutUntil` (DateTime?) to the `User` entity.
- Lock account for 15–30 minutes after 5 consecutive failures.
- Reset counter on successful login.
- Always return generic error "Invalid email or password" — never reveal lockout status.

---

### HIGH-002 — Webhook API Key Vulnerable to Timing Attack
**Jira:** ION-SEC-002
**File:** `src/IonCrm.API/Controllers/SyncController.cs` (lines 57, 115)

```csharp
// VULNERABLE: plain string equality exits at first mismatch
if (string.IsNullOrEmpty(apiKey) || apiKey != expectedKey)
```

Plain `!=` string comparison has variable execution time based on how many characters match, creating a timing oracle. A sophisticated attacker can statistically recover the webhook API key character by character.

**Recommendation:**
```csharp
using System.Security.Cryptography;

// Constant-time byte comparison — prevents timing oracle
if (!CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(apiKey ?? ""),
        Encoding.UTF8.GetBytes(expectedKey ?? "")))
    return StatusCode(401, ApiResponse<object>.Fail("Invalid or missing API key.", 401));
```

---

### HIGH-003 — Full Raw Webhook Payloads Stored in SyncLog.Payload
**Jira:** ION-SEC-003
**Files:**
- `src/IonCrm.Application/Features/Sync/Commands/ProcessWebhook/ProcessSaasAWebhookCommandHandler.cs` (line 51)
- `src/IonCrm.Application/Features/Sync/Commands/ProcessWebhook/ProcessSaasBWebhookCommandHandler.cs`

```csharp
var log = new SyncLog { ..., Payload = request.RawPayload };
```

Webhook payloads from SaaS A and SaaS B contain PII (names, emails, phone numbers, tax IDs, addresses). Persisting raw payloads to the database expands the blast radius of a breach and may violate GDPR/KVKK data minimisation requirements.

Note: `SyncLogDto` correctly omits the `Payload` field from API responses — the risk is at the DB persistence layer only.

**Recommendation:**
- Strip or pseudonymise PII before storing the payload.
- Or store only metadata: `{ "eventType": "...", "entityType": "...", "entityId": "...", "recordedAt": "..." }`.
- If full payloads are needed for debugging, encrypt the column at rest and restrict read access.

---

### HIGH-004 — AutoMapper CVE (GHSA-rvv3-g6hj-g44x)
**Jira:** ION-SEC-004
**File:** `src/IonCrm.Application/IonCrm.Application.csproj`
**Advisory:** https://github.com/advisories/GHSA-rvv3-g6hj-g44x (High severity)

`AutoMapper 14.0.0` has a confirmed high-severity vulnerability.

**Recommendation:**
```bash
cd src/IonCrm.Application
dotnet add package AutoMapper
dotnet list package --vulnerable  # Verify no remaining CVEs
```

---

## MEDIUM Issues (Jira tickets created)

### MEDIUM-001 — Application Connects as Database Owner (`neondb_owner`)
**Jira:** ION-SEC-005
**Config:** CLAUDE.md (Neon DB — dev + prod)

The application connects as `neondb_owner` — the database owner — in both environments. A successful SQL injection (unlikely with EF Core but not impossible via raw queries or future code) would give an attacker full DDL rights: DROP TABLE, CREATE FUNCTION (code execution), COPY TO/FROM (file system access on some setups).

**Recommendation:**
Create a least-privilege application role:
```sql
CREATE ROLE ioncrm_app LOGIN PASSWORD '<strong-password>';
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO ioncrm_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO ioncrm_app;
```
Use `neondb_owner` credentials only in CI/CD migration steps, not in the running application.

---

### MEDIUM-002 — Refresh Token Flow Broken/Incomplete
**Jira:** ION-SEC-006
**Files:** `frontend/src/stores/authStore.ts`, `frontend/src/api/client.ts`

The backend requires `{ "token": "<refresh_token>" }` in the `POST /auth/refresh` body. However:

1. `authStore.ts` discards the `refreshToken` from the login response (only `accessToken` + `user` extracted).
2. `initializeAuth()` posts to `/auth/refresh` with an empty body — guaranteed to fail validation.
3. `client.ts` comments say "httpOnly cookie" but the backend never sets a `Set-Cookie` header.

This means the intended refresh flow is architecturally incomplete: the frontend cannot rotate access tokens, causing users to be force-logged out every 15 minutes.

**Recommendation (preferred — Option A):**
Implement server-side httpOnly cookie for the refresh token:
- On `POST /auth/login` and `POST /auth/refresh` responses, set:
  ```
  Set-Cookie: refresh_token=<raw_token>; HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth/refresh; Max-Age=604800
  ```
- Modify `RefreshTokenCommand` to read from cookie instead of body.
- Frontend already has `withCredentials: true` — no client-side changes needed.

---

### MEDIUM-003 — Rate Limiter Uses In-Memory Store (Not Distributed)
**Jira:** ION-SEC-007
**File:** `src/IonCrm.API/Program.cs` (line 78)

`AddInMemoryRateLimiting()` stores counters per-process. With multiple Railway API instances, each process maintains an independent counter. An attacker can bypass the 10/minute limit by distributing requests across instances.

**Recommendation:**
Replace with a distributed store (Redis):
```csharp
// Replace:
builder.Services.AddInMemoryRateLimiting();
// With:
builder.Services.AddRedisRateLimiting();
builder.Services.Configure<IpRateLimitOptions>(o => o.EnableEndpointRateLimiting = true);
```

---

### MEDIUM-004 — `customerId` URL Parameter Not Validated Against Record Ownership
**Jira:** ION-SEC-008
**Files:**
- `src/IonCrm.API/Controllers/ContactHistoriesController.cs`
- `src/IonCrm.API/Controllers/CustomerTasksController.cs`
- `src/IonCrm.Application/ContactHistory/Queries/GetContactHistoryById/GetContactHistoryByIdQueryHandler.cs`
- `src/IonCrm.Application/Tasks/Queries/GetCustomerTaskById/GetCustomerTaskByIdQueryHandler.cs`

Routes like `GET /api/v1/customers/{customerId}/contact-histories/{id}` accept `customerId` in the path, but handlers only use `{id}` to look up the record. The `customerId` is silently ignored. A user within the same project tenant can:
- Supply any `customerId` value and still retrieve the contact history or task.
- Enumerate records across all customers in their project by brute-forcing IDs.

Cross-tenant isolation is intact (EF global filters + ProjectId checks), but within a project the URL structure implies per-customer access control that is not enforced.

**Recommendation:**
Add ownership validation in handlers:
```csharp
// In GetContactHistoryByIdQueryHandler:
if (history.CustomerId != request.CustomerId)
    return Result<ContactHistoryDto>.Failure("Contact history not found.");
```

---

## LOW Issues (Jira tickets created)

### LOW-001 — No Explicit Request Body Size Limit
**Jira:** ION-SEC-009
**File:** `src/IonCrm.API/Program.cs`

Default ASP.NET Core request body size is 30 MB. Sync webhook endpoints accept arbitrary `JsonElement` with no cap. An attacker could send oversized payloads to exhaust memory or consume CPU during JSON deserialization.

**Recommendation:**
```csharp
builder.Services.Configure<KestrelServerOptions>(options =>
    options.Limits.MaxRequestBodySize = 1_048_576); // 1 MB global limit
```
Or per-endpoint with `[RequestSizeLimit(524_288)]` (512 KB) on sync controllers.

---

### LOW-002 — Hangfire Dashboard Unauthenticated in Development
**Jira:** ION-SEC-010
**File:** `src/IonCrm.API/Middleware/HangfireAdminAuthFilter.cs` (lines 18–21)

```csharp
if (httpContext...IsDevelopment())
    return true; // ALLOWS UNAUTHENTICATED ACCESS
```

If `ASPNETCORE_ENVIRONMENT` is incorrectly set to `Development` on a staging or production server (a common misconfiguration), the Hangfire dashboard (`/hangfire`) becomes fully accessible without authentication, allowing anyone to enqueue, cancel, or inspect all background jobs.

**Recommendation:** Remove the development bypass. Require SuperAdmin JWT in all environments.

---

### LOW-003 — Git History Contains Exposed Credentials
**Jira:** ION-SEC-011

The credentials cleared in CRITICAL-001 remain in git history. The `.gitignore` prevents future commits but does not rewrite history.

**Recommendation (if repo is externally accessible):**
```bash
# Install git-filter-repo, then:
git filter-repo --path src/IonCrm.API/appsettings.Development.json --invert-paths
git push --force-with-lease origin main
# All collaborators must re-clone.
```

---

## INFO / Recommendations

### INFO-001 — Install Secrets Scanning Pre-commit Hook
```bash
# trufflehog (recommended for .NET projects)
pip install pre-commit
# Add to .pre-commit-config.yaml:
# - repo: https://github.com/trufflesecurity/trufflehog
#   hooks: [{id: trufflehog, args: ["git", "file://."]}]
pre-commit install
```

### INFO-002 — CLAUDE.md Contains Real Infrastructure Hostnames
`CLAUDE.md` lists Neon dev and production DB hostnames and usernames (without passwords). While not immediately exploitable, this reduces time-to-exploit in a breach scenario. Consider moving environment-specific metadata to a non-committed `.claude.local.md`.

### INFO-003 — EF Core Command Logging Correctly Muted
`appsettings.json` sets `Microsoft.EntityFrameworkCore.Database.Command: Warning` — EF Core query logging (which can include parameter values) is suppressed in production. Good practice confirmed. ✅

---

## Multi-Tenant Isolation Audit

All controller endpoints were audited. Isolation is enforced at two layers:
1. **EF Core global query filters** on `Customer`, `ContactHistory`, `CustomerTask`, `Opportunity`, `SyncLog`, `Project`, `UserProjectRole` — tenant filter `ProjectId IN (user's ProjectIds)` applied on every query.
2. **Handler-level checks** — explicit `_currentUser.ProjectIds.Contains(record.ProjectId)` before any mutation.

| Endpoint | Isolation Layer | Result |
|----------|-----------------|--------|
| `GET /customers` | EF global filter | ✅ |
| `GET /customers/{id}` | EF filter + handler | ✅ |
| `PUT /customers/{id}` | EF filter + handler | ✅ |
| `DELETE /customers/{id}` | EF filter + handler | ✅ |
| `GET /customers/{cid}/contact-histories` | EF global filter | ✅ |
| `GET /customers/{cid}/contact-histories/{id}` | EF filter + handler (customerId not validated) | ⚠️ MEDIUM-004 |
| `GET /customers/{cid}/tasks` | EF global filter | ✅ |
| `GET /customers/{cid}/tasks/{id}` | EF filter + handler (customerId not validated) | ⚠️ MEDIUM-004 |
| `GET /tasks?projectId=` | Handler validates ProjectIds.Contains | ✅ |
| `GET /sync/logs` | Handler enforces project scope for non-SuperAdmin | ✅ |
| `POST /sync/saas-a` | API key auth + ProjectId from config | ✅ |
| `POST /sync/saas-b` | API key auth + ProjectId from config | ✅ |
| `POST /sync/trigger` | SuperAdmin policy | ✅ |
| `GET /users` | Handler scopes to user's project | ✅ |
| `POST /migration/run` | SuperAdmin policy (class-level) | ✅ |
| `GET /migration/status` | SuperAdmin policy (class-level) | ✅ |

**Overall isolation posture: STRONG** — no cross-tenant data leakage paths identified beyond MEDIUM-004 (intra-project, not cross-tenant).

---

## Files Changed in This Audit

| File | Change | Reason |
|------|--------|--------|
| `src/IonCrm.API/appsettings.Development.json` | Credentials wiped; replaced with placeholder comments | CRITICAL-001 |
| `src/IonCrm.API/Program.cs` | Swagger gated behind `!IsProduction()` | CRITICAL-002 |
| `.gitignore` | Created — excludes `appsettings.Development.json` and all secrets files | CRITICAL-001 |

---

## Summary

**2 critical, 4 high, 4 medium, 3 low issues found.**

The two critical issues have been fixed directly in code. All remaining issues have Jira tickets (ION-SEC-001 through ION-SEC-011) for sprint prioritization.

The application's overall security architecture is sound: JWT with short expiry, SHA-256 hashed refresh tokens with rotation, bcrypt cost-12 passwords, EF Core global tenant filters, FluentValidation on all inputs, parameterized queries, and structured error handling. The critical deficiencies are operational (credential management) rather than architectural.

*Report generated by automated security audit — ION CRM, 2026-03-25*
