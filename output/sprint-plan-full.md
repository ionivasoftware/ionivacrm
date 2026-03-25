# ION CRM — Master Sprint Plan
> **Generated / Refreshed:** 2026-03-25
> **Orchestrator:** Claude (Product Manager Agent)
> **Stack:** ASP.NET Core 8 · Clean Architecture · PostgreSQL (Neon) · React 18 · shadcn/ui · Railway
> **Repo:** https://github.com/ionivasoftware/ionivacrm

---

## 📋 PROJECT OVERVIEW

| Property | Value |
|----------|-------|
| **Architecture** | Clean Architecture: Domain → Application → Infrastructure → API |
| **Auth** | JWT Bearer (15-min access / 7-day refresh), RBAC + Project-scoped roles |
| **Multi-tenancy** | SuperAdmin (all data) + Project-scoped: ProjectAdmin, SalesManager, SalesRep, Accounting |
| **Sync Strategy** | SaaS A & B → CRM every 15 min (background pull); CRM → SaaS instant HTTP push (subscriptions, status changes) |
| **Legacy Migration** | One-time MSSQL .bak → PostgreSQL (EMS.Companies + PotentialCustomers + CustomerInterviews) |
| **Frontend** | React 18, shadcn/ui, Zustand, React Query, dark mode default, Turkish language, mobile-first |
| **Deploy** | Railway via GitHub Actions CI/CD — Dev + Prod environments |
| **DB** | Neon PostgreSQL (dev: ep-royal-grass / prod: ep-purple-sound) |
| **Old DB** | `/input/database/crm.bak` — 4.4 MB MSSQL backup — analyzed ✅ |

---

## 🗂️ LEGACY DATABASE — MIGRATION ANALYSIS SUMMARY

**Source:** `crm.bak` contains two logical databases: **IONCRM** and **EMS**

| Old Table | → New Table | Status |
|-----------|-------------|--------|
| `EMS.dbo.Companies` (Companies/customers) | `Customers` (Status=Musteri) | ✅ Migrated — 639 records |
| `dbo.PotentialCustomers` (Leads) | `Customers` (Status=Potansiyel) | ✅ Migrated |
| `dbo.CustomerInterviews` (Contact history) | `ContactHistories` | ✅ Migrated — 892 records |
| `dbo.AppointedInterviews` (Appointments) | `ContactHistories` (historical) | ✅ Migrated |
| `dbo.Users` | ❌ Not migrated — rebuilt fresh with JWT/RBAC |
| `dbo.InterviewRejectStatus` | ❌ Not migrated — replaced by enums |

**Key mapping rules applied:**
- Old `Adress` (typo) → new `address`
- `isPotantialCustomer` bit → `CustomerStatus` enum (Musteri=0, Potansiyel=1, Demo=2)
- Old int IDs → new GUIDs; old ID stored in `LegacyId` for traceability
- `CustomerInterviews.Type` → `ContactType` enum (Call, Email, Meeting, Note)
- Both Companies and PotentialCustomers merged into single `Customers` table

---

## ⚠️ MANDATORY APPROVAL GATES

| Gate | Sprint | Status |
|------|--------|--------|
| Sprint plan reviewed | Pre-Sprint 0 | ✅ Approved |
| DB schema design reviewed | Sprint 0 | ✅ Approved |
| Sprint 0 complete | Sprint 0 → 1 | ✅ Approved |
| Sprint 1 complete | Sprint 1 → 2 | ✅ Approved |
| **Sprint 2 complete** | **Sprint 2 → 3** | **🔄 AWAITING APPROVAL** |
| Sprint 3 complete | Sprint 3 → 4 | ⏳ Pending |
| Sprint 4 complete | Sprint 4 → 5 | ⏳ Pending |
| Sprint 5 complete | Sprint 5 → 6 | ⏳ Pending |
| Production deployment | Sprint 6 | ⏳ Pending |

---

---

# ✅ SPRINT 0 — Analysis & Architecture
**Status:** COMPLETED
**Goal:** Analyze legacy MSSQL .bak, design PostgreSQL schema, define API contracts, scaffold solution structure.
**Duration:** 2–3 days | **Agent:** Architect Agent | **Story Points:** 21

| Ticket | Title | Points | Status |
|--------|-------|--------|--------|
| ION-001 | [ARCHITECT] Analyze MSSQL .bak and extract legacy schema | 5 | ✅ Done |
| ION-002 | [ARCHITECT] Design PostgreSQL target schema | 8 | ✅ Done |
| ION-003 | [ARCHITECT] Define API contracts and OpenAPI spec | 5 | ✅ Done |
| ION-004 | [ARCHITECT] Define solution folder structure and scaffold plan | 3 | ✅ Done |

**Deliverables:** `db_analysis.md`, DB schema (Customers, ContactHistories, Tasks, Users, Projects, Pipelines, Opportunities, SyncLogs, Notifications), API contracts spec, solution folder structure blueprint, `.env.example`

---

# ✅ SPRINT 1 — Foundation
**Status:** COMPLETED
**Goal:** Bootstrap solution, implement auth, base entities, initial data migration, CI/CD, and Railway deploy.
**Duration:** 3–4 days | **Agents:** Backend, Frontend, DevOps | **Story Points:** 34

| Ticket | Title | Points | Status |
|--------|-------|--------|--------|
| ION-005 | [BACKEND] .NET Core 8 Clean Architecture solution scaffold | 5 | ✅ Done |
| ION-006 | [BACKEND] JWT Authentication (login, logout, refresh, /me) | 8 | ✅ Done |
| ION-007 | [BACKEND] Customers CRUD base + EF Core Neon migrations | 5 | ✅ Done |
| ION-008 | [BACKEND] ContactHistories base endpoints | 3 | ✅ Done |
| ION-009 | [BACKEND] CustomerTasks base endpoints | 3 | ✅ Done |
| ION-010 | [BACKEND] Sync endpoints stub (SaaS A & B inbound) | 3 | ✅ Done |
| ION-011 | [FRONTEND] React 18 scaffold (shadcn/ui, Tailwind, dark mode, sidebar, login) | 5 | ✅ Done |
| ION-012 | [DEVOPS] Railway deploy (dev + prod), GitHub Actions CI/CD | 5 | ✅ Done |
| ION-013 | [BACKEND] One-time data migration: 639 customers + 892 contact histories | 5 | ✅ Done |

**Live Environments:**
- Dev API: https://ion-crm-api-development.up.railway.app
- Dev Frontend: https://ion-crm-frontend-development.up.railway.app
- Prod API: https://ion-crm-api-production.up.railway.app
- Prod Frontend: https://ion-crm-frontend-production.up.railway.app

---

# 🔄 SPRINT 2 — Customer Core Enhancement
**Status:** ACTIVE
**Goal:** Fix critical data-display bugs, add Label/Status classification, build Pipeline (call scheduling) system, Potansiyel→Müşteri merge workflow, all-contact-histories view, and Dashboard analytics.
**Duration:** 3–4 days | **Agents:** Backend Agent, Frontend Agent | **Story Points:** 42

---

### 🎫 ION-014
**Title:** [BACKEND] Fix: Customer list API not returning records
**Sprint:** Sprint 2 | **Points:** 2 | **Labels:** backend, bug | **Priority:** CRITICAL
**Description:**
Customers screen shows 0 records despite 639 customers in Neon DB.
- Diagnose `GET /api/v1/customers` — likely a missing `ProjectId` filter returning 0 for null tenant context
- Verify JWT claims include `ProjectId` and are correctly parsed in middleware
- Confirm EF Core query doesn't accidentally filter on null `IsDeleted`
- Return paginated 639 customers after fix

**Acceptance Criteria:**
- [ ] `GET /api/v1/customers` returns paginated results (default page 1, size 20)
- [ ] All 639 existing records visible to authenticated ProjectAdmin
- [ ] SuperAdmin sees records across all projects

---

### 🎫 ION-015
**Title:** [FRONTEND] Fix: Customer add form renders blank
**Sprint:** Sprint 2 | **Points:** 2 | **Labels:** frontend, bug | **Priority:** CRITICAL
**Description:**
"Müşteri Ekle" page renders empty (no form fields). Diagnose and fix:
- Check React component for missing form state initialization
- Verify React Query mutation for `POST /api/v1/customers` is wired to submit handler
- Ensure required fields (FullName, Phone, Email, Company) are present
- Add form validation with inline error messages

**Acceptance Criteria:**
- [ ] Form displays all required fields on load
- [ ] Submitting a valid customer navigates back to list
- [ ] Validation errors shown inline

---

### 🎫 ION-016
**Title:** [FRONTEND] Add customer detail, edit, and delete pages
**Sprint:** Sprint 2 | **Points:** 5 | **Labels:** frontend | **Priority:** HIGH
**Description:**
Three missing screens:
1. **Detail page** (`/customers/:id`) — name, contact info, label, status, contact history list, tasks list
2. **Edit page** (`/customers/:id/edit`) — pre-filled form, `PUT /api/v1/customers/:id`
3. **Delete** — confirmation dialog with `DELETE /api/v1/customers/:id` (soft delete)

Use shadcn/ui Card, Dialog, and Form components. Mobile-first layout.

**Acceptance Criteria:**
- [ ] Detail page shows all customer fields and related contact history
- [ ] Edit form pre-populates existing values
- [ ] Delete shows confirmation dialog; customer removed from list after confirmation
- [ ] Back navigation works correctly

---

### 🎫 ION-017
**Title:** [BACKEND] Customer Label system (YuksekPotansiyel / Potansiyel / Notr / Vasat / Kotu)
**Sprint:** Sprint 2 | **Points:** 3 | **Labels:** backend | **Priority:** HIGH
**DB Migration:**
```sql
ALTER TABLE "Customers" ADD "Label" integer NOT NULL DEFAULT 2;
-- 0=YuksekPotansiyel, 1=Potansiyel, 2=Notr, 3=Vasat, 4=Kotu
```
**Description:**
- Add `CustomerLabel` enum to Domain layer
- Add `Label` property to `Customer` entity
- Create and run EF Core migration
- Update `CreateCustomerCommand`, `UpdateCustomerCommand`, `CustomerDto`
- Support `?label=` filter on `GET /api/v1/customers`

**Acceptance Criteria:**
- [ ] Migration runs cleanly on dev Neon DB
- [ ] Label field persisted on create/update
- [ ] `GET /customers?label=0` filter works

---

### 🎫 ION-018
**Title:** [BACKEND] Customer Status system (Musteri / Potansiyel / Demo)
**Sprint:** Sprint 2 | **Points:** 3 | **Labels:** backend | **Priority:** HIGH
**DB Migration:**
```sql
ALTER TABLE "Customers" ADD "Status" integer NOT NULL DEFAULT 1;
-- 0=Musteri, 1=Potansiyel, 2=Demo
```
**Description:**
- Add `CustomerStatus` enum to Domain
- EF Core migration
- Update DTOs and commands
- Support `?status=` filter on `GET /api/v1/customers`

**Acceptance Criteria:**
- [ ] Status field persisted
- [ ] `GET /customers?status=0` filter works
- [ ] Existing 639 customers default to Status=Potansiyel (1)

---

### 🎫 ION-019
**Title:** [FRONTEND] Label and Status badges + filters on customer list
**Sprint:** Sprint 2 | **Points:** 3 | **Labels:** frontend | **Priority:** HIGH | **Depends on:** ION-017, ION-018
**Description:**
- Colored badge for Label (YuksekPotansiyel=emerald, Potansiyel=blue, Notr=slate, Vasat=orange, Kotu=red)
- Status badge (Musteri=blue, Potansiyel=yellow, Demo=purple)
- Filter bar: Label dropdown + Status dropdown (chips style)
- Filters call `GET /api/v1/customers?label=X&status=Y`
- Filter state persisted in URL query string

**Acceptance Criteria:**
- [ ] All 5 labels render with distinct colors
- [ ] All 3 statuses render with distinct colors
- [ ] Filter chips update list in real time
- [ ] URL state preserved on page refresh

---

### 🎫 ION-020
**Title:** [BACKEND] Potansiyel → Müşteri merge endpoint
**Sprint:** Sprint 2 | **Points:** 5 | **Labels:** backend | **Priority:** HIGH
**Description:**
`POST /api/v1/customers/{sourceId}/merge` — Body: `{ "targetCustomerId": "uuid" }`

Business rules (atomic transaction):
1. Verify source has `Status = Potansiyel` → else 409
2. Transfer all `ContactHistories` from source → target
3. Transfer all `Tasks` from source → target
4. Soft-delete source customer (`IsDeleted = true`)
5. Return updated target customer DTO

Roles allowed: ProjectAdmin, SalesManager

**Acceptance Criteria:**
- [ ] Transaction rolls back fully on any error
- [ ] Source customer soft-deleted after merge
- [ ] All contact histories appear on target customer
- [ ] Returns 409 if source is not Potansiyel status

---

### 🎫 ION-021
**Title:** [FRONTEND] "Müşteriye Bağla" merge UI
**Sprint:** Sprint 2 | **Points:** 3 | **Labels:** frontend | **Priority:** MEDIUM | **Depends on:** ION-020
**Description:**
On customer detail page, when `Status = Potansiyel`:
- Show "Müşteriye Bağla" button
- Clicking opens searchable modal listing active Musteri-status customers
- User selects target → confirmation dialog with source and target names
- On confirm: call `POST /customers/{id}/merge`, redirect to target customer

**Acceptance Criteria:**
- [ ] Button only visible for Potansiyel-status customers
- [ ] Modal is searchable
- [ ] Confirmation dialog shows both names
- [ ] Success redirects to target customer detail

---

### 🎫 ION-022
**Title:** [BACKEND] Contact History result field + all-histories endpoint
**Sprint:** Sprint 2 | **Points:** 3 | **Labels:** backend | **Priority:** MEDIUM
**DB Migration:**
```sql
ALTER TABLE "ContactHistories" ADD "ContactResult" integer NULL;
-- 0=Olumlu, 1=Olumsuz, 2=BaskaTedarikci
```
**Description:**
- Add `ContactResult` enum and field to entity + DTO
- EF Core migration
- `GET /api/v1/contact-histories` — paginated, with filters: `?from=`, `?to=`, `?type=`, `?result=`
- `PUT /api/v1/contact-histories/{id}` — allow result update

**Acceptance Criteria:**
- [ ] Migration applied
- [ ] GET /contact-histories returns all contacts across project, paginated
- [ ] All three filters work independently and combined

---

### 🎫 ION-023
**Title:** [FRONTEND] All Contact Histories page
**Sprint:** Sprint 2 | **Points:** 3 | **Labels:** frontend | **Priority:** MEDIUM | **Depends on:** ION-022
**Description:**
New route `/contact-histories`:
- Table: Müşteri Adı, Tarih, Tip (icon), Sonuç badge, Notlar (truncated)
- Filter panel (collapsible on mobile): date-range picker, type chips, result chips
- Row click → navigate to customer detail
- Pagination controls

**Acceptance Criteria:**
- [ ] Route accessible from sidebar navigation
- [ ] All three filter types functional
- [ ] Mobile layout collapses filter panel

---

### 🎫 ION-024
**Title:** [BACKEND] Pipeline (Arama Planlaması) CRUD
**Sprint:** Sprint 2 | **Points:** 5 | **Labels:** backend | **Priority:** HIGH
**DB Migration:**
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
**Endpoints:**
- `GET /api/v1/pipelines` — `?from=&to=&status=`; defaults to next 7 days
- `POST /api/v1/pipelines`
- `PUT /api/v1/pipelines/{id}`
- `DELETE /api/v1/pipelines/{id}` (soft delete)
- `POST /api/v1/pipelines/{id}/contact` — create ContactHistory, set pipeline Status=Tamamlandi atomically

**Acceptance Criteria:**
- [ ] EF Core migration runs
- [ ] All CRUD endpoints working
- [ ] `/contact` sub-action creates CH and marks pipeline complete atomically
- [ ] Default date filter returns only next 7 days

---

### 🎫 ION-025
**Title:** [FRONTEND] Pipeline page + "Pipeline Ekle" button
**Sprint:** Sprint 2 | **Points:** 4 | **Labels:** frontend | **Priority:** HIGH | **Depends on:** ION-024
**Description:**
1. **Customer detail page** — "Pipeline Ekle" button → drawer/modal: date picker, notes field
2. **`/pipeline` route** — list grouped by date:
   - Each row: Müşteri adı, Tarih, Notlar, Durum badge, action buttons
   - "Görüşme Kaydı Gir" button → quick contact-log form
   - Edit / Delete actions
3. Status badges: Bekliyor (orange), Tamamlandı (green), İptal (gray)

**Acceptance Criteria:**
- [ ] Pipeline items grouped by date
- [ ] "Görüşme Kaydı Gir" creates contact and marks pipeline done
- [ ] Accessible from sidebar

---

### 🎫 ION-026
**Title:** [FRONTEND+BACKEND] Dashboard — Pipeline widget + analytics charts
**Sprint:** Sprint 2 | **Points:** 5 | **Labels:** frontend, backend | **Priority:** MEDIUM | **Depends on:** ION-017, ION-018, ION-024
**Description:**

**Backend:**
- `GET /api/v1/dashboard/stats` — counts by status, by label, contact history daily counts (last 30 days)
- `GET /api/v1/dashboard/pipeline` — next 7-day pipeline for current project

**Frontend — enhance existing dashboard:**
- 4 metric cards (Total Customers, by Status, by Label, 30-day contacts)
- Customers by Status: donut chart
- Customers by Label: bar chart
- Contact history volume (last 30 days): line chart
- Pipeline Widget: upcoming 7-day calls sorted by date, with inline "Görüşme Kaydı Gir"

Use recharts library. All charts mobile-responsive (stack vertically).

**Acceptance Criteria:**
- [ ] Dashboard shows 4 metrics with real data
- [ ] Pipeline widget lists upcoming calls
- [ ] Charts render with real data
- [ ] Mobile responsive (charts stack vertically)

---

## Sprint 2 Execution Order

```
Phase 1 (Parallel — unblock the app):
  Backend: ION-014 (fix customer list API)
  Frontend: ION-015 (fix add form) + ION-016 (detail/edit/delete pages)

Phase 2 (Backend first, then Frontend):
  Backend: ION-017 + ION-018 → Frontend: ION-019
  Backend: ION-020 → Frontend: ION-021
  Backend: ION-022 → Frontend: ION-023
  Backend: ION-024 → Frontend: ION-025

Phase 3 (Dashboard — requires all above):
  Backend + Frontend: ION-026
```

---

---

# ⏳ SPRINT 3 — Sync Service
**Status:** PLANNED (blocked until Sprint 2 approved)
**Goal:** Build robust bidirectional sync — background pull from SaaS A & B every 15 minutes, and instant push callbacks from CRM to SaaS on subscription/status changes.
**Duration:** 3–4 days | **Agents:** Backend Agent, DevOps Agent | **Story Points:** 39

---

### 🎫 ION-027
**Title:** [BACKEND] SaaS A inbound sync handler (pull every 15 min)
**Sprint:** Sprint 3 | **Points:** 8 | **Labels:** backend, sync
**Description:**
Implement `IHostedService` background worker (`SyncBackgroundService`) firing every 15 minutes:
- Calls SaaS A REST API for new/updated customers
- Upsert logic: `ExternalId` exists → update; else → insert new Customer
- Project scoped: only syncs customers for `Project.SaasCode = "A"`
- Writes result to `SyncLogs` (Success/Failed, record count, payload sample)
- Exponential backoff on failure: 2s, 5s, 10s — max 3 retries

**Environment variables:**
```
SAAS_A_API_URL=
SAAS_A_API_KEY=
SAAS_A_SYNC_INTERVAL_MINUTES=15
```

**Acceptance Criteria:**
- [ ] Background service starts on application startup
- [ ] Runs every 15 minutes (configurable via env var)
- [ ] New customers from SaaS A appear in CRM after sync
- [ ] Updated customers (by ExternalId) are updated, not duplicated
- [ ] SyncLog entry written for every run (success or failure)
- [ ] Failed run triggers retry with backoff

---

### 🎫 ION-028
**Title:** [BACKEND] SaaS B inbound sync handler (pull every 15 min)
**Sprint:** Sprint 3 | **Points:** 5 | **Labels:** backend, sync
**Description:**
Same pattern as ION-027 but for SaaS B:
- Separate `SyncSaasBHandler` implementing `ISyncHandler`
- May have different payload schema than SaaS A
- Both handlers registered in DI, scheduled by same `SyncBackgroundService`
- Project scoped to `Project.SaasCode = "B"`

**Environment variables:**
```
SAAS_B_API_URL=
SAAS_B_API_KEY=
SAAS_B_SYNC_INTERVAL_MINUTES=15
```

**Acceptance Criteria:**
- [ ] SaaS B sync runs independently of SaaS A
- [ ] Different field mappings handled gracefully
- [ ] SyncLog entries distinguish SaasSource A vs B

---

### 🎫 ION-029
**Title:** [BACKEND] Instant CRM → SaaS callback service
**Sprint:** Sprint 3 | **Points:** 8 | **Labels:** backend, sync
**Description:**
When CRM data changes, instantly push to SaaS via HTTP POST to `Project.WebhookUrl`:

**Trigger events:**
- `Customer.Status` changed → `CustomerStatusChangedEvent`
- `Customer.Label` changed → `CustomerLabelChangedEvent`
- New `ContactHistory` created → `ContactHistoryCreatedEvent`
- New subscription/opportunity created → `OpportunityCreatedEvent`

**Implementation:**
- Domain events dispatched via MediatR
- `SaasCallbackHandler` handles all outbound HTTP calls
- Fire-and-forget — does NOT block the original request (background thread)
- Record attempt in `SyncLogs` (Direction=Outbound)
- Retry: up to 3 times on 5xx with 2s → 5s → 10s backoff

**Acceptance Criteria:**
- [ ] Status change triggers outbound POST within 200ms of request returning
- [ ] SyncLog records outbound attempt with status
- [ ] Failure does NOT affect the primary CRM operation
- [ ] Retry logic logs each attempt

---

### 🎫 ION-030
**Title:** [BACKEND] Sync admin endpoints (SuperAdmin only)
**Sprint:** Sprint 3 | **Points:** 3 | **Labels:** backend, sync
**Description:**
- `GET /api/v1/admin/sync/logs` — paginated sync logs, filterable by project, direction, status
- `POST /api/v1/admin/sync/trigger/{projectId}` — manually trigger a sync run
- `GET /api/v1/admin/sync/status` — current sync health (last run time, success rate, pending retries)

All routes: `[Authorize(Roles = "SuperAdmin")]`

**Acceptance Criteria:**
- [ ] Non-SuperAdmin gets 403
- [ ] Manual trigger fires sync immediately for given project
- [ ] Sync logs show 30-day history

---

### 🎫 ION-031
**Title:** [BACKEND] SyncLog entity + EF Core migration + repository
**Sprint:** Sprint 3 | **Points:** 3 | **Labels:** backend, database
**Description:**
New `SyncLogs` table:
```
Id (uuid), ProjectId (uuid), SaasSource (A|B), Direction (Inbound|Outbound),
Status (Success|Failed|Retrying), Payload (jsonb), RecordCount (int),
AttemptCount (int), NextRetryAt (timestamptz), Error (text), CreatedAt (timestamptz)
```
- EF Core migration
- `ISyncLogRepository` interface + EF implementation
- Indexes on: `ProjectId`, `CreatedAt DESC`, `Status`

**Acceptance Criteria:**
- [ ] Migration runs without errors
- [ ] Repository write/query tested

---

### 🎫 ION-032
**Title:** [FRONTEND] Sync status dashboard (SuperAdmin)
**Sprint:** Sprint 3 | **Points:** 5 | **Labels:** frontend, sync
**Description:**
SuperAdmin-only section:
- `/admin/sync` route (route-guarded)
- Summary cards: Last sync time per project, success/failure counts (last 24h)
- Sync log table: Project, Direction, Source, Status, RecordCount, Time
- "Sync Şimdi" button per project → calls `POST /admin/sync/trigger/:id`
- Auto-refresh every 60 seconds

**Acceptance Criteria:**
- [ ] Hidden from non-SuperAdmin users
- [ ] Manual trigger shows success toast
- [ ] Log table paginates

---

### 🎫 ION-033
**Title:** [DEVOPS] Environment secrets for sync service
**Sprint:** Sprint 3 | **Points:** 2 | **Labels:** devops
**Description:**
Add required env vars to Railway (dev + prod):
- `SAAS_A_API_URL`, `SAAS_A_API_KEY`
- `SAAS_B_API_URL`, `SAAS_B_API_KEY`
- `SYNC_INTERVAL_MINUTES=15`
- Document all new vars in `.env.example`
- Update GitHub Actions workflow to pass secrets through

**Acceptance Criteria:**
- [ ] Dev environment syncs to SaaS A and B
- [ ] Secrets not committed to git
- [ ] `.env.example` updated

---

### 🎫 ION-034
**Title:** [BACKEND] Idempotent upsert and conflict resolution for sync
**Sprint:** Sprint 3 | **Points:** 5 | **Labels:** backend, sync
**Description:**
Handle edge cases:
- SaaS sends customer manually deleted in CRM → re-create (SaaS is source of truth for existence)
- Same ExternalId in two SaaS A payloads simultaneously → EF upsert / `ON CONFLICT` pattern
- SaaS B and SaaS A have overlapping ExternalIds → namespaced by `SaasSource` in key
- Write integration test scenarios for each case

**Acceptance Criteria:**
- [ ] Duplicate sync run produces no duplicate records
- [ ] Deleted-then-resynced customer is restored correctly
- [ ] Cross-SaaS ExternalId collision does not corrupt data

---

---

# ⏳ SPRINT 4 — Sales Pipeline & Performance Tracking
**Status:** PLANNED (blocked until Sprint 3 approved)
**Goal:** Build Opportunities/Sales Pipeline Kanban, user performance reports, notification system, and task due-date reminders.
**Duration:** 3–4 days | **Agents:** Backend Agent, Frontend Agent | **Story Points:** 37

---

### 🎫 ION-035
**Title:** [BACKEND] Opportunities entity and CRUD
**Sprint:** Sprint 4 | **Points:** 5 | **Labels:** backend
**Description:**
New `Opportunities` table:
```
Id (uuid), ProjectId, CustomerId, OwnerId (UserId),
Title (varchar 200), Value (decimal 18,2), Stage (enum),
ExpectedCloseDate (date), ActualCloseDate (date nullable),
Notes (text), IsDeleted, CreatedAt, UpdatedAt
```
Stage enum: `Lead=0, Qualified=1, ProposalSent=2, Negotiation=3, Won=4, Lost=5`

Endpoints:
- `GET /api/v1/opportunities` — filters: stage, owner, date range
- `POST /api/v1/opportunities`
- `GET /api/v1/opportunities/{id}`
- `PUT /api/v1/opportunities/{id}`
- `DELETE /api/v1/opportunities/{id}`
- `PUT /api/v1/opportunities/{id}/stage` — move stage, log to AuditLog

**Acceptance Criteria:**
- [ ] All CRUD endpoints working
- [ ] Stage change writes AuditLog entry
- [ ] Value and ExpectedCloseDate required on create

---

### 🎫 ION-036
**Title:** [FRONTEND] Opportunities Kanban board
**Sprint:** Sprint 4 | **Points:** 8 | **Labels:** frontend
**Description:**
`/opportunities` route — Kanban view:
- Columns: Lead → Qualified → Proposal Sent → Negotiation → Won → Lost
- Cards: customer name, opportunity title, value (TRY formatted), expected close date
- Drag-and-drop between stages (calls `PUT /opportunities/{id}/stage`)
- Click card → opportunity detail drawer
- "Yeni Fırsat" button → create form
- Summary bar: total pipeline value, count per stage

**Acceptance Criteria:**
- [ ] Drag-and-drop works on desktop and touch (mobile)
- [ ] Stage move triggers API call with optimistic UI update
- [ ] Total value updates after stage move

---

### 🎫 ION-037
**Title:** [BACKEND] Sales performance report endpoints
**Sprint:** Sprint 4 | **Points:** 5 | **Labels:** backend
**Description:**
- `GET /api/v1/reports/sales-performance` — per user: contact count, opps created, won value, conversion rate. Filters: `?userId=&from=&to=`
- `GET /api/v1/reports/pipeline-forecast` — expected revenue by month (grouped by ExpectedCloseDate). Stage probability weights: Lead=10%, Qualified=30%, Proposal=50%, Negotiation=75%, Won=100%

**Acceptance Criteria:**
- [ ] Performance report returns per-user stats
- [ ] Forecast groups by month correctly
- [ ] Role-based access enforced (SuperAdmin sees all projects)

---

### 🎫 ION-038
**Title:** [FRONTEND] Sales performance & forecast charts
**Sprint:** Sprint 4 | **Points:** 5 | **Labels:** frontend
**Description:**
`/reports` route:
1. **Sales Leaderboard** — table: Rep name, contacts made, opps created, won value
2. **Pipeline Forecast** — bar chart by month showing expected revenue
3. **Win Rate Trend** — line chart over last 6 months
Date range picker filters all charts simultaneously.

**Acceptance Criteria:**
- [ ] Leaderboard sortable by any column
- [ ] Charts responsive on mobile
- [ ] Date range filter applies to all charts simultaneously

---

### 🎫 ION-039
**Title:** [BACKEND] In-app notification system
**Sprint:** Sprint 4 | **Points:** 5 | **Labels:** backend
**Description:**
`Notifications` table:
```
Id, UserId, ProjectId, Type (enum), Message, EntityType, EntityId, IsRead, CreatedAt
```
Type enum: `TaskDue=0, PipelineReminder=1, OpportunityStageChange=2, SyncFailure=3`

Trigger events:
- Task due date approaching (24h before)
- Pipeline call scheduled for today
- Opportunity moved to Won or Lost
- Sync failure detected

Endpoints:
- `GET /api/v1/notifications` — unread first, paginated
- `PUT /api/v1/notifications/{id}/read`
- `PUT /api/v1/notifications/read-all`

**Acceptance Criteria:**
- [ ] Notifications created on all trigger events
- [ ] Mark-read endpoints working
- [ ] Only current user's notifications returned

---

### 🎫 ION-040
**Title:** [FRONTEND] Notification bell + dropdown
**Sprint:** Sprint 4 | **Points:** 3 | **Labels:** frontend
**Description:**
Top navbar notification bell:
- Badge with unread count (red dot)
- Click → dropdown list of recent notifications
- Each item: icon, message, time ago, link to related entity
- "Tümünü okundu işaretle" button
- Polls `GET /notifications` every 30 seconds

**Acceptance Criteria:**
- [ ] Badge count accurate (refreshes every 30s)
- [ ] Clicking notification navigates to related entity
- [ ] Mark-all-read clears badge

---

### 🎫 ION-041
**Title:** [BACKEND] Task due-date + pipeline reminder background job
**Sprint:** Sprint 4 | **Points:** 3 | **Labels:** backend
**Description:**
`ReminderBackgroundService` — runs hourly:
- Query tasks due in next 24 hours with no prior notification → create `Notification` for assigned user
- Query pipeline entries scheduled for today with no prior notification → create `Notification` for sales rep
- Idempotent: same task/pipeline does NOT create duplicate notification on next run

**Acceptance Criteria:**
- [ ] Tasks due tomorrow appear in notifications
- [ ] Same task does not create duplicate notification on next run
- [ ] Pipeline reminders fire on day-of

---

### 🎫 ION-042
**Title:** [FRONTEND] Customer notes full implementation
**Sprint:** Sprint 4 | **Points:** 3 | **Labels:** frontend
**Description:**
Customer detail page — Notes tab:
- List all notes (newest first): author, timestamp, content
- "Not Ekle" inline text area (no modal needed)
- Submit on Ctrl+Enter or button click
- Edit own notes inline; delete own notes with confirm dialog
- React Query invalidation on changes (no page refresh)

**Acceptance Criteria:**
- [ ] Notes list/add/edit/delete all functional
- [ ] Cannot edit/delete another user's note
- [ ] Mobile: full-width note cards

---

---

# ⏳ SPRINT 5 — Frontend Polish & UX
**Status:** PLANNED (blocked until Sprint 4 approved)
**Goal:** Complete all remaining frontend screens, full mobile-first responsiveness, global search (Cmd+K), user/project management, UX polish, accessibility, and CSV export.
**Duration:** 3–4 days | **Agent:** Frontend Agent (+ Backend for search/export/admin endpoints) | **Story Points:** 38

---

### 🎫 ION-043
**Title:** [FRONTEND] Global search (Cmd+K)
**Sprint:** Sprint 5 | **Points:** 5 | **Labels:** frontend
**Description:**
Command-palette style global search:
- Keyboard shortcut: Cmd+K (Mac) / Ctrl+K (Windows)
- Search across: Customers (name, email, phone), Opportunities (title), Pipeline entries
- Calls `GET /api/v1/search?q=` (ION-044)
- Results grouped by type with icons
- Click result → navigate to entity
- Recent searches stored in localStorage
- Mobile: accessible via search icon in header

**Acceptance Criteria:**
- [ ] Opens on Cmd+K / Ctrl+K
- [ ] Results appear within 300ms (debounced 200ms)
- [ ] Keyboard navigation (↑↓ Enter Esc)
- [ ] Mobile: tap search icon

---

### 🎫 ION-044
**Title:** [BACKEND] Global search endpoint
**Sprint:** Sprint 5 | **Points:** 3 | **Labels:** backend
**Description:**
`GET /api/v1/search?q={term}&limit=10`
- PostgreSQL `ILIKE` across: Customer (FullName, Email, Phone, Company), Opportunity (Title)
- Response: `{ type: "customer"|"opportunity"|"pipeline", id, display, subtitle }`
- Project-scoped (SuperAdmin can add `?projectId=`)
- Add GIN indexes on searchable text fields

**Acceptance Criteria:**
- [ ] Returns mixed-type results
- [ ] Response under 100ms for typical queries
- [ ] Minimum 2 characters required

---

### 🎫 ION-045
**Title:** [FRONTEND] User & Project management pages (SuperAdmin)
**Sprint:** Sprint 5 | **Points:** 5 | **Labels:** frontend
**Description:**
`/admin/users`:
- User list: name, email, projects, role, last login, active/inactive toggle
- "Kullanıcı Ekle" form: name, email, temp password, assign project + role
- Edit user: change role, assign/unassign projects, deactivate
- "Şifre Sıfırla" — generate reset link

`/admin/projects`:
- Project list: name, SaasCode, sync status, user count
- "Proje Ekle" form: name, SaasCode, WebhookUrl

**Acceptance Criteria:**
- [ ] All CRUD operations call correct backend endpoints
- [ ] Role assignment updates immediately
- [ ] Deactivated users cannot login

---

### 🎫 ION-046
**Title:** [BACKEND] User & Project management endpoints (SuperAdmin)
**Sprint:** Sprint 5 | **Points:** 3 | **Labels:** backend
**Description:**
- `GET/POST /api/v1/admin/users`
- `PUT/DELETE /api/v1/admin/users/{id}`
- `POST /api/v1/admin/users/{id}/reset-password`
- `GET/POST /api/v1/admin/projects`
- `PUT /api/v1/admin/projects/{id}`

All SuperAdmin-only. Project creation validates SaasCode uniqueness.

**Acceptance Criteria:**
- [ ] All routes return 403 for non-SuperAdmin
- [ ] Create user optionally sends welcome email (if SMTP configured)
- [ ] Project SaasCode uniqueness enforced

---

### 🎫 ION-047
**Title:** [FRONTEND] User profile & settings page
**Sprint:** Sprint 5 | **Points:** 3 | **Labels:** frontend
**Description:**
`/profile` route (all users):
- Display name, email, role, assigned projects
- "Şifre Değiştir" section: current + new + confirm
- Theme toggle (dark/light) — persisted in localStorage
- Language: Turkish only (structure ready for future i18n)

**Acceptance Criteria:**
- [ ] Password change calls `PUT /api/v1/auth/change-password`
- [ ] Theme toggle persists across sessions

---

### 🎫 ION-048
**Title:** [FRONTEND] Mobile navigation overhaul
**Sprint:** Sprint 5 | **Points:** 5 | **Labels:** frontend
**Description:**
Full mobile-first UX (≤768px breakpoint):
- Replace sidebar with bottom navigation bar on mobile
- Bottom nav: Dashboard, Customers, Pipeline, Opportunities, More (...)
- "More" opens sheet with: Reports, Contact Histories, Admin, Profile
- All tables switch to card-list view on mobile
- All forms use full-screen modal on mobile
- Swipe-to-delete on customer/pipeline list items

**Acceptance Criteria:**
- [ ] No horizontal scroll on any screen at 375px width
- [ ] Bottom nav visible on all main routes (mobile)
- [ ] All touch targets ≥ 44×44px

---

### 🎫 ION-049
**Title:** [FRONTEND] Empty states, loading skeletons, error boundaries
**Sprint:** Sprint 5 | **Points:** 3 | **Labels:** frontend
**Description:**
Polish pass on all screens:
- **Empty states**: Customer list, Pipeline, Opportunities — each with CTA button
- **Loading skeletons**: All tables and dashboards show skeleton UI while loading
- **Error boundaries**: Catch render errors per page, show "Bir hata oluştu" with retry button
- **Toast notifications**: Consistent success/error toasts on all mutations

**Acceptance Criteria:**
- [ ] No blank screens during data load
- [ ] Network error shows user-friendly message
- [ ] All CRUD operations show success/error toast

---

### 🎫 ION-050
**Title:** [FRONTEND] Advanced customer filters & export
**Sprint:** Sprint 5 | **Points:** 5 | **Labels:** frontend
**Description:**
Enhanced customer list:
- Multi-select label filter (AND logic)
- Multi-select status filter
- Company name text filter
- "Son Görüşme" date range filter
- Sort by: Created date, Last contact, Name
- **CSV Export**: calls `GET /api/v1/customers/export?format=csv` with active filters
- Persist all filters in URL params (shareable links)

**Acceptance Criteria:**
- [ ] Combined filters work correctly
- [ ] CSV download includes all columns
- [ ] URL with filters can be shared and reloads correctly

---

### 🎫 ION-051
**Title:** [BACKEND] Customer CSV export endpoint
**Sprint:** Sprint 5 | **Points:** 2 | **Labels:** backend
**Description:**
`GET /api/v1/customers/export?format=csv&label=&status=&company=`
- Streams CSV response (Content-Type: text/csv)
- Applies same filters as customer list
- Columns: Id, FullName, Email, Phone, Company, Label, Status, CreatedAt, LastContactDate
- Respects ProjectId tenant scoping
- Filename: `customers-{date}.csv`

**Acceptance Criteria:**
- [ ] Returns valid CSV file
- [ ] All active filters applied
- [ ] Tenant scoping respected

---

### 🎫 ION-052
**Title:** [FRONTEND] Accessibility & i18n preparation
**Sprint:** Sprint 5 | **Points:** 4 | **Labels:** frontend
**Description:**
- Add `aria-label` to all icon-only buttons
- Ensure keyboard-navigable forms (tab order, focus management)
- Add `lang="tr"` to HTML root
- Extract all Turkish string literals to `locales/tr.ts` constants
- Ensure color contrast meets WCAG AA in both dark and light modes

**Acceptance Criteria:**
- [ ] No axe-core accessibility errors on main screens
- [ ] All strings in locales file (ready for future language addition)

---

---

# ⏳ SPRINT 6 — Hardening, Testing & Production
**Status:** PLANNED (blocked until Sprint 5 approved)
**Goal:** Validate migration completeness, write full test suite (unit + integration + E2E), security audit, and deliver production-ready deployment with monitoring.
**Duration:** 3–4 days | **Agents:** Backend Agent, DevOps Agent, Testing Agent | **Story Points:** 40

---

### 🎫 ION-053
**Title:** [BACKEND] Validate & harden one-time data migration
**Sprint:** Sprint 6 | **Points:** 5 | **Labels:** backend, migration
**Description:**
The 639-customer / 892-contact migration ran in Sprint 1. Now validate completely:
- Re-run migration script against dev DB and compare row counts
- Verify nullable fields handled (no "N/A" strings in phone/email)
- Confirm Turkish characters (ğ, ş, ı, ç) preserved correctly (UTF-8)
- Verify `LegacyId` mappings intact (needed for SaaS sync deduplication)
- Write reconciliation SQL: count per-table source vs target
- Document any data quality issues found

**Acceptance Criteria:**
- [ ] 639 customers in new DB with correct field mapping
- [ ] 892 contact histories linked to correct customers
- [ ] No encoding issues in Turkish characters
- [ ] Reconciliation SQL script passes

---

### 🎫 ION-054
**Title:** [TESTING] Backend unit test suite (Domain + Application)
**Sprint:** Sprint 6 | **Points:** 8 | **Labels:** testing, backend
**Description:**
Using xUnit + Moq + FluentAssertions. Target: **80%+ coverage on Application layer**:
- `CreateCustomerCommandHandler` — validation, repo mock, result
- `MergeCustomerCommandHandler` — transaction logic, all edge cases
- `SaasCallbackHandler` — fire-and-forget, retry logic
- `SyncSaasAHandler` — upsert logic, SyncLog creation
- All FluentValidation validators
- All domain entity invariants

**Acceptance Criteria:**
- [ ] `dotnet test` passes with 0 failures
- [ ] Coverage report ≥ 80% on Application layer
- [ ] Edge cases: null payload, duplicate ExternalId, invalid ProjectId

---

### 🎫 ION-055
**Title:** [TESTING] Backend integration test suite
**Sprint:** Sprint 6 | **Points:** 5 | **Labels:** testing, backend
**Description:**
Using `WebApplicationFactory<Program>` + Testcontainers (PostgreSQL):
- Auth: login → get token → use token
- Customer CRUD end-to-end
- Label/Status filter queries
- Merge workflow (Potansiyel → Müşteri)
- Pipeline create → add contact → verify Status=Tamamlandi
- Sync endpoint: send SaaS payload → verify customer upserted
- **Multi-tenant isolation test**: user from Project A cannot see Project B data

**Acceptance Criteria:**
- [ ] All integration tests pass against real PostgreSQL (Testcontainers)
- [ ] Multi-tenant isolation explicitly tested and passing
- [ ] Tests run in CI via GitHub Actions

---

### 🎫 ION-056
**Title:** [TESTING] Frontend E2E test suite (Playwright)
**Sprint:** Sprint 6 | **Points:** 5 | **Labels:** testing, frontend
**Description:**
Key user flows tested with Playwright:
- Login → see dashboard
- Add customer → appears in list
- Change customer label → badge updates
- Add pipeline entry → appears on pipeline page
- Mark pipeline complete → status badge changes
- Create opportunity → drag to "Qualified" stage
- SuperAdmin: view sync logs page

**Acceptance Criteria:**
- [ ] All E2E tests pass against dev environment
- [ ] Tests run in CI on merge to main
- [ ] Screenshots on failure uploaded as GitHub Actions artifact

---

### 🎫 ION-057
**Title:** [DEVOPS] Security audit and hardening
**Sprint:** Sprint 6 | **Points:** 5 | **Labels:** devops, security
**Description:**
Pre-production security checklist:
- Run `dotnet-retire` / OWASP dependency check on NuGet packages
- Run `npm audit` on frontend
- Verify ALL endpoints have `[Authorize]` attribute (no accidental open endpoints)
- Check no secrets in git history (`trufflehog` scan)
- Add rate limiting: `AspNetCoreRateLimit` (100 req/min per IP)
- CORS policy: only allow Railway frontend URL
- Security headers: HSTS, X-Frame-Options, CSP
- Verify JWT expiry/refresh works correctly
- Test that SuperAdmin claims cannot be forged

**Acceptance Criteria:**
- [ ] Zero critical/high NuGet or npm vulnerabilities
- [ ] No unprotected endpoints found
- [ ] Rate limiting configured and tested
- [ ] Security headers present on all responses

---

### 🎫 ION-058
**Title:** [DEVOPS] Production deployment and monitoring
**Sprint:** Sprint 6 | **Points:** 5 | **Labels:** devops
**Description:**
Final production deployment:
- Deploy to Railway prod (API + Frontend services)
- Run EF Core migrations on prod Neon DB
- Verify HTTPS on prod domains
- Configure Railway health checks (ping `/health` every 30s)
- Add Serilog structured logging → Railway logs (JSON format)
- Set all prod environment variables in Railway dashboard
- Verify SaaS A and B webhooks point to prod API URL

**Acceptance Criteria:**
- [ ] Prod API responds at `/health` with 200
- [ ] Prod frontend loads at prod URL
- [ ] All migrations applied to prod DB
- [ ] Health check green in Railway dashboard

---

### 🎫 ION-059
**Title:** [DEVOPS] CI/CD pipeline enhancement
**Sprint:** Sprint 6 | **Points:** 3 | **Labels:** devops
**Description:**
Enhance existing GitHub Actions:
- Add test stage: `dotnet test` runs on every PR
- Add Playwright E2E stage: runs on merge to main
- Add build status badge to README
- Branch protection: require PR review + all checks green before merge to main
- Add `dotnet format` check to PR workflow
- Separate dev auto-deploy (main push) vs prod manual approval gate

**Acceptance Criteria:**
- [ ] PRs blocked from merging if tests fail
- [ ] Prod deploy requires GitHub workflow manual approval
- [ ] Badge visible in README

---

### 🎫 ION-060
**Title:** [BACKEND] Health check and readiness probe endpoint
**Sprint:** Sprint 6 | **Points:** 2 | **Labels:** backend, devops
**Description:**
ASP.NET Core Health Checks:
- `GET /health` — overall: Healthy/Degraded/Unhealthy
  - Checks: DB connectivity (Neon), SaaS A reachability, SaaS B reachability, last sync age (warning if >30 min)
- `GET /health/ready` — simple 200 for Railway readiness probe
- Package: `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`

**Acceptance Criteria:**
- [ ] `/health` returns JSON with component statuses
- [ ] DB failure returns Unhealthy
- [ ] Sync lag > 30 min returns Degraded

---

---

## 📊 SPRINT SUMMARY TABLE

| Sprint | Name | Status | Stories | Points | Duration | Agents |
|--------|------|--------|---------|--------|----------|--------|
| Sprint 0 | Analysis & Architecture | ✅ DONE | 4 | 21 | 2–3 days | Architect |
| Sprint 1 | Foundation | ✅ DONE | 9 | 34 | 3–4 days | Backend, Frontend, DevOps |
| Sprint 2 | Customer Core Enhancement | 🔄 ACTIVE | 13 | 42 | 3–4 days | Backend, Frontend |
| Sprint 3 | Sync Service | ⏳ PLANNED | 8 | 39 | 3–4 days | Backend, DevOps |
| Sprint 4 | Sales Pipeline & Performance | ⏳ PLANNED | 8 | 37 | 3–4 days | Backend, Frontend |
| Sprint 5 | Frontend Polish & UX | ⏳ PLANNED | 10 | 38 | 3–4 days | Frontend, Backend |
| Sprint 6 | Hardening, Testing & Production | ⏳ PLANNED | 8 | 40 | 3–4 days | Backend, DevOps, Testing |
| **TOTAL** | | | **60** | **251** | **~22–27 days** | |

---

## 🚧 OPEN RISKS & DEPENDENCIES

| Risk | Affects Sprint | Mitigation |
|------|---------------|-----------|
| SaaS A/B API credentials not yet provided | Sprint 3 | Use mock server for dev; real creds via env vars |
| SaaS payload schemas unknown | Sprint 3 | Design handlers as schema-driven (JSON config per SaaS source) |
| Neon DB connection pool limits (free tier) | Sprint 3+ | Use Neon pooler URL; configure EF Core pool size |
| Customer list API returning 0 records | Sprint 2 (ION-014) | CRITICAL bug — first task in Sprint 2 |
| EF Core migrations on live Neon DB | Sprint 2 | Run on dev first; all new columns have DEFAULT values |
| Merge operation atomicity | Sprint 2 (ION-020) | Explicit EF Core transaction with try/catch/rollback |
| Playwright tests flaky on Railway CI | Sprint 6 | Add retry logic; use `data-testid` selectors |
| Production migration needs downtime | Sprint 6 | Schedule maintenance window; all migrations are additive only |

---

*Last updated: 2026-03-25 by Orchestrator Agent*
