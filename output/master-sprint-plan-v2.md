# ION CRM — Master Sprint Plan v2
> **Generated:** 2026-03-26
> **Orchestrator:** Claude (Product Manager Agent)
> **Stack:** ASP.NET Core 8 · Clean Architecture · PostgreSQL (Neon) · React 18 · shadcn/ui · Tailwind · Railway
> **Repo:** https://github.com/ionivasoftware/ionivacrm
> **Working dir:** `/root/ems-team/output`

---

## 📋 PROJECT OVERVIEW

| Item | Detail |
|------|--------|
| **System** | ION CRM — Multi-tenant SaaS CRM for managing customers, pipeline, and sync |
| **Architecture** | Clean Architecture: Domain → Application → Infrastructure → API |
| **Auth** | JWT Bearer (15-min access / 7-day refresh), RBAC + Project-scoped |
| **Multi-tenancy** | SuperAdmin (all projects) + ProjectAdmin / SalesManager / SalesRep / Accounting (project-scoped) |
| **Sync** | SaaS A & B → CRM pull every 15 min; CRM → SaaS instant push on events |
| **Migration** | One-time MSSQL .bak → PostgreSQL (639 customers, 892 contacts — ✅ DONE) |
| **Frontend** | React 18, shadcn/ui, Tailwind, Zustand, React Query, dark mode, Turkish, mobile-first |
| **Deploy** | Railway via GitHub Actions CI/CD (dev auto-deploy, prod manual) |
| **DB File** | `/root/ems-team/input/database/crm.bak` (4.4 MB MSSQL backup) |

### Live Environments
| Environment | URL | Status |
|---|---|---|
| Dev API | https://ion-crm-api-development.up.railway.app | ✅ Live |
| Dev Frontend | https://ion-crm-frontend-development.up.railway.app | ✅ Live |
| Prod API | https://ion-crm-api-production.up.railway.app | ✅ Live |
| Prod Frontend | https://ion-crm-frontend-production.up.railway.app | ✅ Live |

### Solution Structure (scaffolded in Sprint 1)
```
IonCrm.sln
├── src/
│   ├── IonCrm.Domain/          → Entities, Enums, Domain Events
│   ├── IonCrm.Application/     → CQRS (MediatR), DTOs, Interfaces, Validators
│   ├── IonCrm.Infrastructure/  → EF Core, Neon PostgreSQL, Repos, Services
│   └── IonCrm.API/             → Controllers, Middleware, Program.cs
├── tests/
│   └── IonCrm.Tests/           → xUnit, Moq, WebApplicationFactory
└── frontend/                   → React 18, shadcn/ui, Tailwind, Zustand
```

---

## ⚠️ APPROVAL GATES (MANDATORY)

| Gate | Sprint | Status |
|------|--------|--------|
| Sprint planning approved | Before any code | ✅ Done |
| DB schema approved | Before migrations | ✅ Done |
| Sprint 0 completed | Analysis & Architecture | ✅ Done |
| Sprint 1 completed | Foundation + Auth + Deploy | ✅ Done |
| Sprint 2 completion | Customer Core Enhancement | 🔄 **ACTIVE — awaiting completion** |
| Sprint 3 start approval | Sync Service | ⏳ Blocked until Sprint 2 done |
| Sprint 4 start approval | Sales Pipeline | ⏳ Blocked |
| Sprint 5 start approval | Frontend Polish | ⏳ Blocked |
| Sprint 6 start approval | Hardening & Production | ⏳ Blocked |
| Production deploy approval | Final deploy | ⏳ Blocked |

---

# ✅ SPRINT 0 — Analysis & Architecture
**Status:** COMPLETED ✅
**Goal:** Analyze legacy MSSQL .bak, design PostgreSQL schema, define API contracts, scaffold solution structure. Zero production code — all output is documentation.
**Duration:** 2–3 days | **Agent:** Architect Agent | **Points:** 21

| Ticket | Title | Points | Status |
|--------|-------|--------|--------|
| ION-001 | [ARCHITECT] Analyze MSSQL .bak and extract legacy schema | 5 | ✅ Done |
| ION-002 | [ARCHITECT] Design PostgreSQL target schema | 8 | ✅ Done |
| ION-003 | [ARCHITECT] Define API contracts and OpenAPI spec | 5 | ✅ Done |
| ION-004 | [ARCHITECT] Define solution folder structure and scaffold plan | 3 | ✅ Done |

### ION-001 — [ARCHITECT] Analyze MSSQL .bak and extract legacy schema
**Points:** 5 | **Labels:** backend, migration, analysis
**Inputs:** `/root/ems-team/input/database/crm.bak` (4.4 MB MSSQL backup)
**Outputs:** `docs/legacy-schema.md`, `docs/migration-mapping.md`

Mount or restore the MSSQL backup using Docker (SQL Server Developer Edition — free). Extract all DDL. Document every table, column, FK, index, and constraint. Map legacy customer + contact history columns to new schema. Flag PII fields. Map MSSQL types to PostgreSQL equivalents.

**Acceptance Criteria:**
- [x] All legacy tables documented with estimated row counts
- [x] Column mapping old → new defined for all entities
- [x] PII fields flagged (email, phone, address)
- [x] MSSQL-specific types (NVARCHAR, UNIQUEIDENTIFIER, BIT) mapped to PostgreSQL equivalents
- [x] 639 customers and 892 contact history rows confirmed in source

---

### ION-002 — [ARCHITECT] Design PostgreSQL target schema
**Points:** 8 | **Labels:** backend, database, architecture
**Inputs:** `docs/migration-mapping.md`
**Outputs:** `docs/db-schema.md`, `src/IonCrm.Domain/Entities/`

Design full PostgreSQL schema for all entities. Every table: `Id (UUID)`, `CreatedAt`, `UpdatedAt`, `IsDeleted`. Every tenant table: `ProjectId`.

Tables:
- `Projects` — SaasCode, WebhookUrl, SyncIntervalMinutes
- `Users` — Email, PasswordHash, FullName, IsActive, IsSuperAdmin
- `UserProjectRoles` — UserId × ProjectId × Role (ProjectAdmin/SalesManager/SalesRep/Accounting)
- `RefreshTokens` — UserId, Token, ExpiresAt, Revoked
- `Customers` — ProjectId, FullName, Email, Phone, Company, ExternalId, SaasSource, Label, Status, LastContactDate, Notes
- `ContactHistories` — CustomerId, ProjectId, Type (Call/Email/Meeting/Note), Date, Notes, ContactResult
- `CustomerTasks` — CustomerId, ProjectId, AssignedUserId, Title, DueDate, IsCompleted
- `Pipelines` — CustomerId, ProjectId, PlannedDate, Notes, Status (Bekliyor/Tamamlandi/Iptal)
- `Opportunities` — CustomerId, ProjectId, OwnerId, Title, Value, Stage, ExpectedCloseDate
- `Notifications` — UserId, ProjectId, Type, Message, EntityType, EntityId, IsRead
- `SyncLogs` — ProjectId, SaasSource, Direction, Status, Payload(jsonb), RecordCount, AttemptCount, NextRetryAt
- `AuditLogs` — UserId, ProjectId, EntityType, EntityId, Action, OldValues(jsonb), NewValues(jsonb)

**Acceptance Criteria:**
- [x] All entities have base fields (Id, CreatedAt, UpdatedAt, IsDeleted)
- [x] All FK relationships fully defined
- [x] Indexes on ProjectId, Email, ExternalId, CreatedAt DESC
- [x] All enum values listed
- [x] Schema validated against legacy mapping

---

### ION-003 — [ARCHITECT] Define API contracts and OpenAPI spec
**Points:** 5 | **Labels:** backend, api, architecture
**Inputs:** `docs/db-schema.md`
**Outputs:** `docs/api-contracts.md`

Define all REST endpoints for: Auth, SuperAdmin, Customers, ContactHistory, Notes, Tasks, Pipelines, Opportunities, Sync, Dashboard, Reports. Specify request/response schemas, required roles, error codes. Define SaaS A and SaaS B inbound payload formats. Define outbound callback payload.

**Acceptance Criteria:**
- [x] All endpoints listed with method, path, auth requirement, allowed roles
- [x] Request/response body schemas defined
- [x] Standardized error codes documented (401, 403, 404, 422, 500)
- [x] Sync payload formats for SaaS A, SaaS B, and outbound callbacks defined
- [x] `ApiResponse<T>` wrapper format documented

---

### ION-004 — [ARCHITECT] Define solution folder structure and scaffold plan
**Points:** 3 | **Labels:** backend, devops, architecture
**Outputs:** `docs/project-structure.md`, `.env.example`

Document exact .NET solution folder structure, Clean Architecture layer responsibilities, DI registration strategy, frontend folder structure, CI/CD workflow layout.

**Acceptance Criteria:**
- [x] Every project layer mapped to Clean Architecture purpose
- [x] DI registration strategy described per layer
- [x] `.env.example` contains all required variable names (no real values)

**Deliverables:** `docs/legacy-schema.md`, `docs/migration-mapping.md`, `docs/db-schema.md`, `docs/api-contracts.md`, `docs/project-structure.md`, `.env.example`

---

# ✅ SPRINT 1 — Foundation
**Status:** COMPLETED ✅
**Goal:** Bootstrap solution, implement auth, deploy CI/CD to Railway, run initial data migration.
**Duration:** 3–4 days | **Agents:** Backend Agent, DevOps Agent, Frontend Agent | **Points:** 34

| Ticket | Title | Points | Status |
|--------|-------|--------|--------|
| ION-005 | [BACKEND] .NET Core 8 Clean Architecture solution scaffold | 5 | ✅ Done |
| ION-006 | [BACKEND] JWT Authentication (login, logout, refresh, /me) | 8 | ✅ Done |
| ION-007 | [BACKEND] Customers CRUD base + EF Core Neon migrations | 5 | ✅ Done |
| ION-008 | [BACKEND] ContactHistories base endpoints | 3 | ✅ Done |
| ION-009 | [BACKEND] CustomerTasks base endpoints | 3 | ✅ Done |
| ION-010 | [BACKEND] Sync endpoints stub (SaaS A, SaaS B inbound) | 3 | ✅ Done |
| ION-011 | [FRONTEND] React 18 app scaffold (shadcn/ui, dark mode, sidebar, login) | 5 | ✅ Done |
| ION-012 | [DEVOPS] Railway deploy (dev + prod) + GitHub Actions CI/CD | 5 | ✅ Done |
| ION-013 | [BACKEND] One-time data migration: 639 customers + 892 contact histories | 5 | ✅ Done |

### Key Sprint 1 Decisions Made:
- **Superadmin seed:** `admin@ioncrm.com` / `IonCrm2024`
- **JWT:** 15-min access token, 7-day refresh token
- **Roles:** SuperAdmin, ProjectAdmin, SalesManager, SalesRep, Accounting
- **Multi-tenancy:** `TenantMiddleware` extracts `ProjectId` from JWT claims; all queries filtered by `ProjectId`
- **Migration tool:** Python script using `pymssql` → read crm.bak → transform → POST to API
- **Database:** Neon PostgreSQL (connection pooler URL for prod)

---

# 🔄 SPRINT 2 — Customer Core Enhancement
**Status:** ACTIVE 🔄 (Backend IN PROGRESS)
**Goal:** Fix critical data-display bugs, add Label/Status classification, build Pipeline (call scheduling), implement Potansiyel→Müşteri atomic merge, deliver all-contact-histories view, add Dashboard analytics.
**Duration:** 3–4 days | **Agents:** Backend Agent, Frontend Agent | **Points:** 42

| Ticket | Title | Points | Agent | Status |
|--------|-------|--------|-------|--------|
| ION-014 | [BACKEND] Fix: Customer list API not returning records | 2 | Backend | ✅ Done |
| ION-015 | [FRONTEND] Fix: Customer add form renders blank | 2 | Frontend | ⏳ |
| ION-016 | [FRONTEND] Customer detail, edit, and delete pages | 5 | Frontend | ⏳ |
| ION-017 | [BACKEND] Customer Label system (YuksekPotansiyel → Kotu enum) | 3 | Backend | ✅ Done |
| ION-018 | [BACKEND] Customer Status system (Musteri/Potansiyel/Demo enum) | 3 | Backend | ⏳ |
| ION-019 | [FRONTEND] Label and Status badges + filter chips on customer list | 3 | Frontend | ⏳ |
| ION-020 | [BACKEND] Potansiyel → Müşteri atomic merge endpoint | 5 | Backend | ⏳ |
| ION-021 | [FRONTEND] "Müşteriye Bağla" merge UI (searchable modal) | 3 | Frontend | ⏳ |
| ION-022 | [BACKEND] ContactHistory ContactResult field + all-histories endpoint | 3 | Backend | ✅ Done |
| ION-023 | [FRONTEND] All Contact Histories page with filters | 3 | Frontend | ⏳ |
| ION-024 | [BACKEND] Pipeline (Arama Planlaması) CRUD + /contact action | 5 | Backend | ⏳ |
| ION-025 | [FRONTEND] Pipeline page + "Pipeline Ekle" button | 4 | Frontend | ⏳ |
| ION-026 | [BOTH] Dashboard — pipeline widget + analytics charts | 5 | Both | ⏳ |

### ION-014 — [BACKEND] Fix: Customer list API not returning records
**Points:** 2 | **Priority:** CRITICAL
Root cause: `TenantMiddleware` was not extracting `ProjectId` from JWT claims correctly, causing EF Core query to filter on `null ProjectId` → 0 results. Fix: parse `ProjectId` claim from JWT, inject into `ICurrentUserService`, apply `WHERE "ProjectId" = :projectId` in customer query. Verify 639 records returned.

**AC:** GET /api/v1/customers returns paginated 639 results for ProjectAdmin; SuperAdmin sees all projects.

---

### ION-015 — [FRONTEND] Fix: Customer add form renders blank
**Points:** 2 | **Priority:** CRITICAL
`Müşteri Ekle` page renders empty — no form fields. Fix React component: ensure form state initialized with `useForm()` (React Hook Form), all required fields rendered (FullName, Phone, Email, Company, Label, Status), React Query mutation wired to `POST /api/v1/customers`.

**AC:** Form displays all fields; submitting valid data navigates back to list; inline validation errors shown.

---

### ION-016 — [FRONTEND] Customer detail, edit, and delete pages
**Points:** 5
Three pages using shadcn/ui Card, Dialog, Form:
1. `/customers/:id` — name, contact info, label badge, status badge, contact history timeline, tasks list
2. `/customers/:id/edit` — pre-filled form, calls `PUT /api/v1/customers/:id`
3. Delete — confirmation Dialog with `DELETE /api/v1/customers/:id` (soft-delete)

**AC:** Detail shows all fields and related data; Edit pre-populates; Delete shows confirmation; back navigation works.

---

### ION-017 — [BACKEND] Customer Label system
**Points:** 3
```sql
ALTER TABLE "Customers" ADD "Label" integer NOT NULL DEFAULT 2;
-- Enum: 0=YuksekPotansiyel, 1=Potansiyel, 2=Notr, 3=Vasat, 4=Kotu
```
Add `CustomerLabel` enum to Domain. EF Core migration. Update `CreateCustomerCommand`, `UpdateCustomerCommand`, `CustomerDto`. Support `?label=` filter.

**AC:** Migration clean; label persisted on create/update; `?label=0` filter works.

---

### ION-018 — [BACKEND] Customer Status system
**Points:** 3
```sql
ALTER TABLE "Customers" ADD "Status" integer NOT NULL DEFAULT 1;
-- Enum: 0=Musteri, 1=Potansiyel, 2=Demo
```
EF Core migration. Update DTOs and commands. Support `?status=` filter.

**AC:** Status persisted; `?status=0` filter works; existing 639 customers default to Status=Potansiyel.

---

### ION-019 — [FRONTEND] Label and Status badges + filter chips
**Points:** 3
Customer list: colored badge for Label (YuksekPotansiyel=green, Kotu=red), status badge (Musteri=blue, Potansiyel=yellow, Demo=purple). Filter bar: Label dropdown + Status dropdown. Filters persist in URL query string.

**AC:** All 5 labels and 3 statuses render with distinct colors; filters update list in real time.

---

### ION-020 — [BACKEND] Potansiyel → Müşteri atomic merge endpoint
**Points:** 5
`POST /api/v1/customers/{sourceId}/merge` — Body: `{ "targetCustomerId": "uuid" }`

Atomic transaction:
1. Verify source.Status = Potansiyel (else 409)
2. Transfer all ContactHistories from source → target (update CustomerId)
3. Transfer all CustomerTasks from source → target
4. Soft-delete source customer (IsDeleted=true)
5. Return updated target customer DTO

Roles: ProjectAdmin, SalesManager.

**AC:** Transaction rolls back fully on any error; 409 if source not Potansiyel; all contact histories appear on target; source soft-deleted.

---

### ION-021 — [FRONTEND] "Müşteriye Bağla" merge UI
**Points:** 3
On customer detail (when Status=Potansiyel): "Müşteriye Bağla" button → searchable modal listing Musteri-status customers → confirmation: "Tüm görüşmeler [source] → [target] aktarılacak. Emin misiniz?" → call merge endpoint → redirect to target.

**AC:** Button only for Potansiyel; modal searchable; confirmation shows names; success redirects to target.

---

### ION-022 — [BACKEND] ContactHistory ContactResult field + all-histories endpoint
**Points:** 3
```sql
ALTER TABLE "ContactHistories" ADD "ContactResult" integer NULL;
-- Enum: 0=Olumlu, 1=Olumsuz, 2=BaskaTedarikci
```
New `GET /api/v1/contact-histories` — paginated, filters: `?from=&to=&type=&result=`.
`PUT /api/v1/contact-histories/{id}` — allow result update.

**AC:** Migration applied; GET returns all project contacts paginated; all filters work independently and combined.

---

### ION-023 — [FRONTEND] All Contact Histories page
**Points:** 3
`/contact-histories` route. Table: Müşteri Adı, Tarih, Tip (icon), Sonuç badge, Notlar (truncated). Filter panel (collapsible on mobile): date-range picker, type chips, result chips. Row click → customer detail. Pagination.

**AC:** Route accessible from sidebar; all 3 filter types functional; mobile layout collapses filter panel.

---

### ION-024 — [BACKEND] Pipeline CRUD + /contact action
**Points:** 5
```sql
CREATE TABLE "Pipelines" (
  "Id" uuid PRIMARY KEY,
  "CustomerId" uuid NOT NULL REFERENCES "Customers"("Id"),
  "ProjectId" uuid NOT NULL,
  "PlannedDate" timestamptz NOT NULL,
  "Notes" text,
  "Status" integer NOT NULL DEFAULT 0,  -- 0=Bekliyor, 1=Tamamlandi, 2=Iptal
  "CreatedAt" timestamptz NOT NULL,
  "UpdatedAt" timestamptz NOT NULL,
  "IsDeleted" boolean NOT NULL DEFAULT false
);
```
Endpoints: GET/POST/PUT/DELETE /api/v1/pipelines (GET default: next 7 days).
`POST /api/v1/pipelines/{id}/contact` — creates ContactHistory, atomically sets Pipeline.Status=Tamamlandi.

**AC:** EF Core migration clean; CRUD all working; /contact action is atomic; default date filter returns next 7 days only.

---

### ION-025 — [FRONTEND] Pipeline page + "Pipeline Ekle" button
**Points:** 4
Customer detail: "Pipeline Ekle" button → drawer with date picker + notes.
`/pipeline` route: list grouped by date. Each row: Müşteri adı, Tarih, Notlar, Durum badge, "Görüşme Kaydı Gir" button. Statuses: Bekliyor (orange), Tamamlandı (green), İptal (gray).

**AC:** Grouped by date; "Görüşme Kaydı Gir" creates contact and marks pipeline done; accessible from sidebar.

---

### ION-026 — [BOTH] Dashboard — pipeline widget + analytics charts
**Points:** 5
**Backend:** `GET /api/v1/dashboard/stats` (counts by status/label, contact history daily counts 30d), `GET /api/v1/dashboard/pipeline` (next 7-day pipeline for project).

**Frontend:** Pipeline widget (upcoming calls + inline "Görüşme Kaydı Gir"). Charts: Total Customers count card, Customers by Status (donut), Customers by Label (bar), Contact history volume last 30 days (line). Use recharts.

**AC:** Dashboard shows 4 metrics + pipeline widget + charts; all use real data; mobile responsive (charts stack vertically).

---

# ⏳ SPRINT 3 — Sync Service
**Status:** PLANNED ⏳ (starts after Sprint 2 human approval)
**Goal:** Build bidirectional sync engine — background pull from SaaS A & B every 15 minutes, instant push callbacks from CRM to SaaS on events. Full conflict resolution and retry logic.
**Duration:** 3–4 days | **Agents:** Backend Agent, DevOps Agent | **Points:** 39

| Ticket | Title | Points | Agent |
|--------|-------|--------|-------|
| ION-027 | [BACKEND] SaaS A inbound sync handler (IHostedService, 15-min) | 8 | Backend |
| ION-028 | [BACKEND] SaaS B inbound sync handler (separate handler) | 5 | Backend |
| ION-029 | [BACKEND] Instant CRM → SaaS callback service (domain events) | 8 | Backend |
| ION-030 | [BACKEND] Sync admin endpoints (SuperAdmin: logs, trigger, status) | 3 | Backend |
| ION-031 | [BACKEND] SyncLog entity + migration + repository | 3 | Backend |
| ION-032 | [FRONTEND] Sync status dashboard (SuperAdmin) | 5 | Frontend |
| ION-033 | [DEVOPS] Environment secrets for sync service (Railway + .env.example) | 2 | DevOps |
| ION-034 | [BACKEND] Idempotent upsert and conflict resolution | 5 | Backend |

### ION-027 — [BACKEND] SaaS A inbound sync handler
**Points:** 8
`SyncBackgroundService : IHostedService` fires every `SAAS_A_SYNC_INTERVAL_MINUTES` (default 15):
- Calls SaaS A REST API (configurable URL + API key)
- Upsert: if `ExternalId` exists for project → UPDATE; else → INSERT new Customer
- Project scoped: only syncs customers where `Project.SaasCode = "A"`
- Write to `SyncLogs`: SaasSource=A, Direction=Inbound, Status, RecordCount, Payload sample
- On failure: exponential backoff (2s, 5s, 10s), max 3 retries

Env vars: `SAAS_A_API_URL`, `SAAS_A_API_KEY`, `SAAS_A_SYNC_INTERVAL_MINUTES=15`

**AC:** Service starts on startup; runs every 15 min (configurable); new SaaS A customers appear in CRM; existing customers updated (not duplicated); SyncLog entry per run; retry logic with backoff.

---

### ION-028 — [BACKEND] SaaS B inbound sync handler
**Points:** 5
Same pattern as ION-027 but for SaaS B. Separate `SyncSaasBHandler : ISyncHandler`. Different payload schema handled via separate mapper. Both registered in DI, scheduled by same `SyncBackgroundService`. SyncLogs distinguish `SaasSource A vs B`.

Env vars: `SAAS_B_API_URL`, `SAAS_B_API_KEY`, `SAAS_B_SYNC_INTERVAL_MINUTES=15`

**AC:** SaaS B sync runs independently; different field mappings handled; SyncLogs distinguish source.

---

### ION-029 — [BACKEND] Instant CRM → SaaS callback service
**Points:** 8

**Trigger events (domain events):**
- `CustomerStatusChangedEvent` → notify SaaS
- `CustomerLabelChangedEvent` → notify SaaS
- `ContactHistoryCreatedEvent` → notify SaaS

**Implementation:**
- Domain events raised in entity mutating methods
- MediatR notification handler: `SaasCallbackHandler`
- HTTP POST to `Project.WebhookUrl` with standardized payload
- Record outbound attempt in `SyncLogs` (Direction=Outbound)
- Fire on background thread — does NOT block primary request
- Retry: 3 attempts with 2s, 5s, 10s backoff on 5xx errors

**Outbound payload format:**
```json
{
  "event": "customer.status_changed",
  "projectId": "uuid",
  "customerId": "uuid",
  "externalId": "string",
  "timestamp": "ISO8601",
  "data": { "oldStatus": "Potansiyel", "newStatus": "Musteri" }
}
```

**AC:** Status change triggers outbound POST within 200ms of primary request returning; SyncLog records attempt; failure does NOT affect primary CRM operation; retry logs each attempt.

---

### ION-030 — [BACKEND] Sync admin endpoints (SuperAdmin only)
**Points:** 3
- `GET /api/v1/admin/sync/logs` — paginated, `?projectId=&direction=&status=&from=&to=`
- `POST /api/v1/admin/sync/trigger/{projectId}` — manually trigger sync run
- `GET /api/v1/admin/sync/status` — last run time, success rate, pending retries per project

All routes: `[Authorize(Roles = "SuperAdmin")]`

**AC:** Non-SuperAdmin gets 403; manual trigger fires immediately; logs show 30-day history.

---

### ION-031 — [BACKEND] SyncLog entity + migration + repository
**Points:** 3
```
SyncLog: Id, ProjectId, SaasSource (A|B|Outbound), Direction (Inbound|Outbound),
Status (Success|Failed|Retrying), Payload (jsonb), RecordCount,
AttemptCount, NextRetryAt, Error (text), CreatedAt
```
`ISyncLogRepository` interface + EF Core implementation. Indexes: `ProjectId`, `CreatedAt DESC`, `Status`.

**AC:** Migration runs without errors; repository write/query tested; indexes confirmed on `\dt`.

---

### ION-032 — [FRONTEND] Sync status dashboard (SuperAdmin)
**Points:** 5
`/admin/sync` route (SuperAdmin-only, route guard):
- Cards: Last sync time per project, success/failure counts (last 24h)
- Sync log table: Project, Direction, Source, Status, RecordCount, CreatedAt
- "Sync Şimdi" button per project → `POST /admin/sync/trigger/:id` → toast feedback
- Auto-refresh every 60 seconds

**AC:** Hidden from non-SuperAdmin; manual trigger shows success toast; log table paginates; auto-refresh works.

---

### ION-033 — [DEVOPS] Environment secrets for sync service
**Points:** 2
Add to Railway dashboard (dev + prod): `SAAS_A_API_URL`, `SAAS_A_API_KEY`, `SAAS_B_API_URL`, `SAAS_B_API_KEY`, `SYNC_INTERVAL_MINUTES=15`. Update `.env.example`. Update GitHub Actions to pass secrets through.

**AC:** Dev environment can connect to SaaS A and B; no secrets in git; `.env.example` updated.

---

### ION-034 — [BACKEND] Idempotent upsert and conflict resolution
**Points:** 5
Edge cases:
- Duplicate sync run → no duplicate records (idempotent on `ExternalId + ProjectId`)
- Customer deleted in CRM, then resynced by SaaS → re-create (SaaS is source of truth for existence)
- SaaS A and SaaS B have overlapping ExternalIds → namespaced by SaasSource in conflict key

Use EF Core `AddOrUpdate` pattern with `ON CONFLICT (ExternalId, SaasSource, ProjectId) DO UPDATE`.

**AC:** Duplicate run = no duplicates; deleted-then-resynced = restored; cross-SaaS ExternalId collision doesn't corrupt data; integration tests for all 3 scenarios.

---

# ⏳ SPRINT 4 — Sales Pipeline & Performance Tracking
**Status:** PLANNED ⏳
**Goal:** Full Opportunities / Sales Pipeline feature set, user performance reports, notification system for sales teams.
**Duration:** 3–4 days | **Agents:** Backend Agent, Frontend Agent | **Points:** 37

| Ticket | Title | Points | Agent |
|--------|-------|--------|-------|
| ION-035 | [BACKEND] Opportunities entity + CRUD | 5 | Backend |
| ION-036 | [FRONTEND] Opportunities Kanban board (drag-and-drop) | 8 | Frontend |
| ION-037 | [BACKEND] Sales performance report endpoints | 5 | Backend |
| ION-038 | [FRONTEND] Sales performance & forecast charts | 5 | Frontend |
| ION-039 | [BACKEND] In-app notification system | 5 | Backend |
| ION-040 | [FRONTEND] Notification bell + dropdown | 3 | Frontend |
| ION-041 | [BACKEND] Task due-date reminder background job | 3 | Backend |
| ION-042 | [FRONTEND] Customer notes full implementation | 3 | Frontend |

### ION-035 — [BACKEND] Opportunities entity + CRUD
**Points:** 5
```sql
CREATE TABLE "Opportunities" (
  "Id" uuid PRIMARY KEY,
  "ProjectId" uuid NOT NULL,
  "CustomerId" uuid NOT NULL REFERENCES "Customers"("Id"),
  "OwnerId" uuid NOT NULL REFERENCES "Users"("Id"),
  "Title" varchar(200) NOT NULL,
  "Value" decimal(18,2) NOT NULL,
  "Stage" integer NOT NULL DEFAULT 0,
  "ExpectedCloseDate" date NOT NULL,
  "ActualCloseDate" date NULL,
  "Notes" text,
  "IsDeleted" boolean NOT NULL DEFAULT false,
  "CreatedAt" timestamptz NOT NULL,
  "UpdatedAt" timestamptz NOT NULL
);
-- Stage enum: Lead=0, Qualified=1, ProposalSent=2, Negotiation=3, Won=4, Lost=5
```
Endpoints: GET/POST/PUT/DELETE /api/v1/opportunities + `PUT /api/v1/opportunities/{id}/stage` (writes AuditLog).

**AC:** All CRUD working; stage change writes AuditLog entry; Value and ExpectedCloseDate required on create.

---

### ION-036 — [FRONTEND] Opportunities Kanban board
**Points:** 8
`/opportunities` route. Kanban columns: Lead, Qualified, Proposal Sent, Negotiation, Won, Lost. Cards show: customer name, title, value (TRY formatted), expected close date. Drag-and-drop between columns (calls `PUT /opportunities/{id}/stage`). Optimistic UI update. Summary bar: total pipeline value, count per stage. "Yeni Fırsat" button → create form.

**AC:** DnD works on desktop (mouse) and mobile (touch); stage move triggers API + optimistic update; total value updates; mobile scrolls horizontally.

---

### ION-037 — [BACKEND] Sales performance report endpoints
**Points:** 5
- `GET /api/v1/reports/sales-performance?userId=&from=&to=&projectId=` — per user: contact count, opps created, won value, conversion rate
- `GET /api/v1/reports/pipeline-forecast` — expected revenue by month (grouped by ExpectedCloseDate month, stage-weighted: Lead=10%, Qualified=30%, Proposal=50%, Negotiation=75%, Won=100%)

SuperAdmin sees all projects; ProjectAdmin sees own project only.

**AC:** Performance report returns per-user stats; forecast groups by month; role-based access enforced.

---

### ION-038 — [FRONTEND] Sales performance & forecast charts
**Points:** 5
`/reports` route:
1. **Sales Leaderboard** — table: Rep name, contacts made, opps created, won value (sortable)
2. **Pipeline Forecast** — bar chart by month showing weighted expected revenue
3. **Win Rate Trend** — line chart over last 6 months

Date range picker filters all charts simultaneously. Mobile: charts stack vertically.

---

### ION-039 — [BACKEND] In-app notification system
**Points:** 5
```sql
CREATE TABLE "Notifications" (
  "Id" uuid, "UserId" uuid, "ProjectId" uuid,
  "Type" integer, -- 0=TaskDue, 1=PipelineReminder, 2=OpportunityStageChange, 3=SyncFailure
  "Message" text, "EntityType" varchar(50), "EntityId" uuid,
  "IsRead" boolean DEFAULT false, "CreatedAt" timestamptz
);
```
Trigger events: Task due 24h before, Pipeline call scheduled today, Opportunity moved to Won/Lost, Sync failure.
Endpoints: `GET /api/v1/notifications`, `PUT /api/v1/notifications/{id}/read`, `PUT /api/v1/notifications/read-all`.

**AC:** Notifications created on all trigger events; mark-read endpoints working; only current user's notifications returned.

---

### ION-040 — [FRONTEND] Notification bell + dropdown
**Points:** 3
Top navbar bell icon: red badge with unread count. Click → dropdown list of recent notifications (icon, message, "X dakika önce", link to entity). "Tümünü okundu işaretle" button. Polls `GET /notifications` every 30 seconds.

**AC:** Badge count accurate (refreshes 30s); click navigates to entity; mark-all-read clears badge.

---

### ION-041 — [BACKEND] Task + pipeline reminder background job
**Points:** 3
`ReminderBackgroundService` runs hourly:
- Query tasks due in next 24h not yet notified → create Notification for assigned user
- Query pipeline entries scheduled today not yet notified → create Notification for sales rep
- Idempotent: same task/pipeline does NOT create duplicate notification on next run

**AC:** Tasks due tomorrow appear in notifications; no duplicate notifications; pipeline reminders fire day-of.

---

### ION-042 — [FRONTEND] Customer notes full implementation
**Points:** 3
Customer detail Notes tab: list all notes (newest first, author + timestamp + content). "Not Ekle" inline textarea (Ctrl+Enter or button). Edit own notes (inline). Delete own notes (confirm dialog). React Query invalidation on all mutations. Mobile: full-width cards.

**AC:** CRUD all functional; cannot edit/delete other user's note; mobile full-width.

---

# ⏳ SPRINT 5 — Frontend Polish & UX
**Status:** PLANNED ⏳
**Goal:** Complete all remaining screens, global search, user management, full mobile-first responsiveness, accessibility, and CSV export.
**Duration:** 3–4 days | **Agent:** Frontend Agent (+ Backend support) | **Points:** 38

| Ticket | Title | Points | Agent |
|--------|-------|--------|-------|
| ION-043 | [FRONTEND] Global search (Cmd+K command palette) | 5 | Frontend |
| ION-044 | [BACKEND] Global search endpoint | 3 | Backend |
| ION-045 | [FRONTEND] User management pages (SuperAdmin) | 5 | Frontend |
| ION-046 | [BACKEND] User management endpoints (SuperAdmin) | 3 | Backend |
| ION-047 | [FRONTEND] User profile & settings page | 3 | Frontend |
| ION-048 | [FRONTEND] Mobile navigation overhaul (bottom nav) | 5 | Frontend |
| ION-049 | [FRONTEND] Empty states, loading skeletons, error boundaries | 3 | Frontend |
| ION-050 | [FRONTEND] Advanced customer filters + CSV export | 5 | Frontend |
| ION-051 | [BACKEND] Customer CSV export endpoint | 2 | Backend |
| ION-052 | [FRONTEND] Accessibility & i18n prep (WCAG AA) | 4 | Frontend |

### ION-043 — [FRONTEND] Global search (Cmd+K command palette)
**Points:** 5
Command-palette style (shadcn/ui `Command` component). Keyboard: Cmd+K (Mac) / Ctrl+K (Windows). Searches: Customers (name, email, phone), Opportunities (title), Pipeline entries. Results grouped by type with icons. Click → navigate. Recent searches in localStorage. Debounced 200ms.

**AC:** Opens on Cmd+K; results ≤300ms; keyboard navigation (↑↓ Enter Esc); mobile: search icon in header.

---

### ION-044 — [BACKEND] Global search endpoint
**Points:** 3
`GET /api/v1/search?q={term}&limit=10`
PostgreSQL `ILIKE` across Customer (FullName, Email, Phone, Company) and Opportunity (Title).
Response: `[{ type: "customer"|"opportunity"|"pipeline", id, display, subtitle }]`
Project-scoped. Indexes: Customer(FullName), Customer(Email), Customer(Phone).

**AC:** Mixed-type results; response <100ms typical; minimum 2 characters required; SuperAdmin can pass `?projectId=`.

---

### ION-045 — [FRONTEND] User management pages (SuperAdmin)
**Points:** 5
`/admin/users`: user list (name, email, projects, role, last login, active toggle), "Kullanıcı Ekle" form, edit role/project assignment, "Şifre Sıfırla".
`/admin/projects`: project list (name, SaasCode, sync status, user count), "Proje Ekle" form (name, SaasCode, WebhookUrl).

**AC:** All CRUD calls correct endpoints; role assignment updates immediately; deactivated users cannot login.

---

### ION-046 — [BACKEND] User management endpoints (SuperAdmin)
**Points:** 3
- `GET/POST/PUT/DELETE /api/v1/admin/users` — CRUD with role assignment
- `POST /api/v1/admin/users/{id}/reset-password` — generate reset token
- `GET/POST/PUT /api/v1/admin/projects` — project CRUD

All routes: `[Authorize(Roles = "SuperAdmin")]`. Create user sends welcome email if SMTP configured. SaasCode must be unique.

---

### ION-047 — [FRONTEND] User profile & settings page
**Points:** 3
`/profile` (all users): display name, email, role, assigned projects. "Şifre Değiştir" section (current + new + confirm → `PUT /api/v1/auth/change-password`). Theme toggle (dark/light, persisted in localStorage).

---

### ION-048 — [FRONTEND] Mobile navigation overhaul
**Points:** 5
Replace sidebar with bottom navigation bar on mobile (≤768px). Bottom nav: Dashboard, Customers, Pipeline, Opportunities, More(…). "More" → sheet: Reports, Contact Histories, Admin, Profile. All tables → card-list view on mobile. All forms → full-screen modal on mobile. Swipe-to-delete on customer/pipeline lists.

**AC:** No horizontal scroll at 375px; bottom nav on all main routes; all touch targets ≥44×44px.

---

### ION-049 — [FRONTEND] Empty states, loading skeletons, error boundaries
**Points:** 3
Empty states with CTA on: Customer list, Pipeline, Opportunities. Skeleton UI on all tables/dashboards during load. Error boundaries per page (React 18 `ErrorBoundary`). Toast notifications (shadcn/ui `Toaster`) on all mutations.

**AC:** No blank screens during load; network error = user-friendly message; all CRUD → toast.

---

### ION-050 — [FRONTEND] Advanced customer filters + CSV export
**Points:** 5
Multi-select label filter (AND), multi-select status filter, company name filter, "Son Görüşme" date range. Sort by: Created, Last contact, Name. "CSV İndir" button → `GET /api/v1/customers/export?format=csv`. Filters persist in URL params.

**AC:** Combined filters work; CSV download correct; shareable URL with filters reloads correctly.

---

### ION-051 — [BACKEND] Customer CSV export endpoint
**Points:** 2
`GET /api/v1/customers/export?format=csv&label=&status=&company=`
Streams CSV (Content-Type: text/csv). Same filters as customer list. Columns: Id, FullName, Email, Phone, Company, Label, Status, CreatedAt, LastContactDate. File named `customers-{date}.csv`. Respects tenant scoping.

---

### ION-052 — [FRONTEND] Accessibility & i18n prep
**Points:** 4
Add `aria-label` to all icon-only buttons. Keyboard-navigable forms. `lang="tr"` on HTML root. Extract all Turkish strings to `locales/tr.ts`. Verify WCAG AA color contrast in both dark and light modes.

**AC:** No axe-core errors on main screens; all strings in locales file.

---

# ⏳ SPRINT 6 — Hardening, Testing & Production
**Status:** PLANNED ⏳
**Goal:** Validate migration completeness, write full test suite, security audit, production-ready deployment with monitoring.
**Duration:** 3–4 days | **Agents:** Backend Agent, DevOps Agent, Testing Agent | **Points:** 40

| Ticket | Title | Points | Agent |
|--------|-------|--------|-------|
| ION-053 | [BACKEND] Validate & harden one-time data migration | 5 | Backend |
| ION-054 | [TESTING] Backend unit test suite (Domain + Application, ≥80% coverage) | 8 | Testing |
| ION-055 | [TESTING] Backend integration test suite (Testcontainers) | 5 | Testing |
| ION-056 | [TESTING] Frontend E2E test suite (Playwright) | 5 | Testing |
| ION-057 | [DEVOPS] Security audit and hardening | 5 | DevOps |
| ION-058 | [DEVOPS] Production deployment and monitoring | 5 | DevOps |
| ION-059 | [DEVOPS] CI/CD pipeline enhancement (branch protection, PR checks) | 3 | DevOps |
| ION-060 | [BACKEND] Health check and readiness probe endpoint | 2 | Backend |

### ION-053 — [BACKEND] Validate & harden one-time data migration
**Points:** 5
Re-run migration script against dev DB; compare row counts (expected: 639 customers, 892 contacts). Verify: no "N/A" strings in phone/email, Turkish characters (ğ,ş,ı,ç) preserved (UTF-8), ExternalId mappings intact for SaaS sync deduplication. Write reconciliation SQL script. Document data quality issues.

**AC:** 639 customers correct; 892 contact histories linked correctly; no encoding issues; reconciliation script passes.

---

### ION-054 — [TESTING] Backend unit test suite
**Points:** 8
xUnit + Moq + FluentAssertions. Target: **≥80% coverage on Application layer**.
Cover: `CreateCustomerCommandHandler`, `MergeCustomerCommandHandler` (transaction logic, all edge cases), `SaasCallbackHandler` (fire-and-forget, retry), `SyncSaasAHandler` (upsert, SyncLog creation), all FluentValidation validators, domain entity invariants.

**AC:** `dotnet test` 0 failures; coverage ≥80% Application layer; edge cases: null payload, duplicate ExternalId, invalid ProjectId.

---

### ION-055 — [TESTING] Backend integration test suite
**Points:** 5
`WebApplicationFactory<Program>` + Testcontainers (PostgreSQL in Docker):
- Auth: login → token → protected endpoint
- Customer CRUD end-to-end with multi-tenant isolation
- Label/Status filter queries
- Merge workflow (Potansiyel → Müşteri atomic)
- Pipeline create → add contact → verify Status=Tamamlandi
- Sync endpoint: send SaaS payload → verify customer upserted
- **Multi-tenant isolation**: Project A user CANNOT see Project B data

**AC:** All integration tests pass against real PostgreSQL; multi-tenant isolation explicitly tested; tests run in CI.

---

### ION-056 — [TESTING] Frontend E2E test suite
**Points:** 5
Playwright tests (data-testid selectors for stability):
- Login → dashboard visible
- Add customer → appears in list
- Change customer label → badge updates
- Add pipeline entry → pipeline page shows it
- Mark pipeline complete → status badge changes
- Create opportunity → drag to "Qualified" stage
- SuperAdmin: sync logs page accessible

**AC:** All E2E pass against dev environment; tests run in CI; screenshots on failure uploaded as artifact.

---

### ION-057 — [DEVOPS] Security audit and hardening
**Points:** 5
- `dotnet-retire` / OWASP dependency check on NuGet packages
- `npm audit` on frontend
- Verify all endpoints have `[Authorize]` (no accidental open routes)
- Check git history for secrets (`trufflehog`)
- Rate limiting: `AspNetCoreRateLimit` (100 req/min per IP, configurable)
- CORS: allow only Railway frontend URL
- Security headers: HSTS, X-Frame-Options, CSP
- Verify JWT expiry/refresh flow
- Test SuperAdmin token cannot be forged by modifying claims

**AC:** Zero critical/high NuGet or npm vulnerabilities; no unprotected endpoints; rate limiting configured; security headers on all responses.

---

### ION-058 — [DEVOPS] Production deployment and monitoring
**Points:** 5
- Deploy to Railway prod (API + Frontend)
- Run EF Core migrations on prod Neon DB
- Verify HTTPS on prod domains
- Configure Railway health checks (ping `/health` every 30s)
- Structured logging: Serilog → Railway logs (JSON format)
- Set all prod env vars in Railway dashboard
- Verify SaaS A and B webhooks point to prod API URL

**AC:** Prod API responds at `https://ion-crm-api-production.up.railway.app/health`; prod frontend loads; all migrations applied; health check green.

---

### ION-059 — [DEVOPS] CI/CD pipeline enhancement
**Points:** 3
- Add test stage: `dotnet test` on every PR
- Add Playwright E2E stage: on merge to main
- Build status badge in README
- Branch protection: require PR review + all checks green before merge
- `dotnet format` check on PR
- Separate dev (auto) and prod (manual approval) deploy jobs

**AC:** PRs blocked from merging if tests fail; prod deploy requires GitHub approval; badge in README.

---

### ION-060 — [BACKEND] Health check and readiness probe
**Points:** 2
`GET /health` — JSON: overall Healthy/Degraded/Unhealthy + component breakdown:
- Neon DB connectivity (`Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`)
- SaaS A reachability (HTTP GET ping)
- SaaS B reachability
- Sync lag: Warning if last sync >30 min

`GET /health/ready` — simple 200 for Railway readiness probe.

**AC:** `/health` returns JSON with component statuses; DB failure → Unhealthy; sync lag >30min → Degraded.

---

## 📊 COMPLETE SPRINT SUMMARY TABLE

| Sprint | Name | Status | Stories | Points | Duration | Agents |
|--------|------|--------|---------|--------|----------|--------|
| Sprint 0 | Analysis & Architecture | ✅ DONE | 4 | 21 | 2–3 days | Architect |
| Sprint 1 | Foundation | ✅ DONE | 9 | 34 | 3–4 days | Backend, Frontend, DevOps |
| Sprint 2 | Customer Core Enhancement | 🔄 ACTIVE | 13 | 42 | 3–4 days | Backend, Frontend |
| Sprint 3 | Sync Service | ⏳ PLANNED | 8 | 39 | 3–4 days | Backend, DevOps |
| Sprint 4 | Sales Pipeline & Performance | ⏳ PLANNED | 8 | 37 | 3–4 days | Backend, Frontend |
| Sprint 5 | Frontend Polish & UX | ⏳ PLANNED | 10 | 38 | 3–4 days | Frontend, Backend |
| Sprint 6 | Hardening, Testing & Production | ⏳ PLANNED | 8 | 40 | 3–4 days | Backend, DevOps, Testing |
| **TOTAL** | | | **60 stories** | **251 pts** | **~22–27 days** | |

---

## 🚧 OPEN RISKS & DEPENDENCIES

| Risk | Affects Sprint | Mitigation |
|------|---------------|------------|
| SaaS A/B API credentials not yet provided | Sprint 3 | Use configurable mock server for dev; real creds injected via Railway env vars |
| SaaS A/B payload schemas unknown | Sprint 3 | Design handlers as schema-driven JSON config; update when creds received |
| Neon DB connection pool limits (free tier: 10 connections) | Sprint 3+ | Use Neon pooler URL; set `MaxPoolSize=5` in EF Core connection string |
| Playwright tests flaky on Railway CI | Sprint 6 | Use `data-testid` selectors; add retry on network failures; run with `--workers=1` |
| Production migration needs brief downtime | Sprint 6 | Schedule maintenance window; all migrations are additive only (no destructive DDL) |
| Turkish character encoding in crm.bak | Sprint 6 | UTF-8 encoding verified in Sprint 0/1; reconciliation script in Sprint 6 validates |
| SaaS A/B may send malformed payloads | Sprint 3 | Defensive deserialization; log malformed payloads to SyncLog; skip and continue |

---

## 🔑 KEY CREDENTIALS & ENDPOINTS

| Item | Value |
|------|-------|
| SuperAdmin login | admin@ioncrm.com / IonCrm2024 |
| Dev API | https://ion-crm-api-development.up.railway.app |
| Dev Frontend | https://ion-crm-frontend-development.up.railway.app |
| Prod API | https://ion-crm-api-production.up.railway.app |
| Prod Frontend | https://ion-crm-frontend-production.up.railway.app |
| GitHub repo | https://github.com/ionivasoftware/ionivacrm |
| DB | Neon PostgreSQL (connection string in Railway env vars) |
| Monitor CI | `gh run watch --repo ionivasoftware/ionivacrm` |

---

*Last updated: 2026-03-26 by Orchestrator Agent (Claude PM)*
