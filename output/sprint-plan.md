# ION CRM — Master Sprint Plan (Updated)
> Generated: 2026-03-24
> Orchestrator: Claude (Product Manager Agent)
> Stack: ASP.NET Core 8 · Clean Architecture · PostgreSQL (Neon) · React 18 · shadcn/ui · Railway
> Repo: https://github.com/ionivasoftware/ionivacrm

---

## 📋 PROJECT SUMMARY

| Item | Detail |
|------|--------|
| Architecture | Clean Architecture (Domain → Application → Infrastructure → API) |
| Auth | JWT Bearer (15-min access / 7-day refresh), RBAC + Project-scoped |
| Multi-tenancy | SuperAdmin (all data) + Project-scoped roles (ProjectAdmin, SalesManager, SalesRep, Accounting) |
| Sync | SaaS A & B → CRM every 15 min (pull); CRM → SaaS instant push (subscriptions, status changes) |
| Migration | One-time MSSQL .bak → PostgreSQL (customers + contact history — 639 customers, 892 contacts migrated ✅) |
| Frontend | React 18, shadcn/ui, Zustand, React Query, dark mode default, Turkish language, mobile-first |
| Deploy | Railway via GitHub Actions CI/CD (dev + prod environments live ✅) |
| DB File | `/input/database/crm.bak` — 4.4 MB MSSQL backup |

---

## ⚠️ APPROVAL GATES (MANDATORY)

1. ✅ After sprint planning → before any code written
2. ✅ After DB schema design → before migrations run
3. ✅ Sprint 0 completed & approved
4. ✅ Sprint 1 completed & approved
5. 🔄 **Sprint 2 active — awaiting completion approval before Sprint 3**
6. ⏳ After Sprint 3 completion → before Sprint 4
7. ⏳ After Sprint 4 completion → before Sprint 5
8. ⏳ After Sprint 5 completion → before Sprint 6
9. ⏳ Before any deployment to Production

---

# ✅ SPRINT 0 — Analysis & Architecture
**Status:** COMPLETED
**Goal:** Analyze legacy MSSQL .bak, design PostgreSQL schema, define API contracts, scaffold solution structure.
**Duration:** 2–3 days | **Agent:** Architect Agent | **Points:** 21

| Ticket | Title | Points | Status |
|--------|-------|--------|--------|
| ION-001 | [ARCHITECT] Analyze MSSQL .bak and extract legacy schema | 5 | ✅ Done |
| ION-002 | [ARCHITECT] Design PostgreSQL target schema | 8 | ✅ Done |
| ION-003 | [ARCHITECT] Define API contracts and OpenAPI spec | 5 | ✅ Done |
| ION-004 | [ARCHITECT] Define solution folder structure and scaffold plan | 3 | ✅ Done |

**Deliverables produced:** `legacy-schema.md`, `migration-mapping.md`, `db-schema.md`, `entity-stubs/`, `api-contracts.md`, `project-structure.md`, `.env.example`

---

# ✅ SPRINT 1 — Foundation
**Status:** COMPLETED
**Goal:** Bootstrap the solution, implement auth, base entities, CI/CD, Railway deploy, and initial data migration.
**Duration:** 3–4 days | **Agents:** Backend Agent, DevOps Agent, Frontend Agent | **Points:** 34

| Ticket | Title | Points | Status |
|--------|-------|--------|--------|
| ION-005 | [BACKEND] .NET Core 8 Clean Architecture solution scaffold | 5 | ✅ Done |
| ION-006 | [BACKEND] JWT Authentication (login, logout, refresh, /me) | 8 | ✅ Done |
| ION-007 | [BACKEND] Customers CRUD base (+ EF Core Neon migrations) | 5 | ✅ Done |
| ION-008 | [BACKEND] ContactHistories base endpoints | 3 | ✅ Done |
| ION-009 | [BACKEND] CustomerTasks base endpoints | 3 | ✅ Done |
| ION-010 | [BACKEND] Sync endpoints stub (SaaS A, SaaS B inbound) | 3 | ✅ Done |
| ION-011 | [FRONTEND] React 18 app scaffold (shadcn/ui, Tailwind, dark mode, sidebar, login) | 5 | ✅ Done |
| ION-012 | [DEVOPS] Railway deploy (dev + prod), GitHub Actions CI/CD | 5 | ✅ Done |
| ION-013 | [BACKEND] One-time data migration: 639 customers + 892 contact histories from .bak | 5 | ✅ Done |

**Live environments:**
- Dev API: https://ion-crm-api-development.up.railway.app
- Dev Frontend: https://ion-crm-frontend-development.up.railway.app
- Prod API: https://ion-crm-api-production.up.railway.app
- Prod Frontend: https://ion-crm-frontend-production.up.railway.app

---

# 🔄 SPRINT 2 — Customer Core Enhancement
**Status:** ACTIVE
**Goal:** Fix critical data-display bugs, add Label/Status classification system, build Pipeline (call scheduling), merge Potansiyel→Müşteri workflow, and add Dashboard analytics.
**Duration:** 3–4 days | **Agents:** Backend Agent, Frontend Agent | **Points:** 42

---

### 🎫 ION-014
**Title:** [BACKEND] Fix: Customer list API not returning records
**Sprint:** Sprint 2
**Story Points:** 2
**Labels:** backend, bug
**Description:**
Customers screen shows 0 records despite 639 customers in Neon DB. Diagnose the root cause:
- Check `GET /api/v1/customers` query — likely a missing `ProjectId` filter returning 0 for null tenant context.
- Verify JWT claims include ProjectId and are correctly parsed in middleware.
- Confirm EF Core query does not accidentally filter IsDeleted=false on null records.
- Return paginated 639 customers after fix.

**Acceptance Criteria:**
- [ ] `GET /api/v1/customers` returns paginated results (default page 1, size 20)
- [ ] All 639 existing records visible to authenticated ProjectAdmin
- [ ] SuperAdmin can see records across all projects

---

### 🎫 ION-015
**Title:** [FRONTEND] Fix: Customer add form renders blank
**Sprint:** Sprint 2
**Story Points:** 2
**Labels:** frontend, bug
**Description:**
The "Müşteri Ekle" page renders empty (no form fields). Diagnose and fix:
- Check React component for missing form state initialization.
- Verify React Query mutation for `POST /api/v1/customers` is wired to submit handler.
- Ensure all required fields (FullName, Phone, Email, Company) are present.
- Add form validation with error messages.

**Acceptance Criteria:**
- [ ] Form displays all required fields on load
- [ ] Submitting a valid customer navigates back to list
- [ ] Validation errors shown inline

---

### 🎫 ION-016
**Title:** [FRONTEND] Add customer detail, edit, and delete pages
**Sprint:** Sprint 2
**Story Points:** 5
**Labels:** frontend
**Description:**
Three missing customer screens:
1. **Detail page** (`/customers/:id`) — name, contact info, label, status, contact history list, tasks list.
2. **Edit page** (`/customers/:id/edit`) — pre-filled form, `PUT /api/v1/customers/:id`.
3. **Delete** — confirmation dialog with `DELETE /api/v1/customers/:id` (soft delete).

Use shadcn/ui Card, Dialog, and Form components. Mobile-first layout.

**Acceptance Criteria:**
- [ ] Detail page shows all customer fields and related contact history
- [ ] Edit form pre-populates existing values
- [ ] Delete shows confirmation dialog; customer removed from list after confirmation
- [ ] Back navigation works correctly

---

### 🎫 ION-017
**Title:** [BACKEND] Customer Label system (YuksekPotansiyel/Potansiyel/Notr/Vasat/Kotu)
**Sprint:** Sprint 2
**Story Points:** 3
**Labels:** backend
**Description:**
Add classification labels to customers:
```sql
ALTER TABLE "Customers" ADD "Label" integer NOT NULL DEFAULT 2;
-- 0=YuksekPotansiyel, 1=Potansiyel, 2=Notr, 3=Vasat, 4=Kotu
```
- Add `CustomerLabel` enum to Domain layer.
- Add `Label` property to `Customer` entity.
- Create and run EF Core migration.
- Update `CreateCustomerCommand`, `UpdateCustomerCommand`, `CustomerDto`.
- Support `?label=` filter on `GET /api/v1/customers`.

**Acceptance Criteria:**
- [ ] Migration runs cleanly on dev Neon DB
- [ ] Label field persisted on create/update
- [ ] GET customers supports `?label=0` filter

---

### 🎫 ION-018
**Title:** [BACKEND] Customer Status system (Musteri/Potansiyel/Demo)
**Sprint:** Sprint 2
**Story Points:** 3
**Labels:** backend
**Description:**
```sql
ALTER TABLE "Customers" ADD "Status" integer NOT NULL DEFAULT 1;
-- 0=Musteri, 1=Potansiyel, 2=Demo
```
- Add `CustomerStatus` enum to Domain.
- EF Core migration.
- Update DTOs and commands.
- Support `?status=` filter on `GET /api/v1/customers`.

**Acceptance Criteria:**
- [ ] Status field persisted
- [ ] GET customers supports `?status=0` filter
- [ ] Existing 639 customers default to Status=Potansiyel (1)

---

### 🎫 ION-019
**Title:** [FRONTEND] Label and Status badges + filters on customer list
**Sprint:** Sprint 2
**Story Points:** 3
**Labels:** frontend
**Description:**
Enhance customer list page:
- Show colored badge for Label (e.g., YuksekPotansiyel = green, Kotu = red)
- Show status badge (Musteri = blue, Potansiyel = yellow, Demo = purple)
- Add filter bar: Label dropdown + Status dropdown (chips style)
- Selecting filter calls `GET /api/v1/customers?label=X&status=Y`
- Filters persist in URL query string

**Acceptance Criteria:**
- [ ] All 5 labels render with distinct colors
- [ ] All 3 statuses render with distinct colors
- [ ] Filter chips update list in real time

---

### 🎫 ION-020
**Title:** [BACKEND] Potansiyel → Müşteri merge endpoint
**Sprint:** Sprint 2
**Story Points:** 5
**Labels:** backend
**Description:**
`POST /api/v1/customers/{sourceId}/merge`
Body: `{ "targetCustomerId": "uuid" }`

Business rules (must be atomic transaction):
1. Verify `source` customer has `Status = Potansiyel`
2. Transfer all `ContactHistories` from source → target (`CustomerId` updated)
3. Transfer all `Tasks` from source → target
4. Soft-delete the source customer (`IsDeleted = true`)
5. Return updated target customer DTO

Roles: ProjectAdmin, SalesManager

**Acceptance Criteria:**
- [ ] Transaction rolls back fully on any error
- [ ] Source customer soft-deleted after merge
- [ ] All contact histories appear on target customer
- [ ] Returns 409 if source is not Potansiyel status

---

### 🎫 ION-021
**Title:** [FRONTEND] "Müşteriye Bağla" merge UI
**Sprint:** Sprint 2
**Story Points:** 3
**Labels:** frontend
**Description:**
On the customer detail page, when `Status = Potansiyel`:
- Show "Müşteriye Bağla" button
- Clicking opens a searchable modal listing active Musteri-status customers
- User selects target → confirmation dialog: "639 görüşme [source] → [target] aktarılacak. Emin misiniz?"
- On confirm: call `POST /customers/{id}/merge`, refresh page, redirect to target customer

**Acceptance Criteria:**
- [ ] Button only visible for Potansiyel-status customers
- [ ] Modal is searchable
- [ ] Confirmation dialog shows source/target names
- [ ] Success redirects to target customer detail

---

### 🎫 ION-022
**Title:** [BACKEND] Contact History result field + all-histories endpoint
**Sprint:** Sprint 2
**Story Points:** 3
**Labels:** backend
**Description:**
```sql
ALTER TABLE "ContactHistories" ADD "ContactResult" integer NULL;
-- 0=Olumlu, 1=Olumsuz, 2=BaskaTedarikci
```
- Add `ContactResult` enum and field to entity + DTO
- EF Core migration
- `GET /api/v1/contact-histories` — paginated, with filters:
  - `?from=` `?to=` (date range)
  - `?type=` (Call, Email, Meeting, Note)
  - `?result=` (Olumlu, Olumsuz, BaskaTedarikci)
- `PUT /api/v1/contact-histories/{id}` — allow result update

**Acceptance Criteria:**
- [ ] Migration applied
- [ ] GET /contact-histories returns all contacts across project, paginated
- [ ] All three filters work independently and combined

---

### 🎫 ION-023
**Title:** [FRONTEND] All Contact Histories page
**Sprint:** Sprint 2
**Story Points:** 3
**Labels:** frontend
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
**Sprint:** Sprint 2
**Story Points:** 5
**Labels:** backend
**Description:**
New `Pipelines` table:
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
Endpoints:
- `GET /api/v1/pipelines` — `?from=&to=&status=` filters; defaults to next 7 days
- `POST /api/v1/pipelines`
- `PUT /api/v1/pipelines/{id}`
- `DELETE /api/v1/pipelines/{id}` (soft delete)
- `POST /api/v1/pipelines/{id}/contact` — create ContactHistory from pipeline entry, sets pipeline Status=Tamamlandi

**Acceptance Criteria:**
- [ ] EF Core migration runs
- [ ] CRUD all working
- [ ] /contact sub-action creates CH and marks pipeline complete atomically
- [ ] Default date filter returns only next 7 days

---

### 🎫 ION-025
**Title:** [FRONTEND] Pipeline page + "Pipeline Ekle" button
**Sprint:** Sprint 2
**Story Points:** 4
**Labels:** frontend
**Description:**
1. **Customer detail page** — "Pipeline Ekle" button opens drawer/modal: date picker, notes field
2. **`/pipeline` route** — list view grouped by date:
   - Each row: Müşteri adı, Tarih, Notlar, Durum badge, action buttons
   - "Görüşme Kaydı Gir" button per row → quick contact-log form
   - Edit / Delete actions
3. Status badges: Bekliyor (orange), Tamamlandı (green), İptal (gray)

**Acceptance Criteria:**
- [ ] Pipeline items grouped by date
- [ ] "Görüşme Kaydı Gir" creates contact and marks pipeline done
- [ ] Accessible from sidebar

---

### 🎫 ION-026
**Title:** [FRONTEND] Dashboard — Pipeline widget + analytics charts
**Sprint:** Sprint 2
**Story Points:** 5
**Labels:** frontend
**Description:**
Enhance the existing dashboard:

**Pipeline Widget:**
- Upcoming 7-day pipeline calls, sorted by date
- "Görüşme Kaydı Gir" action inline

**Analytics Charts (use recharts or similar):**
1. Total Customers count card
2. Customers by Status (donut/pie)
3. Customers by Label (bar chart)
4. Contact history volume over last 30 days (line chart)

All charts call `GET /api/v1/dashboard/stats`
Pipeline widget calls `GET /api/v1/dashboard/pipeline`

**Backend:**
- `GET /api/v1/dashboard/stats` — counts by status, by label, contact history daily counts
- `GET /api/v1/dashboard/pipeline` — next 7-day pipeline for current project

**Acceptance Criteria:**
- [ ] Dashboard shows 4 metrics
- [ ] Pipeline widget lists upcoming calls
- [ ] Charts render with real data
- [ ] Mobile responsive (charts stack vertically)

---

# ⏳ SPRINT 3 — Sync Service
**Status:** PLANNED
**Goal:** Build a robust bidirectional sync engine: background pull from SaaS A & B every 15 minutes, and instant push callbacks from CRM to SaaS on subscription/status changes.
**Duration:** 3–4 days | **Agents:** Backend Agent, DevOps Agent | **Points:** 39

---

### 🎫 ION-027
**Title:** [BACKEND] SaaS A inbound sync handler (pull every 15 min)
**Sprint:** Sprint 3
**Story Points:** 8
**Labels:** backend, sync
**Description:**
Implement a `IHostedService` background worker (`SyncBackgroundService`) that fires every 15 minutes:
- Calls SaaS A REST API (or polls configured webhook endpoint) for new/updated customers
- Payload schema to be confirmed from Sprint 0 API contracts
- Upsert logic: if `ExternalId` exists → update; else → insert new Customer
- Project scoped: only syncs customers for `Project.SaasCode = "A"`
- Write result to `SyncLogs` (Success/Failed, record count, payload sample)
- On failure: exponential backoff, max 3 retries, alert via log

Environment variables:
```
SAAS_A_API_URL=
SAAS_A_API_KEY=
SAAS_A_SYNC_INTERVAL_MINUTES=15
```

**Acceptance Criteria:**
- [ ] Background service starts on application startup
- [ ] Runs exactly every 15 minutes (configurable)
- [ ] New customers from SaaS A appear in CRM after sync
- [ ] Updated customers (by ExternalId) are updated, not duplicated
- [ ] SyncLog entry written for every run (success or failure)
- [ ] Failed run triggers retry logic with backoff

---

### 🎫 ION-028
**Title:** [BACKEND] SaaS B inbound sync handler (pull every 15 min)
**Sprint:** Sprint 3
**Story Points:** 5
**Labels:** backend, sync
**Description:**
Same pattern as ION-027 but for SaaS B:
- May have different payload schema than SaaS A
- Separate `SyncSaasBHandler` implementing `ISyncHandler`
- Both handlers registered in DI, scheduled by the same `SyncBackgroundService`
- Project scoped to `Project.SaasCode = "B"`

Environment variables:
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
**Sprint:** Sprint 3
**Story Points:** 8
**Labels:** backend, sync
**Description:**
When CRM data changes (customer status, subscription), instantly push to SaaS:

**Trigger events:**
- Customer.Status changed → notify SaaS
- Customer.Label changed → notify SaaS
- New ContactHistory created → notify SaaS

**Implementation:**
- Domain event: `CustomerStatusChangedEvent`, `ContactHistoryCreatedEvent`
- MediatR handler: `SaasCallbackHandler`
- HTTP POST to `Project.WebhookUrl` with payload (see api-contracts.md for format)
- Record attempt in `SyncLogs` (Direction=Outbound)
- Retry up to 3 times on 5xx errors with 2s, 5s, 10s backoff
- Do NOT block the original request — fire outbound callback on background thread

**Acceptance Criteria:**
- [ ] Status change triggers outbound HTTP POST within 200ms of request returning
- [ ] SyncLog records outbound attempt with status
- [ ] Failure does not affect the primary CRM operation
- [ ] Retry logic logs each attempt

---

### 🎫 ION-030
**Title:** [BACKEND] Sync admin endpoints (SuperAdmin only)
**Sprint:** Sprint 3
**Story Points:** 3
**Labels:** backend, sync
**Description:**
Admin endpoints for visibility and manual control:
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
**Title:** [BACKEND] SyncLog entity + migration + repository
**Sprint:** Sprint 3
**Story Points:** 3
**Labels:** backend, database
**Description:**
Implement the `SyncLog` entity from the DB schema:
```
Id, ProjectId, SaasSource (A|B), Direction (Inbound|Outbound),
Status (Success|Failed|Retrying), Payload (jsonb), RecordCount,
AttemptCount, NextRetryAt, Error, CreatedAt
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
**Sprint:** Sprint 3
**Story Points:** 5
**Labels:** frontend, sync
**Description:**
SuperAdmin-only section in the app:
- `/admin/sync` route
- Cards: Last sync time per project, success/failure counts (last 24h)
- Sync log table: Project, Direction, Source, Status, RecordCount, Time
- "Sync Şimdi" button per project → calls `POST /admin/sync/trigger/:id`
- Auto-refresh every 60 seconds

**Acceptance Criteria:**
- [ ] Hidden from non-SuperAdmin users (route guard)
- [ ] Manual trigger shows toast on success
- [ ] Log table paginates

---

### 🎫 ION-033
**Title:** [DEVOPS] Environment secrets for sync service
**Sprint:** Sprint 3
**Story Points:** 2
**Labels:** devops
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
**Sprint:** Sprint 3
**Story Points:** 5
**Labels:** backend, sync
**Description:**
Handle edge cases in sync logic:
- If SaaS sends a customer that was manually deleted in CRM → re-create or skip? Policy: re-create (SaaS is source of truth for existence)
- If same ExternalId appears in two SaaS A payloads simultaneously → use `ON CONFLICT (ExternalId, ProjectId) DO UPDATE` or EF upsert pattern
- If SaaS B and SaaS A have overlapping ExternalIds → namespaced by `SaasSource` in key
- Write integration test scenarios for each case

**Acceptance Criteria:**
- [ ] Duplicate sync run produces no duplicate records
- [ ] Deleted-then-resynced customer is restored correctly
- [ ] Cross-SaaS ExternalId collision does not corrupt data

---

# ⏳ SPRINT 4 — Sales Pipeline & Performance Tracking
**Status:** PLANNED
**Goal:** Build the full Opportunities / Sales Pipeline feature set, user performance reports, and notification system for sales teams.
**Duration:** 3–4 days | **Agents:** Backend Agent, Frontend Agent | **Points:** 37

---

### 🎫 ION-035
**Title:** [BACKEND] Opportunities entity and CRUD
**Sprint:** Sprint 4
**Story Points:** 5
**Labels:** backend
**Description:**
New `Opportunities` table:
```
Id (UUID), ProjectId, CustomerId, OwnerId (UserId),
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
- `PUT /api/v1/opportunities/{id}/stage` — move stage, log stage change in AuditLog

**Acceptance Criteria:**
- [ ] All CRUD endpoints working
- [ ] Stage change writes AuditLog entry
- [ ] Value and ExpectedCloseDate required on create

---

### 🎫 ION-036
**Title:** [FRONTEND] Opportunities Kanban board
**Sprint:** Sprint 4
**Story Points:** 8
**Labels:** frontend
**Description:**
`/opportunities` route with Kanban view:
- Columns: Lead, Qualified, Proposal Sent, Negotiation, Won, Lost
- Cards: customer name, opportunity title, value (TRY formatted), expected close date
- Drag-and-drop between stages (calls `PUT /opportunities/{id}/stage`)
- Click card → opportunity detail drawer
- "Yeni Fırsat" button → create form
- Summary bar: total pipeline value, count per stage

**Acceptance Criteria:**
- [ ] Drag-and-drop works on desktop and touch (mobile)
- [ ] Stage move triggers API call and optimistic UI update
- [ ] Total value updates after stage move

---

### 🎫 ION-037
**Title:** [BACKEND] Sales performance report endpoints
**Sprint:** Sprint 4
**Story Points:** 5
**Labels:** backend
**Description:**
`GET /api/v1/reports/sales-performance`
- Per user: contact count, opportunities created, won value, conversion rate
- Filter: `?userId=&from=&to=&projectId=`
- SuperAdmin sees all projects; ProjectAdmin sees their project

`GET /api/v1/reports/pipeline-forecast`
- Expected revenue by month (grouped by ExpectedCloseDate month)
- Stage-weighted probability: Lead=10%, Qualified=30%, Proposal=50%, Negotiation=75%, Won=100%

**Acceptance Criteria:**
- [ ] Performance report returns per-user stats
- [ ] Forecast groups by month correctly
- [ ] Role-based access enforced

---

### 🎫 ION-038
**Title:** [FRONTEND] Sales performance & forecast charts
**Sprint:** Sprint 4
**Story Points:** 5
**Labels:** frontend
**Description:**
`/reports` route:
1. **Sales Leaderboard** — table: Rep name, contacts made, opps created, won value
2. **Pipeline Forecast** — bar chart by month showing expected revenue
3. **Win Rate Trend** — line chart over last 6 months
Date range picker to filter all charts

**Acceptance Criteria:**
- [ ] Leaderboard sortable by any column
- [ ] Charts responsive on mobile
- [ ] Date range filter applies to all charts simultaneously

---

### 🎫 ION-039
**Title:** [BACKEND] In-app notification system
**Sprint:** Sprint 4
**Story Points:** 5
**Labels:** backend
**Description:**
Lightweight notification system:
```
Notifications table:
  Id, UserId, ProjectId, Type (enum), Message, EntityType, EntityId,
  IsRead, CreatedAt
```
Type enum: `TaskDue=0, PipelineReminder=1, OpportunityStageChange=2, SyncFailure=3`

Events that create notifications:
- Task due date approaching (24h before)
- Pipeline call scheduled for today
- Opportunity moved to Won or Lost
- Sync failure

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
**Sprint:** Sprint 4
**Story Points:** 3
**Labels:** frontend
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
**Title:** [BACKEND] Task due-date reminder background job
**Sprint:** Sprint 4
**Story Points:** 3
**Labels:** backend
**Description:**
Extend `SyncBackgroundService` (or create `ReminderBackgroundService`) to run hourly:
- Query tasks due in next 24 hours that have not been notified yet
- Create `Notification` records for assigned user
- Query pipeline entries scheduled for today that have not been notified
- Create `Notification` records for responsible sales rep

**Acceptance Criteria:**
- [ ] Tasks due tomorrow appear in notifications
- [ ] Same task does not create duplicate notification on next run
- [ ] Pipeline reminders fire on day-of

---

### 🎫 ION-042
**Title:** [FRONTEND] Customer notes full implementation
**Sprint:** Sprint 4
**Story Points:** 3
**Labels:** frontend
**Description:**
Customer detail page — Notes tab:
- List all notes (newest first): author, timestamp, content
- "Not Ekle" inline text area (no modal needed)
- Submit on Ctrl+Enter or button click
- Edit note (own notes only) — inline edit
- Delete note (own notes only) — confirm dialog
- Notes update without page refresh (React Query invalidation)

**Acceptance Criteria:**
- [ ] Notes list/add/edit/delete all functional
- [ ] Cannot edit/delete another user's note
- [ ] Mobile: full-width note cards

---

# ⏳ SPRINT 5 — Frontend Polish & UX
**Status:** PLANNED
**Goal:** Complete all remaining frontend screens, ensure full mobile-first responsiveness, implement global search, refine UX, and prepare for production.
**Duration:** 3–4 days | **Agent:** Frontend Agent | **Points:** 38

---

### 🎫 ION-043
**Title:** [FRONTEND] Global search (Cmd+K)
**Sprint:** Sprint 5
**Story Points:** 5
**Labels:** frontend
**Description:**
Command-palette style global search:
- Keyboard shortcut: Cmd+K (Mac) / Ctrl+K (Windows)
- Search across: Customers (name, email, phone), Opportunities (title), Pipeline entries
- `GET /api/v1/search?q=` endpoint (backend: Sprint 5 ION-044)
- Results grouped by type with icons
- Click result → navigate to entity
- Recent searches stored in localStorage

**Acceptance Criteria:**
- [ ] Opens on Cmd+K
- [ ] Results appear within 300ms (debounced 200ms)
- [ ] Keyboard navigation (↑↓ Enter Esc)
- [ ] Mobile: accessible via search icon in header

---

### 🎫 ION-044
**Title:** [BACKEND] Global search endpoint
**Sprint:** Sprint 5
**Story Points:** 3
**Labels:** backend
**Description:**
`GET /api/v1/search?q={term}&limit=10`
- PostgreSQL `ILIKE` across: Customer (FullName, Email, Phone, Company), Opportunity (Title)
- Results: `{ type: "customer"|"opportunity"|"pipeline", id, display, subtitle }`
- Project-scoped (SuperAdmin can add `?projectId=`)
- Index on Customer(FullName), Customer(Email), Customer(Phone)

**Acceptance Criteria:**
- [ ] Returns mixed-type results
- [ ] Response under 100ms for typical queries
- [ ] Minimum 2 characters required

---

### 🎫 ION-045
**Title:** [FRONTEND] User management pages (SuperAdmin)
**Sprint:** Sprint 5
**Story Points:** 5
**Labels:** frontend
**Description:**
`/admin/users` — SuperAdmin only:
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
**Title:** [BACKEND] User management endpoints (SuperAdmin)
**Sprint:** Sprint 5
**Story Points:** 3
**Labels:** backend
**Description:**
- `GET /api/v1/admin/users` — all users with roles
- `POST /api/v1/admin/users` — create user, auto-assign to project with role
- `PUT /api/v1/admin/users/{id}` — update user info, roles
- `DELETE /api/v1/admin/users/{id}` — deactivate (soft)
- `POST /api/v1/admin/users/{id}/reset-password` — generate reset token
- `GET /api/v1/admin/projects` — all projects
- `POST /api/v1/admin/projects` — create project
- `PUT /api/v1/admin/projects/{id}` — update webhook URL, sync config

**Acceptance Criteria:**
- [ ] All SuperAdmin-only routes return 403 for non-superadmin
- [ ] Create user sends welcome email (if SMTP configured)
- [ ] Project creation validates SaasCode uniqueness

---

### 🎫 ION-047
**Title:** [FRONTEND] User profile & settings page
**Sprint:** Sprint 5
**Story Points:** 3
**Labels:** frontend
**Description:**
`/profile` route (all users):
- Display name, email, role, assigned projects
- "Şifre Değiştir" section: current + new + confirm
- Theme toggle (dark/light) — persisted in localStorage
- Language: Turkish only (but structure for future i18n)

**Acceptance Criteria:**
- [ ] Password change calls `PUT /api/v1/auth/change-password`
- [ ] Theme toggle persists across sessions

---

### 🎫 ION-048
**Title:** [FRONTEND] Mobile navigation overhaul
**Sprint:** Sprint 5
**Story Points:** 5
**Labels:** frontend
**Description:**
Ensure full mobile-first UX:
- Replace sidebar with bottom navigation bar on mobile (≤768px)
- Bottom nav: Dashboard, Customers, Pipeline, Opportunities, More (...)
- "More" opens sheet with: Reports, Contact Histories, Admin, Profile
- All tables switch to card-list view on mobile
- All forms use full-screen modal on mobile
- Swipe-to-delete on customer/pipeline list items (mobile)

**Acceptance Criteria:**
- [ ] No horizontal scroll on any screen at 375px width
- [ ] Bottom nav visible on all main routes (mobile)
- [ ] All touch targets ≥ 44×44px

---

### 🎫 ION-049
**Title:** [FRONTEND] Empty states, loading skeletons, error boundaries
**Sprint:** Sprint 5
**Story Points:** 3
**Labels:** frontend
**Description:**
Polish pass on all screens:
- **Empty states**: Customer list (no customers), Pipeline (no upcoming calls), Opportunities (no opps) — each with CTA button
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
**Sprint:** Sprint 5
**Story Points:** 5
**Labels:** frontend
**Description:**
Enhanced customer list:
- Multi-select label filter (AND logic)
- Multi-select status filter
- Company name filter (text input)
- "Son Görüşme" date range filter
- Sort by: Created date, Last contact, Name
- **CSV Export**: `GET /api/v1/customers/export?format=csv` with active filters
- Persist filters in URL params (shareable links)

**Acceptance Criteria:**
- [ ] Combined filters work correctly
- [ ] CSV download includes all columns (Name, Email, Phone, Company, Label, Status, LastContact)
- [ ] URL with filters can be shared and reloads correctly

---

### 🎫 ION-051
**Title:** [BACKEND] Customer CSV export endpoint
**Sprint:** Sprint 5
**Story Points:** 2
**Labels:** backend
**Description:**
`GET /api/v1/customers/export?format=csv&label=&status=&company=`
- Streams CSV response (Content-Type: text/csv)
- Applies same filters as customer list
- Columns: Id, FullName, Email, Phone, Company, Label, Status, CreatedAt, LastContactDate
- Respects tenant (ProjectId) scoping

**Acceptance Criteria:**
- [ ] Returns valid CSV
- [ ] All active filters applied
- [ ] File named `customers-{date}.csv`

---

### 🎫 ION-052
**Title:** [FRONTEND] Accessibility & i18n prep
**Sprint:** Sprint 5
**Story Points:** 4
**Labels:** frontend
**Description:**
- Add `aria-label` to all icon-only buttons
- Ensure keyboard-navigable forms
- Add `lang="tr"` to HTML root
- Extract all Turkish string literals to `locales/tr.ts` constants file
- Ensure color contrast meets WCAG AA in both dark and light modes

**Acceptance Criteria:**
- [ ] No axe-core accessibility errors on main screens
- [ ] All strings in locales file (ready for future language addition)

---

# ⏳ SPRINT 6 — Hardening, Testing & Production
**Status:** PLANNED
**Goal:** Validate the MSSQL migration completeness, write full test suite, perform security audit, and deliver production-ready deployment with monitoring.
**Duration:** 3–4 days | **Agents:** Backend Agent, DevOps Agent, Testing Agent | **Points:** 40

---

### 🎫 ION-053
**Title:** [BACKEND] Validate & harden one-time data migration
**Sprint:** Sprint 6
**Story Points:** 5
**Labels:** backend, migration
**Description:**
The 639-customer / 892-contact migration ran in Sprint 1. Now validate it completely:
- Re-run migration script against dev DB; compare row counts
- Verify all nullable fields handled (no "N/A" strings in phone/email)
- Confirm Turkish characters (ğ, ş, ı, ç) preserved correctly (UTF-8)
- Verify `ExternalId` mappings intact (needed for SaaS sync deduplication)
- Write a reconciliation SQL script: count per-table source vs target
- Document any data quality issues found

**Acceptance Criteria:**
- [ ] 639 customers in new DB with correct field mapping
- [ ] 892 contact histories linked to correct customers
- [ ] No encoding issues in Turkish characters
- [ ] Reconciliation script passes

---

### 🎫 ION-054
**Title:** [TESTING] Backend unit test suite (Domain + Application)
**Sprint:** Sprint 6
**Story Points:** 8
**Labels:** testing, backend
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
**Sprint:** Sprint 6
**Story Points:** 5
**Labels:** testing, backend
**Description:**
Using `WebApplicationFactory<Program>` + Testcontainers (PostgreSQL):
- Auth: login → get token → use token
- Customer CRUD end-to-end
- Label/Status filter queries
- Merge workflow (Potansiyel → Müşteri)
- Pipeline create → add contact → verify status=Tamamlandi
- Sync endpoint: send SaaS payload → verify customer upserted
- Multi-tenant isolation: user from Project A cannot see Project B data

**Acceptance Criteria:**
- [ ] All integration tests pass against real PostgreSQL (Testcontainers)
- [ ] Multi-tenant isolation tested explicitly
- [ ] Tests run in CI via GitHub Actions

---

### 🎫 ION-056
**Title:** [TESTING] Frontend E2E test suite
**Sprint:** Sprint 6
**Story Points:** 5
**Labels:** testing, frontend
**Description:**
Using Playwright:
- Login → see dashboard
- Add customer → appears in list
- Change customer label → badge updates
- Add pipeline entry → appears in pipeline page
- Mark pipeline as complete → status badge changes
- Create opportunity → drag to "Qualified" stage
- SuperAdmin: view sync logs page

**Acceptance Criteria:**
- [ ] All E2E tests pass against dev environment
- [ ] Tests run in CI
- [ ] Screenshots on failure uploaded as GitHub Actions artifact

---

### 🎫 ION-057
**Title:** [DEVOPS] Security audit and hardening
**Sprint:** Sprint 6
**Story Points:** 5
**Labels:** devops, security
**Description:**
Pre-production security checklist:
- Run `dotnet-retire` / OWASP dependency check on NuGet packages
- Run `npm audit` on frontend
- Verify all endpoints have `[Authorize]` attribute (no accidental open endpoints)
- Check no secrets in git history (`git-secrets` or `trufflehog`)
- Add rate limiting middleware: `AspNetCoreRateLimit` (100 req/min per IP)
- Add CORS policy: only allow Railway frontend URL
- Add security headers: HSTS, X-Frame-Options, CSP
- Verify JWT expiry/refresh works correctly
- Test that SuperAdmin token cannot be forged by modifying claims

**Acceptance Criteria:**
- [ ] Zero critical/high NuGet or npm vulnerabilities
- [ ] No unprotected endpoints
- [ ] Rate limiting configured
- [ ] Security headers present on all responses

---

### 🎫 ION-058
**Title:** [DEVOPS] Production deployment and monitoring
**Sprint:** Sprint 6
**Story Points:** 5
**Labels:** devops
**Description:**
Final production deployment:
- Deploy to Railway prod (API + Frontend services)
- Run EF Core migrations on prod Neon DB (`dotnet ef database update`)
- Verify HTTPS on prod domains
- Configure Railway health checks (ping `/health` every 30s)
- Add structured logging: Serilog → Railway logs (JSON format)
- Add OpenTelemetry traces (optional, if time allows)
- Set all prod environment variables in Railway dashboard
- Verify SaaS A and B webhooks point to prod API URL

**Acceptance Criteria:**
- [ ] Prod API responds at `https://ion-crm-api-production.up.railway.app/health`
- [ ] Prod frontend loads at `https://ion-crm-frontend-production.up.railway.app`
- [ ] All migrations applied to prod DB
- [ ] Health check green in Railway dashboard

---

### 🎫 ION-059
**Title:** [DEVOPS] CI/CD pipeline enhancement
**Sprint:** Sprint 6
**Story Points:** 3
**Labels:** devops
**Description:**
Enhance the existing GitHub Actions pipeline:
- Add test stage: `dotnet test` runs on every PR
- Add Playwright E2E stage: runs on merge to main
- Add build status badge to README
- Branch protection: require PR review + all checks green before merge to main
- Add `dotnet format` check to PR workflow
- Separate dev and prod deploy jobs (dev auto-deploys on main push; prod requires manual approval)

**Acceptance Criteria:**
- [ ] PRs blocked from merging if tests fail
- [ ] Prod deploy requires GitHub workflow approval
- [ ] Badge shows in README

---

### 🎫 ION-060
**Title:** [BACKEND] Health check and readiness probe endpoint
**Sprint:** Sprint 6
**Story Points:** 2
**Labels:** backend, devops
**Description:**
Add ASP.NET Core Health Checks:
- `GET /health` — overall status: Healthy/Degraded/Unhealthy
- Checks: DB connectivity (Neon), SaaS A reachability, SaaS B reachability, last sync age (warning if >30 min)
- `GET /health/ready` — simple 200 for Railway readiness probe
- Add `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`

**Acceptance Criteria:**
- [ ] `/health` returns JSON with component statuses
- [ ] DB failure returns Unhealthy
- [ ] Sync lag > 30 min returns Degraded

---

## 📊 SPRINT SUMMARY TABLE

| Sprint | Name | Status | Stories | Points | Duration | Agents |
|--------|------|--------|---------|--------|----------|--------|
| Sprint 0 | Analysis & Architecture | ✅ DONE | 4 | 21 | 2–3 days | Architect |
| Sprint 1 | Foundation | ✅ DONE | 9 | 34 | 3–4 days | Backend, Frontend, DevOps |
| Sprint 2 | Customer Core Enhancement | 🔄 ACTIVE | 13 | 42 | 3–4 days | Backend, Frontend |
| Sprint 3 | Sync Service | ⏳ PLANNED | 8 | 39 | 3–4 days | Backend, DevOps |
| Sprint 4 | Sales Pipeline & Performance | ⏳ PLANNED | 8 | 37 | 3–4 days | Backend, Frontend |
| Sprint 5 | Frontend Polish & UX | ⏳ PLANNED | 10 | 38 | 3–4 days | Frontend |
| Sprint 6 | Hardening, Testing & Production | ⏳ PLANNED | 8 | 40 | 3–4 days | Backend, DevOps, Testing |
| **TOTAL** | | | **60** | **251** | **~22–27 days** | |

---

## 🚧 OPEN RISKS & DEPENDENCIES

| Risk | Sprint | Mitigation |
|------|--------|-----------|
| SaaS A/B API credentials not yet provided | Sprint 3 | Use mock server for development; real creds swapped in via env vars |
| SaaS payload schemas unknown | Sprint 3 | Design handlers to be schema-driven (JSON config per SaaS source) |
| Neon DB connection pool limits (free tier) | Sprint 3+ | Use Neon pooler URL; add connection pool settings in EF Core |
| Playwright tests flaky on Railway CI | Sprint 6 | Add retry logic; use stable selectors (data-testid) |
| Production migration needs downtime | Sprint 6 | Schedule maintenance window; migrations are additive only (no destructive DDL) |

---
*Last updated: 2026-03-24 by Orchestrator Agent*
