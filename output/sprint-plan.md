# ION CRM — Full Sprint Plan
> Generated: 2026-03-23
> Orchestrator: Claude (Product Manager Agent)
> Stack: ASP.NET Core 8 · Clean Architecture · PostgreSQL · React 18 · shadcn/ui · Railway

---

## 📋 PROJECT SUMMARY

| Item | Detail |
|------|--------|
| Architecture | Clean Architecture (Domain → Application → Infrastructure → API) |
| Auth | JWT Bearer (15min access / 7day refresh), RBAC + Project-scoped |
| Multi-tenancy | SuperAdmin (all data) + Project-scoped roles (ProjectAdmin, SalesManager, SalesRep, Accounting) |
| Sync | SaaS A & B → CRM every 15min (pull); CRM → SaaS instant push (subscriptions, status changes) |
| Migration | One-time MSSQL .bak → PostgreSQL (customers + contact history only) |
| Frontend | React 18, shadcn/ui, Zustand, React Query, dark mode default, Turkish language, mobile-first |
| Deploy | Railway via GitHub Actions CI/CD |
| DB File | `/input/database/crm.bak` — 4.4 MB Microsoft SQL Server backup (confirmed) |

---

## ⚠️ APPROVAL GATES (MANDATORY)
Per CLAUDE.md — Orchestrator MUST pause at:
1. ✅ After sprint planning → **before any code written** ← YOU ARE HERE
2. After DB schema design → before migrations run
3. After each Sprint completion → before next sprint starts
4. Before any deployment to Railway

---

# SPRINT 0 — Analysis & Architecture

**Goal:** Understand legacy data, design the target schema, define all API contracts, and scaffold the solution structure. No production code written until this sprint is approved.

**Duration:** 2–3 days
**Agent:** Architect Agent
**Story Points Total:** 21

---

### 🎫 ION-001
**Title:** [ARCHITECT] Analyze MSSQL .bak file and extract legacy schema
**Sprint:** Sprint 0
**Story Points:** 5
**Labels:** backend, migration, analysis
**Description:**
- Mount `/input/database/crm.bak` (4.4 MB MSSQL backup)
- Use `mssql-scripter` or restore to a local MSSQL Docker container to extract DDL
- Document every table, column, data type, FK relationship, and index
- Identify which tables map to: customers, contact history, users, projects
- Flag tables/columns NOT needed in new system (do NOT migrate everything)
- Output: `/output/legacy-schema.md` with full table inventory
- Output: `/output/migration-mapping.md` with old-column → new-column mapping

**Acceptance Criteria:**
- [ ] All legacy tables documented with row count estimates
- [ ] Mapping document identifies source → target for customers and contact history
- [ ] Columns with PII flagged for secure handling
- [ ] Any MSSQL-specific types (money, nvarchar, bit) mapped to PostgreSQL equivalents

---

### 🎫 ION-002
**Title:** [ARCHITECT] Design PostgreSQL target schema
**Sprint:** Sprint 0
**Story Points:** 8
**Labels:** backend, database, architecture
**Description:**
Design the complete target PostgreSQL schema following these rules from CLAUDE.md:
- Every table: `Id (UUID/Guid)`, `CreatedAt`, `UpdatedAt`, `IsDeleted` (soft delete)
- Every query has implicit `ProjectId` tenant filter (except SuperAdmin)
- Use EF Core migrations — no raw SQL ever

**Tables to design:**

```
Projects          — Id, Name, SaasCode (A|B), WebhookUrl, SyncEnabled, ...
Users             — Id, Email, PasswordHash, FullName, IsActive, IsSuperAdmin, ...
UserProjectRoles  — UserId, ProjectId, Role (enum), ...
Customers         — Id, ProjectId, ExternalId (from SaaS), FullName, Email, Phone,
                    Company, Status (enum), Source (enum), ...
ContactHistory    — Id, ProjectId, CustomerId, UserId, Type (Call|Email|Meeting|Note),
                    Subject, Body, OccurredAt, ...
Notes             — Id, ProjectId, CustomerId, UserId, Content, ...
Tasks             — Id, ProjectId, CustomerId, AssignedUserId, Title, DueDate,
                    IsCompleted, Priority (enum), ...
Opportunities     — Id, ProjectId, CustomerId, OwnerId, Title, Value (decimal),
                    Stage (enum), ExpectedCloseDate, ...
SyncLogs          — Id, ProjectId, SaasSource (A|B), Direction (Inbound|Outbound),
                    Status (Success|Failed|Retrying), Payload (jsonb),
                    AttemptCount, NextRetryAt, Error, ...
RefreshTokens     — Id, UserId, Token, ExpiresAt, IsRevoked, ...
AuditLogs         — Id, ProjectId, UserId, Action, EntityType, EntityId,
                    OldValues (jsonb), NewValues (jsonb), ...
```

**Output:** `/output/db-schema.md` with full ERD description and column specs
**Output:** EF Core entity class stubs in `/output/entity-stubs/`

**Acceptance Criteria:**
- [ ] All entities have base fields (Id, CreatedAt, UpdatedAt, IsDeleted)
- [ ] Foreign key relationships defined
- [ ] Indexes identified (ProjectId on every tenant table, Email unique on Users)
- [ ] Enum values listed for all enum columns
- [ ] Schema reviewed against legacy mapping for completeness

---

### 🎫 ION-003
**Title:** [ARCHITECT] Define API contracts and OpenAPI spec
**Sprint:** Sprint 0
**Story Points:** 5
**Labels:** backend, api, architecture
**Description:**
Define all REST endpoints the system will expose. Format: OpenAPI 3.0 YAML or detailed markdown.

**Endpoint groups:**
```
Auth:
  POST   /api/v1/auth/login
  POST   /api/v1/auth/refresh
  POST   /api/v1/auth/logout
  POST   /api/v1/auth/register (SuperAdmin only)

SuperAdmin:
  GET    /api/v1/admin/projects
  POST   /api/v1/admin/projects
  GET    /api/v1/admin/users
  PUT    /api/v1/admin/users/{id}/roles

Customers:
  GET    /api/v1/customers            (paginated, filtered, tenant-scoped)
  POST   /api/v1/customers
  GET    /api/v1/customers/{id}
  PUT    /api/v1/customers/{id}
  DELETE /api/v1/customers/{id}       (soft delete)
  GET    /api/v1/customers/{id}/history
  GET    /api/v1/customers/{id}/tasks
  GET    /api/v1/customers/{id}/opportunities

ContactHistory:
  POST   /api/v1/customers/{id}/history
  PUT    /api/v1/history/{id}
  DELETE /api/v1/history/{id}

Tasks:
  GET    /api/v1/tasks                (my tasks, overdue, etc.)
  POST   /api/v1/tasks
  PUT    /api/v1/tasks/{id}
  PATCH  /api/v1/tasks/{id}/complete

Opportunities:
  GET    /api/v1/opportunities        (pipeline view, paginated)
  POST   /api/v1/opportunities
  PUT    /api/v1/opportunities/{id}
  PATCH  /api/v1/opportunities/{id}/stage

Sync:
  POST   /api/v1/sync/saas-a         (SaaS A pushes data here)
  POST   /api/v1/sync/saas-b         (SaaS B pushes data here)
  GET    /api/v1/sync/logs            (admin view)

Dashboard:
  GET    /api/v1/dashboard/summary    (KPIs per project)
```

**Standard response wrapper:**
```json
{
  "success": true,
  "data": {},
  "errors": [],
  "pagination": { "page": 1, "pageSize": 20, "total": 150 }
}
```

**Output:** `/output/api-contracts.md`

**Acceptance Criteria:**
- [ ] All endpoints listed with HTTP method, path, auth requirement, roles allowed
- [ ] Request/response body schemas defined for each endpoint
- [ ] Error codes and messages standardized (401, 403, 404, 422, 500)
- [ ] Sync payload formats for SaaS A and SaaS B defined

---

### 🎫 ION-004
**Title:** [ARCHITECT] Define solution folder structure and project scaffold plan
**Sprint:** Sprint 0
**Story Points:** 3
**Labels:** backend, devops, architecture
**Description:**
Document the exact folder/project structure before any code is written.

```
IonCrm/
├── src/
│   ├── IonCrm.Domain/
│   │   ├── Entities/
│   │   ├── Enums/
│   │   ├── Interfaces/
│   │   └── ValueObjects/
│   ├── IonCrm.Application/
│   │   ├── Common/         (ApiResponse, Result, PaginatedList)
│   │   ├── Auth/           (Commands, Queries, DTOs)
│   │   ├── Customers/
│   │   ├── Opportunities/
│   │   ├── Tasks/
│   │   ├── Sync/
│   │   └── Dashboard/
│   ├── IonCrm.Infrastructure/
│   │   ├── Persistence/    (AppDbContext, Migrations, Repositories)
│   │   ├── Services/       (SyncBackgroundService, EmailService)
│   │   ├── ExternalApis/   (SaasAClient, SaasBClient)
│   │   └── Migration/      (LegacyMigrationService)
│   └── IonCrm.API/
│       ├── Controllers/
│       ├── Middleware/     (ExceptionMiddleware, TenantMiddleware)
│       └── Extensions/
├── tests/
│   ├── IonCrm.Tests.Unit/
│   └── IonCrm.Tests.Integration/
├── frontend/
│   ├── src/
│   │   ├── components/     (shadcn/ui wrappers)
│   │   ├── pages/
│   │   ├── stores/         (Zustand)
│   │   ├── hooks/          (React Query hooks)
│   │   ├── api/            (axios instances)
│   │   └── lib/
│   └── public/
├── .github/
│   └── workflows/
│       ├── ci.yml
│       └── deploy.yml
├── docker-compose.yml      (local dev: postgres + mssql)
├── .env.example
└── README.md
```

**Output:** `/output/project-structure.md`

**Acceptance Criteria:**
- [ ] Every project layer mapped to Clean Architecture purpose
- [ ] DI registration strategy described
- [ ] Local dev environment documented (Docker Compose with Postgres + MSSQL for .bak analysis)
- [ ] Environment variables listed (.env.example template)

---

# SPRINT 1 — Foundation

**Goal:** Working solution scaffold with authentication, multi-tenant middleware, base entity framework, and CI/CD pipeline.

**Duration:** 3–4 days
**Agents:** Backend Agent, DevOps Agent
**Story Points Total:** 34

---

### 🎫 ION-010
**Title:** [BACKEND] Scaffold .NET solution with Clean Architecture projects
**Sprint:** Sprint 1
**Story Points:** 3
**Labels:** backend
**Description:**
- Create solution with 5 projects: Domain, Application, Infrastructure, API, Tests
- Wire project references (API → Infrastructure → Application → Domain)
- Install base NuGet packages:
  - MediatR, FluentValidation, AutoMapper (Application)
  - EF Core, Npgsql, Hangfire (Infrastructure)
  - Swashbuckle, Serilog (API)
- Configure `appsettings.json` with environment variable references only (no hardcoded values)
- Create `.env.example` with all required environment variables

**Acceptance Criteria:**
- [ ] `dotnet build` succeeds with 0 errors
- [ ] All project references correctly set
- [ ] No secrets hardcoded anywhere

---

### 🎫 ION-011
**Title:** [BACKEND] Implement base entities, Result<T> pattern, and ApiResponse wrapper
**Sprint:** Sprint 1
**Story Points:** 3
**Labels:** backend
**Description:**
In `IonCrm.Domain`:
- Create `BaseEntity` with: `Id (Guid)`, `CreatedAt`, `UpdatedAt`, `IsDeleted`
- Create `Result<T>` type with success/failure states and error messages

In `IonCrm.Application`:
- Create `ApiResponse<T>` wrapper: `{ success, data, errors[], pagination? }`
- Create `PaginatedList<T>` with page/pageSize/total
- Create `PaginationQuery` base class

**Acceptance Criteria:**
- [ ] All domain entities inherit BaseEntity
- [ ] Result<T> used for all business logic returns (no exceptions for business errors)
- [ ] ApiResponse<T> returned from all controllers

---

### 🎫 ION-012
**Title:** [BACKEND] EF Core setup, AppDbContext, and initial migration
**Sprint:** Sprint 1
**Story Points:** 5
**Labels:** backend, database
**Description:**
- Implement `AppDbContext` in Infrastructure layer
- Configure all entities from Sprint 0 schema design
- Global query filter on every entity: `IsDeleted == false && ProjectId == currentProjectId` (SuperAdmin bypasses)
- Seed initial SuperAdmin user (credentials from ENV)
- Create and run initial migration: `dotnet ef migrations add InitialCreate`
- Test with local Postgres via Docker Compose

**Acceptance Criteria:**
- [ ] `dotnet ef database update` runs without errors
- [ ] Tenant filter active on all project-scoped queries
- [ ] SuperAdmin bypass works correctly
- [ ] Seed data creates SuperAdmin user

---

### 🎫 ION-013
**Title:** [BACKEND] JWT Authentication — login, refresh, logout
**Sprint:** Sprint 1
**Story Points:** 8
**Labels:** backend, auth
**Description:**
Implement full JWT auth flow:

**LoginCommand:**
- Accept email + password
- Validate with FluentValidation
- bcrypt verify (cost 12)
- Return: `{ accessToken (15min), refreshToken (7days), user: { id, email, fullName, projects[], roles{} } }`
- JWT payload: `userId`, `projectIds[]`, `roles{ [projectId]: role }`

**RefreshCommand:**
- Accept refreshToken
- Validate not revoked, not expired
- Rotate: issue new access + refresh token, revoke old refresh token

**LogoutCommand:**
- Revoke refresh token

**Middleware:**
- `TenantMiddleware` — extracts projectId from route/header, validates user has access
- Rate limiting on `/auth/login` (5 req/min per IP)

**Acceptance Criteria:**
- [ ] Login returns correct JWT with project/role claims
- [ ] Refresh token rotation works
- [ ] Expired/revoked tokens rejected with 401
- [ ] Rate limiting blocks brute force
- [ ] Passwords never logged

---

### 🎫 ION-014
**Title:** [BACKEND] Role-based authorization middleware and policies
**Sprint:** Sprint 1
**Story Points:** 5
**Labels:** backend, auth
**Description:**
Implement authorization policies:
- `SuperAdminOnly` — IsSuperAdmin claim
- `ProjectAdmin` — role in current project context
- `SalesManager` — SalesManager or above in project
- `SalesRep` — any role in project
- `Accounting` — Accounting role in project

Create `[RequireProjectRole(Role.SalesRep)]` attribute for controller decoration.

Implement `ICurrentUserService` — exposes:
- `UserId`, `IsSuperAdmin`, `CurrentProjectId`, `HasRoleInProject(projectId, role)`

**Acceptance Criteria:**
- [ ] SuperAdmin can access all project endpoints
- [ ] SalesRep cannot access other project's data (403)
- [ ] Accounting cannot access sales endpoints (403)
- [ ] ICurrentUserService injected and working in all layers

---

### 🎫 ION-015
**Title:** [BACKEND] Global exception middleware and Serilog structured logging
**Sprint:** Sprint 1
**Story Points:** 3
**Labels:** backend
**Description:**
- Global exception handler middleware — catches all unhandled exceptions, returns `ApiResponse` with appropriate status codes
- Never expose stack traces in production
- Serilog configuration: structured JSON logging, console sink (dev), file sink (prod)
- Correlation ID middleware — adds `X-Correlation-Id` header to every request
- Never log: passwords, tokens, connection strings, PII

**Acceptance Criteria:**
- [ ] 500 errors return `{ success: false, errors: ["Internal server error"] }` in prod
- [ ] Correlation ID present on all log entries
- [ ] Sensitive data not logged

---

### 🎫 ION-016
**Title:** [DEVOPS] GitHub Actions CI pipeline + Docker Compose local dev
**Sprint:** Sprint 1
**Story Points:** 5
**Labels:** devops
**Description:**
**CI Pipeline (`.github/workflows/ci.yml`):**
- Trigger: push to any branch, PR to main
- Steps: checkout → setup .NET 8 → restore → build → test → lint frontend
- Block merge if tests fail

**Docker Compose (`docker-compose.yml`):**
- `postgres` service (latest, port 5432)
- `mssql` service (for .bak analysis in Sprint 0 — keep for dev)
- `api` service (build from Dockerfile)
- `frontend` service

**Dockerfile** for API (multi-stage: build → runtime)

**Railway deploy config** (`railway.toml`):
- Environment variables from Railway dashboard
- Health check endpoint `/health`
- Implement `/health` endpoint in API

**Acceptance Criteria:**
- [ ] CI runs on every PR
- [ ] `docker-compose up` starts all services locally
- [ ] `/health` returns 200 with DB connectivity status
- [ ] No secrets in git

---

### 🎫 ION-017
**Title:** [BACKEND] SuperAdmin project & user management endpoints
**Sprint:** Sprint 1
**Story Points:** 5
**Labels:** backend
**Description:**
SuperAdmin-only endpoints:
- `GET /api/v1/admin/projects` — list all projects
- `POST /api/v1/admin/projects` — create project (name, saasCode, webhookUrl)
- `GET /api/v1/admin/users` — list all users (paginated)
- `POST /api/v1/admin/users` — create user
- `PUT /api/v1/admin/users/{id}/roles` — assign user to project with role
- `DELETE /api/v1/admin/users/{id}/roles/{projectId}` — remove from project

All guarded with `[RequireSuperAdmin]`.

**Acceptance Criteria:**
- [ ] Non-SuperAdmin gets 403
- [ ] User can be assigned to multiple projects with different roles
- [ ] JWT reflects updated roles on next login

---

# SPRINT 2 — Customer Core

**Goal:** Full customer lifecycle management — CRUD, contact history, notes, tasks — all multi-tenant with proper role enforcement.

**Duration:** 3–4 days
**Agent:** Backend Agent
**Story Points Total:** 29

---

### 🎫 ION-020
**Title:** [BACKEND] Customer CRUD with multi-tenant filtering
**Sprint:** Sprint 2
**Story Points:** 8
**Labels:** backend
**Description:**
Full CQRS for customers:

**Commands:** `CreateCustomerCommand`, `UpdateCustomerCommand`, `DeleteCustomerCommand` (soft)
**Queries:** `GetCustomersQuery` (paginated + filter by status/source/search), `GetCustomerByIdQuery`

**Customer fields:** FullName, Email, Phone, Company, Status (Lead/Active/Churned/Prospect), Source (SaasA/SaasB/Manual), ExternalId (for sync mapping), ProjectId, AssignedUserId

**Rules:**
- SalesRep only sees customers assigned to them (`AssignedUserId == currentUserId`)
- SalesManager/ProjectAdmin sees all project customers
- SuperAdmin sees all
- `ExternalId` + `ProjectId` must be unique (for sync upsert)
- All commands validated with FluentValidation

**Endpoints:**
- `GET /api/v1/customers?page=1&pageSize=20&status=Active&search=john`
- `POST /api/v1/customers`
- `GET /api/v1/customers/{id}`
- `PUT /api/v1/customers/{id}`
- `DELETE /api/v1/customers/{id}`

**Acceptance Criteria:**
- [ ] SalesRep cannot see other reps' customers
- [ ] Soft delete sets IsDeleted=true, filters it from all queries
- [ ] Pagination correct (total count accurate with filters)
- [ ] ExternalId uniqueness enforced per project

---

### 🎫 ION-021
**Title:** [BACKEND] Contact history CRUD
**Sprint:** Sprint 2
**Story Points:** 5
**Labels:** backend
**Description:**
Track every interaction with a customer.

**ContactHistory entity:** CustomerId, UserId, Type (Call/Email/Meeting/Note/SMS), Subject, Body, OccurredAt, DurationMinutes (nullable)

**Commands:** `AddContactHistoryCommand`, `UpdateContactHistoryCommand`, `DeleteContactHistoryCommand`
**Queries:** `GetCustomerHistoryQuery` (paginated, filter by type/date range)

**Endpoints:**
- `GET /api/v1/customers/{customerId}/history`
- `POST /api/v1/customers/{customerId}/history`
- `PUT /api/v1/history/{id}`
- `DELETE /api/v1/history/{id}`

**Rules:**
- SalesRep can only edit/delete their own history entries
- SalesManager can edit/delete any

**Acceptance Criteria:**
- [ ] History entries tied to correct customer and user
- [ ] Date range filtering works
- [ ] SalesRep cannot delete other reps' history entries (403)

---

### 🎫 ION-022
**Title:** [BACKEND] Notes module
**Sprint:** Sprint 2
**Story Points:** 3
**Labels:** backend
**Description:**
Customer notes (rich text content stored as plain text/markdown):

**Note entity:** CustomerId, UserId, Content, IsPinned

**Commands:** `CreateNoteCommand`, `UpdateNoteCommand`, `DeleteNoteCommand`
**Queries:** `GetCustomerNotesQuery` (pinned first, then by date)

**Endpoints:**
- `GET /api/v1/customers/{customerId}/notes`
- `POST /api/v1/customers/{customerId}/notes`
- `PUT /api/v1/notes/{id}`
- `DELETE /api/v1/notes/{id}`
- `PATCH /api/v1/notes/{id}/pin`

**Acceptance Criteria:**
- [ ] Pinned notes appear first
- [ ] SalesRep can only edit own notes

---

### 🎫 ION-023
**Title:** [BACKEND] Tasks module
**Sprint:** Sprint 2
**Story Points:** 5
**Labels:** backend
**Description:**
Customer-linked tasks (to-dos, follow-ups, callbacks):

**Task entity:** CustomerId, AssignedUserId, CreatedByUserId, Title, Description, DueDate, IsCompleted, CompletedAt, Priority (Low/Medium/High/Urgent)

**Commands:** `CreateTaskCommand`, `UpdateTaskCommand`, `CompleteTaskCommand`, `DeleteTaskCommand`
**Queries:** `GetMyTasksQuery` (for current user), `GetCustomerTasksQuery`, `GetOverdueTasksQuery`

**Endpoints:**
- `GET /api/v1/tasks` (my tasks, with filters: overdue, today, upcoming, completed)
- `POST /api/v1/tasks`
- `PUT /api/v1/tasks/{id}`
- `PATCH /api/v1/tasks/{id}/complete`
- `DELETE /api/v1/tasks/{id}`
- `GET /api/v1/customers/{customerId}/tasks`

**Acceptance Criteria:**
- [ ] SalesRep only sees tasks assigned to them
- [ ] Overdue filter works based on DueDate vs current UTC time
- [ ] CompleteTask sets CompletedAt timestamp

---

### 🎫 ION-024
**Title:** [BACKEND] Customer 360° view aggregation endpoint
**Sprint:** Sprint 2
**Story Points:** 3
**Labels:** backend
**Description:**
Single endpoint that returns everything about a customer for the detail view:

`GET /api/v1/customers/{id}/360`

Response:
```json
{
  "customer": { ...full customer object },
  "recentHistory": [ ...last 5 entries ],
  "openTasks": [ ...incomplete tasks, sorted by dueDate ],
  "pinnedNotes": [ ...pinned notes ],
  "openOpportunities": [ ...active opportunities ],
  "stats": {
    "totalInteractions": 42,
    "lastContactDate": "2026-03-01",
    "totalOpportunityValue": 15000.00
  }
}
```

**Acceptance Criteria:**
- [ ] Single DB round-trip using EF Core Include/projection
- [ ] Tenant filter applies to all sub-queries
- [ ] Stats are accurate

---

### 🎫 ION-025
**Title:** [BACKEND] Dashboard KPI summary endpoint
**Sprint:** Sprint 2
**Story Points:** 5
**Labels:** backend
**Description:**
`GET /api/v1/dashboard/summary`

Returns per-project KPIs:
```json
{
  "totalCustomers": 150,
  "newCustomersThisMonth": 12,
  "activeLeads": 34,
  "openTasks": 8,
  "overdueTasksCount": 3,
  "openOpportunitiesValue": 125000.00,
  "wonThisMonth": 5,
  "syncStatus": {
    "lastSyncSaasA": "2026-03-23T10:00:00Z",
    "lastSyncSaasB": "2026-03-23T10:05:00Z",
    "failedSyncsLast24h": 0
  }
}
```

SalesRep version: scoped to their customers only.
SalesManager/ProjectAdmin: full project view.

**Acceptance Criteria:**
- [ ] Correct scoping per role
- [ ] All counts accurate
- [ ] syncStatus reflects real SyncLog data

---

# SPRINT 3 — Sync Service

**Goal:** Bidirectional sync engine — SaaS A & B → CRM (every 15 min pull + inbound push endpoint), and CRM → SaaS instant callbacks for subscription/status changes.

**Duration:** 3–4 days
**Agent:** Backend Agent
**Story Points Total:** 34

---

### 🎫 ION-030
**Title:** [BACKEND] Inbound sync endpoint — SaaS A
**Sprint:** Sprint 3
**Story Points:** 5
**Labels:** backend, sync
**Description:**
`POST /api/v1/sync/saas-a` — receives customer data from SaaS A.

**Payload (SaaS A format — to be confirmed from legacy .bak analysis):**
```json
{
  "eventType": "customer.updated",
  "timestamp": "2026-03-23T10:00:00Z",
  "data": {
    "externalId": "saas-a-001",
    "fullName": "Ahmet Yılmaz",
    "email": "ahmet@example.com",
    "phone": "+90...",
    "subscriptionStatus": "active",
    "subscriptionExpiresAt": "2026-12-31T23:59:59Z"
  }
}
```

**Logic:**
1. Validate HMAC signature header (`X-Saas-Signature`) — reject if invalid
2. Find existing customer by `ExternalId + ProjectId` (upsert)
3. Map fields to Customer entity
4. Record SyncLog entry (Success/Failed)
5. Return 200 immediately, process async

**Acceptance Criteria:**
- [ ] Invalid HMAC → 401
- [ ] New customer created if ExternalId not found
- [ ] Existing customer updated (not duplicated)
- [ ] SyncLog entry created for every call

---

### 🎫 ION-031
**Title:** [BACKEND] Inbound sync endpoint — SaaS B
**Sprint:** Sprint 3
**Story Points:** 5
**Labels:** backend, sync
**Description:**
`POST /api/v1/sync/saas-b` — identical structure to ION-030 but SaaS B payload format may differ.

**Additional SaaS B fields:** `companyName`, `sector`, `tier (gold/silver/bronze)`

**Logic:** Same upsert logic as SaaS A, but with SaaS B field mapping.
Separate HMAC secret per SaaS source (from ENV).

**Acceptance Criteria:**
- [ ] SaaS B-specific fields mapped correctly
- [ ] Does not interfere with SaaS A customer records (different ExternalId namespace)
- [ ] SyncLog records source as "SaasB"

---

### 🎫 ION-032
**Title:** [BACKEND] Hangfire background sync job — 15-minute pull
**Sprint:** Sprint 3
**Story Points:** 8
**Labels:** backend, sync
**Description:**
.NET BackgroundService + Hangfire recurring job that pulls from SaaS A & B every 15 minutes.

**SyncBackgroundService:**
- Registered as `IHostedService`
- Hangfire recurring job: `"sync-saas-all"` → every 15 minutes
- Calls `ISaasAClient.GetUpdatedCustomers(since: lastSuccessfulSync)`
- Calls `ISaasBClient.GetUpdatedCustomers(since: lastSuccessfulSync)`
- Uses cursor-based sync: reads `LastSyncAt` from SyncLog per project per source
- Processes response through same upsert logic as inbound endpoints
- Concurrency: max 1 sync job running per project at a time (Hangfire distributed lock)

**ISaasAClient / ISaasBClient** (in Infrastructure/ExternalApis):
- HTTP client with retry (Polly: 3 retries, exponential backoff)
- Timeout: 30 seconds
- API keys from ENV variables

**Acceptance Criteria:**
- [ ] Job runs every 15 minutes (verified in Hangfire dashboard)
- [ ] Uses LastSyncAt cursor — does not re-import already synced records
- [ ] Max 1 concurrent sync per project
- [ ] Failed sync retried 3 times with exponential backoff
- [ ] SyncLog records every attempt

---

### 🎫 ION-033
**Title:** [BACKEND] Outbound instant callback — CRM → SaaS
**Sprint:** Sprint 3
**Story Points:** 8
**Labels:** backend, sync
**Description:**
When specific events occur in CRM, push changes instantly to SaaS.

**Trigger events:**
- Customer subscription status changed
- Customer subscription extended
- Customer marked as churned
- Customer notes updated (if SaaS has callback URL)

**Implementation:**
- `IDomainEvent` on Customer entity (e.g., `CustomerStatusChangedEvent`)
- MediatR `INotificationHandler` processes the event
- `IOutboundSyncService.NotifySaas(projectId, payload)` called
- Finds project's `WebhookUrl` and POST's callback
- HMAC sign outbound request with shared secret

**Retry logic:**
- Failed callbacks queued in Hangfire
- 3 retries with exponential backoff (1min, 5min, 15min)
- After 3 failures: mark SyncLog as Failed, alert SuperAdmin

**Payload to SaaS:**
```json
{
  "event": "customer.status_changed",
  "externalId": "saas-a-001",
  "newStatus": "active",
  "effectiveAt": "2026-03-23T10:00:00Z",
  "source": "ion-crm"
}
```

**Acceptance Criteria:**
- [ ] Status change triggers callback within 1 second
- [ ] Callback signed with HMAC
- [ ] Retry queue works
- [ ] SyncLog records outbound events

---

### 🎫 ION-034
**Title:** [BACKEND] Sync logs admin endpoint & retry mechanism
**Sprint:** Sprint 3
**Story Points:** 5
**Labels:** backend, sync
**Description:**
- `GET /api/v1/sync/logs?source=SaasA&status=Failed&from=2026-03-01` — admin only
- `POST /api/v1/sync/logs/{id}/retry` — manually retry a failed sync
- Display in response: source, direction, status, attemptCount, error, payload (sanitized)

**SyncLog retention:** soft-delete logs older than 90 days (nightly Hangfire job).

**Acceptance Criteria:**
- [ ] Only SuperAdmin or ProjectAdmin can view sync logs
- [ ] Manual retry re-queues the job
- [ ] Log retention job runs nightly

---

### 🎫 ION-035
**Title:** [BACKEND] Hangfire dashboard and monitoring
**Sprint:** Sprint 3
**Story Points:** 3
**Labels:** backend, devops
**Description:**
- Enable Hangfire dashboard at `/hangfire` (SuperAdmin access only)
- Configure Hangfire to use PostgreSQL storage
- Set up job success/failure metrics
- Add Hangfire basic auth protection (ENV-sourced credentials)

**Acceptance Criteria:**
- [ ] Hangfire dashboard accessible to SuperAdmin only
- [ ] All background jobs visible
- [ ] Job history retained for 7 days

---

# SPRINT 4 — Sales Pipeline

**Goal:** Full opportunities/pipeline management with stage tracking, funnel views, and performance reporting.

**Duration:** 3 days
**Agent:** Backend Agent
**Story Points Total:** 21

---

### 🎫 ION-040
**Title:** [BACKEND] Opportunities CRUD
**Sprint:** Sprint 4
**Story Points:** 5
**Labels:** backend
**Description:**
**Opportunity entity:** CustomerId, OwnerId (UserId), Title, Description, Value (decimal), Currency (default TRY), Stage (enum), ExpectedCloseDate, ActualCloseDate, WonLostReason, Probability (0-100)

**Stage enum:** Lead → Qualified → Proposal → Negotiation → Won → Lost

**Commands:** `CreateOpportunityCommand`, `UpdateOpportunityCommand`, `ChangeOpportunityStageCommand`, `DeleteOpportunityCommand`

**Endpoints:**
- `GET /api/v1/opportunities?stage=Proposal&ownerId=xxx`
- `POST /api/v1/opportunities`
- `GET /api/v1/opportunities/{id}`
- `PUT /api/v1/opportunities/{id}`
- `PATCH /api/v1/opportunities/{id}/stage`
- `DELETE /api/v1/opportunities/{id}`

**Rules:**
- SalesRep: sees only their own opportunities
- SalesManager: sees all project opportunities
- Stage change recorded in history (AuditLog)

**Acceptance Criteria:**
- [ ] Stage transition validated (can't go from Lead directly to Won)
- [ ] AuditLog entry created on stage change
- [ ] Won/Lost records ActualCloseDate automatically

---

### 🎫 ION-041
**Title:** [BACKEND] Pipeline board query (Kanban view data)
**Sprint:** Sprint 4
**Story Points:** 5
**Labels:** backend
**Description:**
`GET /api/v1/opportunities/pipeline` — returns all open opportunities grouped by stage.

Response:
```json
{
  "columns": [
    {
      "stage": "Lead",
      "count": 12,
      "totalValue": 45000.00,
      "opportunities": [ ...top 20 per stage, sorted by value desc ]
    },
    ...
  ],
  "summary": {
    "totalOpen": 45,
    "totalValue": 380000.00,
    "weightedValue": 142000.00
  }
}
```

Weighted value = sum of (Value × Probability / 100).

**Acceptance Criteria:**
- [ ] All stages represented (even empty ones)
- [ ] Weighted value calculation correct
- [ ] Tenant-scoped (SalesRep sees own only)

---

### 🎫 ION-042
**Title:** [BACKEND] Performance & sales reporting endpoints
**Sprint:** Sprint 4
**Story Points:** 5
**Labels:** backend
**Description:**
`GET /api/v1/reports/sales?from=2026-01-01&to=2026-03-31&groupBy=rep`

Returns:
```json
{
  "period": { "from": "...", "to": "..." },
  "byRep": [
    {
      "userId": "xxx",
      "userName": "Mehmet Kaya",
      "wonCount": 8,
      "wonValue": 45000.00,
      "lostCount": 3,
      "conversionRate": 72.7,
      "avgDealSize": 5625.00,
      "activePipelineValue": 22000.00
    }
  ],
  "totals": { ... }
}
```

SalesRep only sees own stats. SalesManager sees team.

**Acceptance Criteria:**
- [ ] Date range filter works correctly
- [ ] Conversion rate formula: won / (won + lost) × 100
- [ ] Correct role scoping

---

### 🎫 ION-043
**Title:** [BACKEND] Audit log for all entity changes
**Sprint:** Sprint 4
**Story Points:** 6
**Labels:** backend
**Description:**
Implement automatic audit logging via EF Core `SaveChangesAsync` override in `AppDbContext`.

For every Insert/Update/Delete on auditable entities (Customers, Opportunities, ContactHistory):
- Capture: `UserId`, `Action (Created/Updated/Deleted)`, `EntityType`, `EntityId`, `OldValues (jsonb)`, `NewValues (jsonb)`, `Timestamp`
- Store in `AuditLogs` table
- Exclude sensitive fields from audit log values (passwords, tokens)

`GET /api/v1/audit?entityType=Customer&entityId=xxx` — SuperAdmin/ProjectAdmin only

**Acceptance Criteria:**
- [ ] All CRUD operations on auditable entities create audit entries
- [ ] Old and new values stored correctly
- [ ] Sensitive fields excluded
- [ ] Query endpoint works with filters

---

# SPRINT 5 — Frontend

**Goal:** Complete React frontend — all screens, dark mode, mobile-first, Turkish language, shadcn/ui components, connected to live API.

**Duration:** 4–5 days
**Agent:** Frontend Agent
**Story Points Total:** 42

---

### 🎫 ION-050
**Title:** [FRONTEND] React project scaffold with Vite, shadcn/ui, Zustand, React Query
**Sprint:** Sprint 5
**Story Points:** 3
**Labels:** frontend
**Description:**
- Vite + React 18 + TypeScript
- shadcn/ui init with dark mode as default
- Tailwind CSS configured
- Zustand store setup (authStore, uiStore)
- React Query v5 setup with axios interceptor (auto-attach JWT, handle 401 refresh)
- i18n setup (i18next) — Turkish as default locale
- React Router v6 with protected routes
- ESLint + Prettier config

**Acceptance Criteria:**
- [ ] `npm run dev` starts with no errors
- [ ] Dark mode is default, toggle works
- [ ] Turkish locale loaded
- [ ] Protected routes redirect to login if unauthenticated

---

### 🎫 ION-051
**Title:** [FRONTEND] Authentication screens — Login, session management
**Sprint:** Sprint 5
**Story Points:** 3
**Labels:** frontend
**Description:**
- Login page: email/password form, shadcn/ui Card, error handling
- JWT stored in memory (access token) + httpOnly cookie (refresh token)
- Auto-refresh access token 1 minute before expiry
- Logout clears all tokens
- Post-login redirect to last visited page
- Loading spinner on auth state resolution

**Acceptance Criteria:**
- [ ] Login works with real API
- [ ] Token refresh transparent to user
- [ ] Logout fully clears session
- [ ] Invalid credentials show error message in Turkish

---

### 🎫 ION-052
**Title:** [FRONTEND] App shell — sidebar, topbar, navigation
**Sprint:** Sprint 5
**Story Points:** 5
**Labels:** frontend
**Description:**
Responsive app shell:
- **Sidebar** (desktop): logo, nav links (Dashboard, Müşteriler, Görevler, Fırsatlar, Senkron, Ayarlar), user avatar + project switcher at bottom
- **Bottom tab bar** (mobile): Dashboard, Müşteriler, Görevler, Fırsatlar
- **Topbar**: project name, search bar (global), notifications bell, dark/light toggle, user menu
- **Project switcher**: dropdown showing user's projects, switches active project context
- SuperAdmin sees extra "Yönetim Paneli" nav item

**Acceptance Criteria:**
- [ ] Sidebar collapses to icon-only on tablet
- [ ] Bottom tab bar appears below 768px
- [ ] Project switcher updates all data to selected project
- [ ] Active nav item highlighted

---

### 🎫 ION-053
**Title:** [FRONTEND] Dashboard screen
**Sprint:** Sprint 5
**Story Points:** 5
**Labels:** frontend
**Description:**
`/dashboard` — KPI cards + recent activity:

**KPI Cards (shadcn/ui Card):**
- Toplam Müşteri, Bu Ay Yeni, Aktif Fırsatlar (with value), Bugün Görevler
- Each card has icon, value, % change from last month

**Charts (recharts or shadcn/ui charts):**
- Funnel/bar chart: pipeline by stage
- Line chart: new customers last 30 days

**Sync Status Widget:**
- Last sync time for SaaS A and SaaS B
- Red badge if last sync > 30 min ago

**Recent Activity Feed:**
- Last 10 contact history entries across project

**Acceptance Criteria:**
- [ ] All KPIs loaded from `/api/v1/dashboard/summary`
- [ ] Charts render correctly in dark mode
- [ ] Mobile layout stacks cards vertically
- [ ] Data refreshes every 5 minutes (React Query staleTime)

---

### 🎫 ION-054
**Title:** [FRONTEND] Customer list screen with filters and search
**Sprint:** Sprint 5
**Story Points:** 5
**Labels:** frontend
**Description:**
`/musteriler` — paginated customer list:
- Search input (debounced 300ms)
- Filter chips: Status (Tümü / Aktif / Lead / Kayıp / Prospect), Source
- Table (desktop): avatar, name, company, phone, status badge, last contact, assigned rep
- Card grid (mobile): compact customer cards
- Infinite scroll or pagination controls
- "Yeni Müşteri" button → opens Create Customer drawer

**Acceptance Criteria:**
- [ ] Search debounced, triggers API call
- [ ] Filters update URL query params (shareable links)
- [ ] Mobile card layout renders correctly
- [ ] Empty state with illustration shown when no results

---

### 🎫 ION-055
**Title:** [FRONTEND] Customer detail screen (360° view)
**Sprint:** Sprint 5
**Story Points:** 8
**Labels:** frontend
**Description:**
`/musteriler/{id}` — full customer profile:

**Header:** Avatar, name, company, status badge, quick action buttons (Ara, E-posta, Görev Ekle)

**Tabs:**
1. **Özet** — key info, assigned rep, contact info
2. **İletişim Geçmişi** — timeline of all interactions, add new entry button
3. **Notlar** — pinned notes first, add/edit/delete
4. **Görevler** — open tasks list, add task
5. **Fırsatlar** — linked opportunities with stage
6. **Aktivite** — audit log (manager+ only)

**Edit Customer** — inline edit or side drawer

**Acceptance Criteria:**
- [ ] All tabs load data from respective API endpoints
- [ ] Adding history/note/task works without page refresh
- [ ] Edit form validates required fields
- [ ] SalesRep sees only their own history entries (API enforced, UI consistent)

---

### 🎫 ION-056
**Title:** [FRONTEND] Tasks screen
**Sprint:** Sprint 5
**Story Points:** 3
**Labels:** frontend
**Description:**
`/gorevler` — personal task board:
- **Filter tabs:** Bugün / Bu Hafta / Gecikmiş / Tümü / Tamamlananlar
- Task cards: title, customer name (link), priority badge, due date, complete checkbox
- Swipe to complete on mobile (shadcn gesture or custom)
- "Yeni Görev" button → drawer with form
- Overdue count badge on "Gecikmiş" tab

**Acceptance Criteria:**
- [ ] Complete checkbox calls API and removes from pending list
- [ ] Overdue tasks shown in red
- [ ] Task creation pre-fills customer if navigated from customer detail

---

### 🎫 ION-057
**Title:** [FRONTEND] Opportunities pipeline (Kanban) screen
**Sprint:** Sprint 5
**Story Points:** 5
**Labels:** frontend
**Description:**
`/firsatlar` — Kanban board:
- Columns: Lead → Qualified → Proposal → Negotiation → Won → Lost
- Each column shows count + total value
- Drag-and-drop cards between stages (dnd-kit)
- Opportunity card: title, customer, value, close date, owner avatar
- Mobile: horizontal scroll or list view with stage filter

**Opportunity Detail Drawer:**
- Edit all fields
- Stage change with confirmation (Won/Lost requires reason)
- Activity history

**Acceptance Criteria:**
- [ ] Drag-and-drop triggers `PATCH /opportunities/{id}/stage`
- [ ] Won/Lost confirmation modal with reason field
- [ ] Mobile scrollable columns work on iOS/Android WebView
- [ ] Currency formatted as ₺ (Turkish Lira)

---

### 🎫 ION-058
**Title:** [FRONTEND] SuperAdmin panel
**Sprint:** Sprint 5
**Story Points:** 3
**Labels:** frontend
**Description:**
`/admin` — SuperAdmin only:
- **Projects tab:** list all projects, add project, configure webhook URL, toggle sync on/off
- **Users tab:** list all users, create user, assign to project with role, remove from project
- **Sync Logs tab:** table of sync logs (filterable by source/status), retry button on failed
- **System Health:** DB connection, last sync times, Hangfire job status

**Acceptance Criteria:**
- [ ] Non-SuperAdmin gets redirect to dashboard
- [ ] User role assignment updates immediately
- [ ] Retry button calls API and shows success/fail toast

---

# SPRINT 6 — Migration, Testing & Deploy

**Goal:** Migrate legacy MSSQL data to PostgreSQL, achieve full test coverage, security audit, and deploy to Railway.

**Duration:** 3–4 days
**Agents:** Backend Agent, DevOps Agent, Testing Agent
**Story Points Total:** 34

---

### 🎫 ION-060
**Title:** [BACKEND] Legacy MSSQL migration service — customers
**Sprint:** Sprint 6
**Story Points:** 8
**Labels:** backend, migration
**Description:**
Standalone `MigrationService` (console app or hosted service) that reads from MSSQL (mounted `.bak`) and writes to PostgreSQL.

**Migration scope (from ION-001 analysis):**
- Customers table → `Customers` entity
- Contact history table → `ContactHistory` entity
- Map old IDs to new GUIDs (store mapping in `MigrationIdMap` table for traceability)

**Idempotency:**
- Check if ExternalId already exists before inserting
- Can run multiple times safely
- `--dry-run` flag for safe preview

**Logging:**
- Log every migrated record count
- Log any records skipped/failed with reason
- Output migration report to `/output/migration-report.md`

**Acceptance Criteria:**
- [ ] All legacy customers migrated with no data loss
- [ ] Contact history linked to correct migrated customers
- [ ] Re-running migration does not create duplicates
- [ ] Migration report shows counts and any failures

---

### 🎫 ION-061
**Title:** [BACKEND] Data validation post-migration
**Sprint:** Sprint 6
**Story Points:** 3
**Labels:** backend, migration, testing
**Description:**
- Count verification: legacy record count == migrated record count
- Spot-check 20 random customers for data accuracy
- Verify all ContactHistory entries linked to valid CustomerId
- Check no null values in required fields
- Generate `/output/validation-report.md`

**Acceptance Criteria:**
- [ ] 100% of customers migrated (or known exceptions documented)
- [ ] No orphaned ContactHistory records
- [ ] Validation report signed off

---

### 🎫 ION-062
**Title:** [TESTING] Unit tests — Application layer (CQRS handlers)
**Sprint:** Sprint 6
**Story Points:** 5
**Labels:** testing
**Description:**
xUnit tests for all MediatR command/query handlers.

**Coverage targets:**
- All Customer CRUD handlers
- Auth commands (login, refresh)
- Sync upsert logic
- Tenant filter enforcement
- Opportunity stage transitions

**Mocking:** Moq for repositories, ICurrentUserService

Minimum 80% code coverage on Application layer.

**Acceptance Criteria:**
- [ ] `dotnet test` passes with 0 failures
- [ ] 80%+ coverage on Application layer
- [ ] Tenant isolation tested explicitly (SalesRep cannot access other rep's data)

---

### 🎫 ION-063
**Title:** [TESTING] Integration tests — API endpoints
**Sprint:** Sprint 6
**Story Points:** 5
**Labels:** testing
**Description:**
xUnit integration tests using `WebApplicationFactory` with test PostgreSQL instance.

**Test scenarios:**
- Login flow (valid/invalid credentials)
- CRUD operations with correct/incorrect permissions (403 tests)
- Sync endpoint with valid/invalid HMAC signature
- Tenant isolation (user from Project A cannot access Project B data)
- Pagination correctness

**Acceptance Criteria:**
- [ ] All happy-path API tests pass
- [ ] All 403/401/404 scenarios tested
- [ ] Tests run in CI pipeline

---

### 🎫 ION-064
**Title:** [TESTING] Security audit checklist
**Sprint:** Sprint 6
**Story Points:** 3
**Labels:** testing, security
**Description:**
Manual + automated security review:

**Checklist:**
- [ ] No secrets in git history (`git log` + trufflehog scan)
- [ ] All auth endpoints rate-limited
- [ ] JWT expiry enforced
- [ ] CORS locked to allowed origins
- [ ] All inputs validated (FluentValidation + EF parameterized)
- [ ] HTTPS-only headers in production (HSTS)
- [ ] Tenant filter on every query (SQL explain plan review)
- [ ] Audit log captures all data changes
- [ ] Password hashing is bcrypt cost 12
- [ ] Refresh token rotation working
- [ ] HMAC verification on inbound sync

**Output:** `/output/security-audit.md`

**Acceptance Criteria:**
- [ ] Zero critical findings
- [ ] All items checked and documented

---

### 🎫 ION-065
**Title:** [DEVOPS] Railway deployment — production setup
**Sprint:** Sprint 6
**Story Points:** 5
**Labels:** devops
**Description:**
Deploy to Railway:

**Services on Railway:**
- `ion-crm-api` — .NET 8 API (Dockerfile)
- `ion-crm-frontend` — React (Vite build, static serve)
- `ion-crm-postgres` — Railway Postgres addon

**GitHub Actions deploy workflow (`.github/workflows/deploy.yml`):**
- Trigger: push to `main`
- Steps: build → test → docker build → push to Railway

**Railway environment variables to configure:**
- `DATABASE_URL`
- `JWT_SECRET`
- `JWT_REFRESH_SECRET`
- `SAAS_A_API_KEY`, `SAAS_A_WEBHOOK_SECRET`
- `SAAS_B_API_KEY`, `SAAS_B_WEBHOOK_SECRET`
- `HANGFIRE_DASHBOARD_USER`, `HANGFIRE_DASHBOARD_PASS`
- `CORS_ALLOWED_ORIGINS`

**Acceptance Criteria:**
- [ ] Production deploy succeeds via GitHub Actions
- [ ] Health check returns 200 on Railway URL
- [ ] HTTPS enforced (Railway auto-TLS)
- [ ] No hardcoded secrets in any config file
- [ ] Rollback procedure documented

---

### 🎫 ION-066
**Title:** [DEVOPS] Production monitoring & alerting setup
**Sprint:** Sprint 6
**Story Points:** 5
**Labels:** devops
**Description:**
- Serilog → Railway logs (structured JSON)
- Health check endpoint `/health` monitors: DB connectivity, last sync time, disk
- Set up Railway alerts: CPU > 80%, memory > 85%, deploy failures
- Document runbook: `/output/runbook.md`
  - How to restart API
  - How to trigger manual sync
  - How to run migration again
  - How to rotate JWT secrets

**Acceptance Criteria:**
- [ ] Logs visible in Railway dashboard
- [ ] Health endpoint monitored
- [ ] Runbook written and reviewed

---

# 📊 SPRINT SUMMARY TABLE

| Sprint | Name | Stories | Points | Duration | Agents |
|--------|------|---------|--------|----------|--------|
| Sprint 0 | Analysis & Architecture | 4 | 21 | 2–3 days | Architect |
| Sprint 1 | Foundation | 8 | 34 | 3–4 days | Backend, DevOps |
| Sprint 2 | Customer Core | 6 | 29 | 3–4 days | Backend |
| Sprint 3 | Sync Service | 6 | 34 | 3–4 days | Backend |
| Sprint 4 | Sales Pipeline | 4 | 21 | 3 days | Backend |
| Sprint 5 | Frontend | 9 | 42 | 4–5 days | Frontend |
| Sprint 6 | Migration & Testing | 7 | 34 | 3–4 days | Backend, DevOps, Testing |
| **TOTAL** | | **44 stories** | **215 pts** | **~3.5 weeks** | |

---

*⚠️ AWAITING HUMAN APPROVAL FOR SPRINT 0 BEFORE ANY CODE IS WRITTEN*
