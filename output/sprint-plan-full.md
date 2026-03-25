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
| **Legacy Migration** | One-time MSSQL .bak → PostgreSQL (EMS.Companies + PotentialCustomers + CustomerInterviews) ✅ DONE |
| **Frontend** | React 18, shadcn/ui, Zustand, React Query, dark mode default, Turkish language, mobile-first |
| **Deploy** | Railway via GitHub Actions CI/CD — Dev + Prod environments |
| **DB** | Neon PostgreSQL (dev: ep-royal-grass / prod: ep-purple-sound) |
| **Old DB** | `/input/database/crm.bak` — 4.4 MB MSSQL backup — analyzed ✅ — migrated ✅ |

---

## 🗂️ LEGACY DATABASE — MIGRATION SUMMARY

**Source:** `crm.bak` (4.4 MB) contains two logical databases: **IONCRM** and **EMS**

| Old Table | → New Table | Records | Status |
|-----------|-------------|---------|--------|
| `EMS.dbo.Companies` | `Customers` (Status=Musteri) | 639 | ✅ Migrated |
| `dbo.PotentialCustomers` | `Customers` (Status=Potansiyel) | included | ✅ Migrated |
| `dbo.CustomerInterviews` | `ContactHistories` | 892 | ✅ Migrated |
| `dbo.AppointedInterviews` | `ContactHistories` (historical) | included | ✅ Migrated |
| `dbo.Users` | ❌ Not migrated | — | Rebuilt fresh |
| `dbo.InterviewRejectStatus` | ❌ Not migrated | — | Replaced by enums |

**Key field mappings applied:**
- Old `Adress` (typo) → new `address`
- `isPotantialCustomer` bit → `CustomerStatus` enum (Musteri=0, Potansiyel=1, Demo=2)
- Old int IDs → new GUIDs; old ID stored in `LegacyId` for traceability
- `CustomerInterviews.Type` → `ContactType` enum (Call, Email, Meeting, Note)
- Both Companies and PotentialCustomers unified into single `Customers` table

---

## 🏗️ DB SCHEMA (Neon PostgreSQL)

### Core Tables
```
Customers          — Id(uuid), CompanyName, ContactName, Phone, Email, Address,
                     TaxNumber, TaxUnit, Label(enum), Status(enum), LegacyId(int?),
                     ProjectId(uuid FK), CreatedByUserId(uuid), IsDeleted, CreatedAt, UpdatedAt

ContactHistories   — Id(uuid), CustomerId(uuid FK), UserId(uuid FK), Type(enum),
                     ContactResult(enum?), Content, Subject, ContactPersonName,
                     ContactPersonPhone, ContactedAt, ProjectId(uuid), IsDeleted, CreatedAt

CustomerTasks      — Id(uuid), CustomerId(uuid FK), AssignedToUserId(uuid FK),
                     Title, Description, DueDate, IsCompleted, ProjectId(uuid), CreatedAt

Pipelines          — Id(uuid), CustomerId(uuid FK), ProjectId(uuid FK),
                     PlannedDate(timestamptz), Notes(text), Status(enum:Bekliyor/Tamamlandi/Iptal),
                     IsDeleted, CreatedAt, UpdatedAt

Users              — Id(uuid), Email, PasswordHash, FullName, Role(enum), IsActive,
                     CreatedAt, UpdatedAt

UserProjects       — UserId(uuid FK), ProjectId(uuid FK), Role(enum) — junction table

Projects           — Id(uuid), Name, SaaSType(enum:SaaSA/SaaSB), SyncUrl,
                     CallbackUrl, IsActive, CreatedAt

SyncLogs           — Id(uuid), ProjectId(uuid FK), SyncType, Status, RecordsProcessed,
                     ErrorMessage, StartedAt, CompletedAt

SaaSCallbackLogs   — Id(uuid), ProjectId(uuid FK), EventType, Payload(jsonb),
                     HttpStatus, AttemptCount, SentAt, ResponseAt
```

### Enums
```
CustomerLabel:  YuksekPotansiyel=0, Potansiyel=1, Notr=2, Vasat=3, Kotu=4
CustomerStatus: Musteri=0, Potansiyel=1, Demo=2
ContactType:    Call=0, Email=1, Meeting=2, Note=3
ContactResult:  Olumlu=0, Olumsuz=1, BaskaTedarikci=2
PipelineStatus: Bekliyor=0, Tamamlandi=1, Iptal=2
UserRole:       SuperAdmin=0, ProjectAdmin=1, SalesManager=2, SalesRep=3, Accounting=4
SaaSType:       SaaSA=0, SaaSB=1
```

---

## ⚠️ APPROVAL GATES

| Gate | Sprint | Status |
|------|--------|--------|
| Sprint plan reviewed | Pre-Sprint 0 | ✅ Approved |
| DB schema design reviewed | Sprint 0 | ✅ Approved |
| Sprint 0 complete | Sprint 0 → 1 | ✅ Approved |
| Sprint 1 complete | Sprint 1 → 2 | ✅ Approved |
| **Sprint 2 complete** | **Sprint 2 → 3** | **🔄 IN PROGRESS** |
| Sprint 3 complete | Sprint 3 → 4 | ⏳ Pending |
| Sprint 4 complete | Sprint 4 → 5 | ⏳ Pending |
| Sprint 5 complete | Sprint 5 → 6 | ⏳ Pending |
| Production deployment | Sprint 6 | ⏳ Pending |

---

---

# ✅ SPRINT 0 — Analysis & Architecture
**Status:** COMPLETED ✅
**Goal:** Analyze legacy MSSQL .bak, design PostgreSQL schema, define API contracts, scaffold solution structure.
**Duration:** Completed | **Agent:** Architect Agent | **Story Points:** 21

| Ticket | Title | Points | Status |
|--------|-------|--------|--------|
| ION-001 | [ARCHITECT] Analyze MSSQL .bak and extract legacy schema | 5 | ✅ Done |
| ION-002 | [ARCHITECT] Design PostgreSQL target schema (all tables + enums) | 8 | ✅ Done |
| ION-003 | [ARCHITECT] Define API contracts and OpenAPI spec | 5 | ✅ Done |
| ION-004 | [ARCHITECT] Define solution folder structure and scaffold plan | 3 | ✅ Done |

**Deliverables produced:**
- `input/db_analysis.md` — Full legacy DB analysis with migration plan
- PostgreSQL schema design (Customers, ContactHistories, Tasks, Users, Projects, Pipelines, Opportunities, SyncLogs)
- API contract spec (REST endpoints, auth flow, sync endpoints)
- Clean Architecture folder blueprint
- `.env.example` with all required environment variables

---

# ✅ SPRINT 1 — Foundation
**Status:** COMPLETED ✅
**Goal:** Build the production-ready foundation: solution scaffold, authentication, core entities, sync endpoints, CI/CD, and data migration.
**Duration:** Completed | **Agent:** Backend Agent + Frontend Agent | **Story Points:** 55

| Ticket | Title | Points | Status |
|--------|-------|--------|--------|
| ION-005 | [BACKEND] .NET Core 8 Clean Architecture solution scaffold | 8 | ✅ Done |
| ION-006 | [BACKEND] JWT Authentication (login, logout, refresh, /me) | 8 | ✅ Done |
| ION-007 | [BACKEND] Multi-tenant middleware (ProjectId scope from JWT) | 5 | ✅ Done |
| ION-008 | [BACKEND] Customers CRUD (temel — GET list, GET by id, POST, PUT, DELETE) | 8 | ✅ Done |
| ION-009 | [BACKEND] ContactHistories CRUD | 5 | ✅ Done |
| ION-010 | [BACKEND] CustomerTasks CRUD | 3 | ✅ Done |
| ION-011 | [BACKEND] Sync endpoints (SaaS A + SaaS B inbound) | 5 | ✅ Done |
| ION-012 | [FRONTEND] React app scaffold (dark mode, sidebar, login screen, routing) | 8 | ✅ Done |
| ION-013 | [DEVOPS] Railway deploy (dev + prod), GitHub Actions CI/CD, Neon DB setup | 5 | ✅ Done |
| ION-013b | [MIGRATION] One-time data migration: 639 customers + 892 contact histories | 5 | ✅ Done |

**Key deliverables:**
- Live dev API: https://ion-crm-api-development.up.railway.app
- Live dev frontend: https://ion-crm-frontend-development.up.railway.app
- GitHub Actions CI/CD pipeline (build → test → deploy on push to main)
- 639 legacy customers migrated to Neon DB
- 892 legacy contact histories migrated

---

# 🔄 SPRINT 2 — Customer Core Enhancement
**Status:** ACTIVE 🔄 (Awaiting approval)
**Goal:** Fix critical data-display bugs, add Label/Status classification system, build Pipeline call-scheduling feature, implement Potansiyel→Müşteri atomic merge workflow, deliver all-contact-histories view, and add Dashboard analytics charts. By end of sprint the CRM is fully usable by sales reps day-to-day.
**Duration:** 3–4 days | **Agents:** Backend Agent + Frontend Agent | **Story Points:** 42 | **Est. Cost:** ~$18–28

### DB Migrations Required
```sql
ALTER TABLE "Customers" ADD "Label" integer NOT NULL DEFAULT 2;
  -- 0=YuksekPotansiyel, 1=Potansiyel, 2=Notr, 3=Vasat, 4=Kotu

ALTER TABLE "Customers" ADD "Status" integer NOT NULL DEFAULT 1;
  -- 0=Musteri, 1=Potansiyel, 2=Demo

ALTER TABLE "ContactHistories" ADD "ContactResult" integer NULL;
  -- 0=Olumlu, 1=Olumsuz, 2=BaskaTedarikci

CREATE TABLE "Pipelines" (
  "Id" uuid PRIMARY KEY,
  "CustomerId" uuid NOT NULL REFERENCES "Customers"("Id"),
  "ProjectId" uuid NOT NULL,
  "PlannedDate" timestamptz NOT NULL,
  "Notes" text,
  "Status" integer NOT NULL DEFAULT 0,
  "CreatedAt" timestamptz NOT NULL,
  "UpdatedAt" timestamptz NOT NULL,
  "IsDeleted" boolean NOT NULL DEFAULT false
);
```

### New API Endpoints
```
GET    /api/v1/customers              (add ?label= ?status= filters)
POST   /api/v1/customers/{id}/merge   (atomic Potansiyel→Müşteri merge)
GET    /api/v1/contact-histories      (with ?from= ?to= ?type= ?result= filters)
PUT    /api/v1/contact-histories/{id}
GET    /api/v1/pipelines              (default: next 7 days)
POST   /api/v1/pipelines
PUT    /api/v1/pipelines/{id}
DELETE /api/v1/pipelines/{id}
POST   /api/v1/pipelines/{id}/contact
GET    /api/v1/dashboard/stats
GET    /api/v1/dashboard/pipeline
```

### New Frontend Routes
```
/customers/:id           Customer detail page
/customers/:id/edit      Customer edit form
/contact-histories       All contact histories with filters
/pipeline                Call planning / pipeline manager
```

### Stories

| Ticket | Title | Agent | Points | Priority | Status |
|--------|-------|-------|--------|----------|--------|
| ION-014 | [BACKEND] Fix: Customer list API not returning records | Backend | 2 | 🔴 CRITICAL | ⏳ |
| ION-015 | [FRONTEND] Fix: Customer add form renders blank | Frontend | 2 | 🔴 CRITICAL | ⏳ |
| ION-016 | [FRONTEND] Add customer detail, edit, and delete pages | Frontend | 5 | 🔴 HIGH | ⏳ |
| ION-017 | [BACKEND] Customer Label system (enum + migration + filter) | Backend | 3 | 🔴 HIGH | ⏳ |
| ION-018 | [BACKEND] Customer Status system (enum + migration + filter) | Backend | 3 | 🔴 HIGH | ⏳ |
| ION-019 | [FRONTEND] Label and Status badges + filters on customer list | Frontend | 3 | 🔴 HIGH | ⏳ |
| ION-020 | [BACKEND] Potansiyel → Müşteri merge endpoint (transactional) | Backend | 5 | 🟠 HIGH | ⏳ |
| ION-021 | [FRONTEND] Müşteriye Bağla merge UI (modal + confirmation) | Frontend | 3 | 🟡 MEDIUM | ⏳ |
| ION-022 | [BACKEND] ContactResult field + all-histories paginated endpoint | Backend | 3 | 🟡 MEDIUM | ⏳ |
| ION-023 | [FRONTEND] All Contact Histories page (/contact-histories) | Frontend | 3 | 🟡 MEDIUM | ⏳ |
| ION-024 | [BACKEND] Pipeline CRUD + /contact sub-action (atomic) | Backend | 5 | 🔴 HIGH | ⏳ |
| ION-025 | [FRONTEND] Pipeline page + Pipeline Ekle button on customer detail | Frontend | 4 | 🔴 HIGH | ⏳ |
| ION-026 | [FULL-STACK] Dashboard — Pipeline widget + analytics charts | Both | 5 | 🟡 MEDIUM | ⏳ |

**Execution order:**
1. **Phase 1 (parallel):** ION-014 (Backend) + ION-015 + ION-016 (Frontend)
2. **Phase 2 (backend-first):** ION-017+018 → ION-019 | ION-020 → ION-021 | ION-022 → ION-023 | ION-024 → ION-025
3. **Phase 3 (dashboard):** ION-026 (requires all above)

---

# ⏳ SPRINT 3 — Sync Engine
**Status:** PLANNED ⏳
**Goal:** Build a robust background sync service that pulls from SaaS A and SaaS B every 15 minutes into CRM, and delivers instant HTTP callbacks from CRM back to SaaS platforms when subscriptions or status changes occur. Includes webhook signature validation, retry logic, and sync audit logging.
**Duration:** 3–4 days | **Agents:** Backend Agent | **Story Points:** 38 | **Est. Cost:** ~$15–22

### Architecture
```
[SaaS A API] ←—pull every 15min—— [BackgroundService (IHostedService)]
[SaaS B API] ←—pull every 15min—— [BackgroundService (IHostedService)]
                                              ↓
                                    [SyncLog + conflict resolver]
                                              ↓
                                    [Neon PostgreSQL]
                                              ↓ (on Customer status change / subscription event)
[SaaS A Webhook] ←—instant HTTP push—— [CallbackService]
[SaaS B Webhook] ←—instant HTTP push—— [CallbackService]
```

### Stories

| Ticket | Title | Agent | Points | Priority |
|--------|-------|-------|--------|----------|
| ION-027 | [BACKEND] SyncBackgroundService — IHostedService with 15-min timer | Backend | 5 | 🔴 HIGH |
| ION-028 | [BACKEND] SaaS A sync adapter (pull + upsert customers) | Backend | 8 | 🔴 HIGH |
| ION-029 | [BACKEND] SaaS B sync adapter (pull + upsert customers) | Backend | 8 | 🔴 HIGH |
| ION-030 | [BACKEND] Conflict resolution engine (SaaS wins / CRM wins / merge policy) | Backend | 5 | 🟠 HIGH |
| ION-031 | [BACKEND] SyncLog entity + logging middleware (start/complete/error) | Backend | 3 | 🟠 HIGH |
| ION-032 | [BACKEND] CRM → SaaS instant callback service (HTTP push on events) | Backend | 8 | 🔴 HIGH |
| ION-033 | [BACKEND] Callback retry logic (3 attempts, exponential backoff, dead-letter) | Backend | 3 | 🟡 MEDIUM |
| ION-034 | [FRONTEND] Sync status page (last sync time, records synced, errors) | Frontend | 3 | 🟡 MEDIUM |
| ION-035 | [BACKEND] Manual sync trigger endpoint (POST /api/v1/sync/trigger) | Backend | 2 | 🟡 MEDIUM |
| ION-036 | [FRONTEND] Sync status widget on Dashboard | Frontend | 2 | 🟡 LOW |

**Acceptance criteria (sprint-level):**
- Both SaaS adapters pull and upsert without data loss
- Sync completes within 2 minutes for up to 10,000 records
- Callbacks delivered within 5 seconds of CRM event
- Failed callbacks retried 3× with backoff; failures logged in SaaSCallbackLogs
- SuperAdmin can manually trigger a sync and see results immediately

**Technical notes:**
- Use `IHostedService` + `PeriodicTimer` (not Hangfire — keep dependencies minimal)
- Sync is idempotent — keyed on `LegacyId` or SaaS external ID
- Webhook payloads signed with HMAC-SHA256 (shared secret per project)
- SaaS A and SaaS B adapters are separate classes implementing `ISaaSyncAdapter`
- Dead-lettered callbacks stored in `SaaSCallbackLogs` with `AttemptCount` field

---

# ⏳ SPRINT 4 — User Management & Notifications
**Status:** PLANNED ⏳
**Goal:** Complete multi-user management (invite, roles, project assignment), add in-app notification system for pipeline reminders and sync alerts, and deliver advanced reporting/export features for sales managers.
**Duration:** 3–4 days | **Agents:** Backend Agent + Frontend Agent | **Story Points:** 44 | **Est. Cost:** ~$18–25

### Stories

| Ticket | Title | Agent | Points | Priority |
|--------|-------|-------|--------|----------|
| ION-037 | [BACKEND] User invite system (email invite → set password flow) | Backend | 5 | 🔴 HIGH |
| ION-038 | [BACKEND] Role-based access control middleware (enforce per-endpoint) | Backend | 5 | 🔴 HIGH |
| ION-039 | [BACKEND] Project assignment (assign user to project, change role) | Backend | 3 | 🔴 HIGH |
| ION-040 | [FRONTEND] User management screen (list, invite, edit role, deactivate) | Frontend | 5 | 🔴 HIGH |
| ION-041 | [FRONTEND] SuperAdmin panel (view all projects, all users, system health) | Frontend | 8 | 🟠 HIGH |
| ION-042 | [BACKEND] Notification entity + pipeline reminder scheduler (day-of alerts) | Backend | 5 | 🟡 MEDIUM |
| ION-043 | [BACKEND] Sync failure notification (alert on repeated sync errors) | Backend | 3 | 🟡 MEDIUM |
| ION-044 | [FRONTEND] Notification bell (in-app, real-time via polling or SSE) | Frontend | 4 | 🟡 MEDIUM |
| ION-045 | [BACKEND] Reports endpoints (sales by rep, contact stats, pipeline conversion) | Backend | 3 | 🟡 MEDIUM |
| ION-046 | [FRONTEND] Reports page (table + chart, date range filter, CSV export) | Frontend | 3 | 🟡 MEDIUM |

**Acceptance criteria (sprint-level):**
- SuperAdmin can invite a user who receives an email with a set-password link
- Roles enforced: SalesRep cannot access admin screens; ProjectAdmin cannot cross-project
- Pipeline items trigger a reminder notification on the day of
- Reports page exports CSV with correct encoding (UTF-8 BOM for Turkish chars)
- SuperAdmin panel shows live system health (last sync time, error count, user count)

**Technical notes:**
- Email via SMTP (configure Railway env vars); use MailKit
- SSE (Server-Sent Events) for real-time notifications — no SignalR to keep it simple
- CSV export: use CsvHelper library; include Turkish character handling
- RBAC: policy-based authorization (`[Authorize(Policy = "SalesManagerOnly")]`)

---

# ⏳ SPRINT 5 — Frontend Polish & Mobile
**Status:** PLANNED ⏳
**Goal:** Complete all remaining frontend screens, achieve true mobile-first experience across all pages, refine dark mode, add keyboard shortcuts, improve UX based on sales rep feedback, and optimize bundle size and loading performance.
**Duration:** 3–4 days | **Agents:** Frontend Agent | **Story Points:** 40 | **Est. Cost:** ~$14–20

### Stories

| Ticket | Title | Agent | Points | Priority |
|--------|-------|-------|--------|----------|
| ION-047 | [FRONTEND] Mobile nav — bottom tab bar on < 768px (replaces sidebar) | Frontend | 5 | 🔴 HIGH |
| ION-048 | [FRONTEND] Customer list — mobile card layout (swipe to call/edit) | Frontend | 4 | 🔴 HIGH |
| ION-049 | [FRONTEND] Pipeline page — mobile-optimized card stack | Frontend | 3 | 🟠 HIGH |
| ION-050 | [FRONTEND] Contact history inline creation — slide-up sheet on mobile | Frontend | 4 | 🟠 HIGH |
| ION-051 | [FRONTEND] Global search (customers + contact histories) | Frontend | 5 | 🟠 HIGH |
| ION-052 | [FRONTEND] Dark mode polish (contrast audit, fix remaining light-mode leaks) | Frontend | 3 | 🟡 MEDIUM |
| ION-053 | [FRONTEND] Loading skeletons for all data-heavy screens | Frontend | 3 | 🟡 MEDIUM |
| ION-054 | [FRONTEND] Error boundaries + offline/network error states | Frontend | 3 | 🟡 MEDIUM |
| ION-055 | [FRONTEND] Keyboard shortcuts (J/K navigation, N=new customer, S=search) | Frontend | 2 | 🟡 MEDIUM |
| ION-056 | [FRONTEND] Bundle size audit — code-split routes, lazy load charts | Frontend | 3 | 🟡 MEDIUM |
| ION-057 | [FRONTEND] PWA manifest + service worker (add to home screen) | Frontend | 3 | 🟡 LOW |
| ION-058 | [FRONTEND] i18n audit — ensure all strings use Turkish, no English leaks | Frontend | 2 | 🟡 LOW |

**Acceptance criteria (sprint-level):**
- All screens usable on 375px width (iPhone SE) with no horizontal scroll
- Lighthouse mobile score ≥ 85 (performance, accessibility)
- Dark mode has no white/light-background leaks
- Global search returns results in < 300ms (debounced, indexed)
- App can be installed as PWA on Android/iOS
- Bundle initial load < 200KB gzip

**Technical notes:**
- Bottom tab bar: 5 tabs (Home, Customers, Pipeline, Contacts, Menu)
- Swipe gestures: use `@use-gesture/react`
- Skeletons: shadcn/ui `Skeleton` component
- Code splitting: `React.lazy()` + `Suspense` per route
- PWA: Vite PWA plugin

---

# ⏳ SPRINT 6 — Testing, Security & Production Hardening
**Status:** PLANNED ⏳
**Goal:** Achieve production-ready quality with a full test suite (unit + integration + E2E), pass a security audit (OWASP top-10 review), configure production Neon DB with proper indexes and connection pooling, and execute the final production deployment with zero-downtime migration strategy.
**Duration:** 4–5 days | **Agents:** Backend Agent + Frontend Agent + DevOps Agent | **Story Points:** 48 | **Est. Cost:** ~$20–30

### Stories

| Ticket | Title | Agent | Points | Priority |
|--------|-------|-------|--------|----------|
| ION-059 | [BACKEND] Unit tests — Domain layer (entities, value objects, enums) | Backend | 5 | 🔴 HIGH |
| ION-060 | [BACKEND] Unit tests — Application layer (command/query handlers via MediatR) | Backend | 8 | 🔴 HIGH |
| ION-061 | [BACKEND] Integration tests — API endpoints (WebApplicationFactory + Neon test DB) | Backend | 8 | 🔴 HIGH |
| ION-062 | [BACKEND] Integration tests — Sync service + SaaS adapters (mock SaaS endpoints) | Backend | 5 | 🟠 HIGH |
| ION-063 | [FRONTEND] Component tests — critical forms (React Testing Library) | Frontend | 4 | 🟠 HIGH |
| ION-064 | [FRONTEND] E2E tests — happy paths (Playwright: login, add customer, pipeline) | Frontend | 5 | 🟠 HIGH |
| ION-065 | [BACKEND] Security audit — OWASP Top 10 review + fix findings | Backend | 5 | 🔴 HIGH |
| ION-066 | [BACKEND] Rate limiting (ASP.NET Core rate limiter middleware) | Backend | 2 | 🟠 HIGH |
| ION-067 | [BACKEND] Input validation audit (FluentValidation coverage 100%) | Backend | 3 | 🟠 HIGH |
| ION-068 | [DEVOPS] Production DB indexing (Customers, ContactHistories, Pipelines FK+date) | DevOps | 3 | 🔴 HIGH |
| ION-069 | [DEVOPS] Railway prod environment — env vars audit, secrets rotation | DevOps | 2 | 🔴 HIGH |
| ION-070 | [DEVOPS] Zero-downtime deploy strategy (Railway health checks, rollback plan) | DevOps | 3 | 🟠 HIGH |
| ION-071 | [BACKEND] API documentation — Swagger/OpenAPI complete and accurate | Backend | 2 | 🟡 MEDIUM |
| ION-072 | [DEVOPS] Monitoring — Railway metrics + Sentry error tracking | DevOps | 2 | 🟡 MEDIUM |

**Acceptance criteria (sprint-level):**
- Test coverage ≥ 80% on Application layer
- All OWASP Top-10 items reviewed; critical/high findings fixed
- Production DB has indexes on: `Customers(ProjectId, IsDeleted)`, `ContactHistories(CustomerId, ContactedAt)`, `Pipelines(ProjectId, PlannedDate, IsDeleted)`
- E2E test suite runs in < 3 minutes in CI
- Production deploy completes with 0 downtime (health check validated)
- Sentry integrated and receiving live error events

**Security checklist (ION-065):**
- [ ] SQL injection: EF Core parameterized queries only — no raw SQL with user input
- [ ] XSS: React JSX escaping + CSP headers
- [ ] CSRF: SameSite cookie on refresh token
- [ ] Auth bypass: test all endpoints with missing/invalid JWT
- [ ] IDOR: all queries scoped to `ProjectId` from JWT (not from request body)
- [ ] Rate limiting: login endpoint max 10 req/min per IP
- [ ] Secrets: no secrets in source code or git history
- [ ] Dependencies: `dotnet list package --vulnerable` + `npm audit`

---

## 📊 SPRINT SUMMARY TABLE

| Sprint | Name | Status | Stories | Points | Duration | Est. Cost | Agents |
|--------|------|--------|---------|--------|----------|-----------|--------|
| Sprint 0 | Analysis & Architecture | ✅ Done | 4 | 21 | 2-3 days | ~$5-8 | Architect |
| Sprint 1 | Foundation | ✅ Done | 10 | 55 | 4-5 days | ~$25-35 | Backend + Frontend |
| Sprint 2 | Customer Core Enhancement | 🔄 Active | 13 | 42 | 3-4 days | ~$18-28 | Backend + Frontend |
| Sprint 3 | Sync Engine | ⏳ Planned | 10 | 38 | 3-4 days | ~$15-22 | Backend (+ Frontend) |
| Sprint 4 | User Management & Notifications | ⏳ Planned | 10 | 44 | 3-4 days | ~$18-25 | Backend + Frontend |
| Sprint 5 | Frontend Polish & Mobile | ⏳ Planned | 12 | 40 | 3-4 days | ~$14-20 | Frontend |
| Sprint 6 | Testing, Security & Production | ⏳ Planned | 14 | 48 | 4-5 days | ~$20-30 | Backend + Frontend + DevOps |
| **TOTAL** | | | **73** | **288** | **~22-29 days** | **~$115-168** | |

---

## 🔌 API CONTRACT SUMMARY

### Authentication
```
POST /api/v1/auth/login          { email, password } → { accessToken, refreshToken }
POST /api/v1/auth/refresh        { refreshToken } → { accessToken, refreshToken }
POST /api/v1/auth/logout         (clears refresh token)
GET  /api/v1/auth/me             → { id, email, fullName, role, projects[] }
```

### Customers
```
GET    /api/v1/customers         ?page=&size=&label=&status=&search=
POST   /api/v1/customers         { companyName, contactName, phone, email, address, taxNumber, taxUnit, label, status }
GET    /api/v1/customers/:id
PUT    /api/v1/customers/:id
DELETE /api/v1/customers/:id     (soft delete)
POST   /api/v1/customers/:id/merge  { targetCustomerId }
```

### Contact Histories
```
GET    /api/v1/contact-histories        ?page=&size=&from=&to=&type=&result=&customerId=
POST   /api/v1/contact-histories        { customerId, type, contactResult, content, subject, contactPersonName, contactPersonPhone, contactedAt }
GET    /api/v1/contact-histories/:id
PUT    /api/v1/contact-histories/:id
DELETE /api/v1/contact-histories/:id
```

### Pipelines
```
GET    /api/v1/pipelines         ?from=&to= (default: next 7 days)
POST   /api/v1/pipelines         { customerId, plannedDate, notes }
PUT    /api/v1/pipelines/:id
DELETE /api/v1/pipelines/:id
POST   /api/v1/pipelines/:id/contact  { type, content, contactResult } → creates ContactHistory + marks pipeline Tamamlandi
```

### Dashboard
```
GET    /api/v1/dashboard/stats          → { totalCustomers, byLabel[], byStatus[], contactsByDay[30] }
GET    /api/v1/dashboard/pipeline       → Pipeline[] (next 7 days, Bekliyor only)
```

### Sync (Sprint 3)
```
POST   /api/v1/sync/trigger             (SuperAdmin only — manual sync)
GET    /api/v1/sync/logs                ?page=&projectId=
GET    /api/v1/sync/logs/:id
```

### Users / Admin (Sprint 4)
```
GET    /api/v1/users                    (ProjectAdmin+ scoped)
POST   /api/v1/users/invite             { email, role, projectId }
PUT    /api/v1/users/:id/role
DELETE /api/v1/users/:id               (deactivate)
GET    /api/v1/admin/projects           (SuperAdmin only)
GET    /api/v1/admin/health             (SuperAdmin only)
```

---

## 🧱 CLEAN ARCHITECTURE STRUCTURE
```
IonCrm.sln
├── src/
│   ├── IonCrm.Domain/            Pure entities, enums, domain exceptions, interfaces
│   ├── IonCrm.Application/       MediatR commands/queries, DTOs, FluentValidation, interfaces
│   ├── IonCrm.Infrastructure/    EF Core, Neon PostgreSQL, email, HTTP clients (SaaS adapters)
│   └── IonCrm.API/               Controllers, middleware (JWT, multi-tenant), Swagger, DI
├── frontend/                     React 18, TypeScript, shadcn/ui, Zustand, React Query
├── tests/
│   ├── IonCrm.Domain.Tests/
│   ├── IonCrm.Application.Tests/
│   └── IonCrm.Integration.Tests/
├── input/
│   ├── database/crm.bak          Legacy MSSQL backup (migrated ✅)
│   └── db_analysis.md            DB analysis (completed ✅)
└── output/                       Sprint plans, approval JSONs, docs
```

---

## 🌍 ENVIRONMENTS

| Env | API | Frontend | Database |
|-----|-----|----------|----------|
| Dev | https://ion-crm-api-development.up.railway.app | https://ion-crm-frontend-development.up.railway.app | ep-royal-grass-a9u9toyt-pooler.gwc.azure.neon.tech / neondb |
| Prod | https://ion-crm-api-production.up.railway.app | https://ion-crm-frontend-production.up.railway.app | ep-purple-sound-a9vyag84-pooler.gwc.azure.neon.tech / ioncrm |

---

*Last updated: 2026-03-25 by Orchestrator Agent*
