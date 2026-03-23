# ION CRM — PostgreSQL Database Schema Design
**Version:** 1.0.0
**Date:** 2026-03-23
**Designer:** Solution Architect Agent

---

## Design Principles
- Every table has: `id` (UUID), `created_at`, `updated_at`, `is_deleted` (soft delete)
- Tenant isolation via `project_id` on all business tables
- All foreign keys cascade soft-delete behavior at application layer (EF Core)
- Indexes on every FK column and common filter columns
- UUID primary keys (no sequential int IDs — prevents enumeration attacks)

---

## Tables

### 1. `projects` (Tenants)
```sql
CREATE TABLE projects (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        VARCHAR(200) NOT NULL,
    description TEXT,
    is_active   BOOLEAN NOT NULL DEFAULT true,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted  BOOLEAN NOT NULL DEFAULT false
);

CREATE INDEX idx_projects_is_active ON projects(is_active) WHERE is_deleted = false;
```

**EF Core Entity:** `Project`
**Notes:** Each row is one tenant (e.g., "Ioniva Muhasebe", "Ioniva Satis")

---

### 2. `users`
```sql
CREATE TABLE users (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email          VARCHAR(320) NOT NULL UNIQUE,
    password_hash  VARCHAR(72)  NOT NULL,   -- bcrypt, 60 chars output, 72 max input
    first_name     VARCHAR(100) NOT NULL,
    last_name      VARCHAR(100) NOT NULL,
    is_super_admin BOOLEAN NOT NULL DEFAULT false,
    is_active      BOOLEAN NOT NULL DEFAULT true,
    created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted     BOOLEAN NOT NULL DEFAULT false
);

CREATE UNIQUE INDEX idx_users_email ON users(email) WHERE is_deleted = false;
CREATE INDEX idx_users_is_super_admin ON users(is_super_admin) WHERE is_deleted = false;
```

**EF Core Entity:** `User`
**Notes:** `is_super_admin = true` bypasses all tenant filters

---

### 3. `user_project_roles` (Junction — User ↔ Project with Role)
```sql
CREATE TYPE user_role AS ENUM (
    'ProjectAdmin',
    'SalesManager',
    'SalesRep',
    'Accounting'
);

CREATE TABLE user_project_roles (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    project_id  UUID NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    role        user_role NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted  BOOLEAN NOT NULL DEFAULT false,
    UNIQUE (user_id, project_id)   -- one role per user per project
);

CREATE INDEX idx_upr_user_id    ON user_project_roles(user_id);
CREATE INDEX idx_upr_project_id ON user_project_roles(project_id);
```

**EF Core Entity:** `UserProjectRole`
**Notes:** A user can belong to multiple projects with different roles

---

### 4. `customers`
```sql
CREATE TYPE customer_status AS ENUM (
    'Lead',
    'Active',
    'Inactive',
    'Churned'
);

CREATE TYPE customer_segment AS ENUM (
    'SME',
    'Enterprise',
    'Startup',
    'Government',
    'Individual'
);

CREATE TABLE customers (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id       UUID NOT NULL REFERENCES projects(id),
    code             VARCHAR(50),                    -- internal customer code
    company_name     VARCHAR(300) NOT NULL,
    contact_name     VARCHAR(200),
    email            VARCHAR(320),
    phone            VARCHAR(50),
    address          TEXT,
    tax_number       VARCHAR(50),
    tax_unit         VARCHAR(100),
    status           customer_status NOT NULL DEFAULT 'Lead',
    segment          customer_segment,
    assigned_user_id UUID REFERENCES users(id),
    legacy_id        VARCHAR(50),                    -- old int ID from crm.bak migration
    created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted       BOOLEAN NOT NULL DEFAULT false
);

CREATE INDEX idx_customers_project_id       ON customers(project_id) WHERE is_deleted = false;
CREATE INDEX idx_customers_assigned_user_id ON customers(assigned_user_id) WHERE is_deleted = false;
CREATE INDEX idx_customers_status           ON customers(project_id, status) WHERE is_deleted = false;
CREATE INDEX idx_customers_company_name     ON customers USING gin(to_tsvector('simple', company_name));
CREATE INDEX idx_customers_legacy_id        ON customers(legacy_id) WHERE legacy_id IS NOT NULL;
```

**EF Core Entity:** `Customer`
**Migration source:** `EMS.dbo.Companies` + `dbo.PotentialCustomers`

---

### 5. `contact_history`
```sql
CREATE TYPE contact_type AS ENUM (
    'Call',
    'Email',
    'Meeting',
    'Note',
    'WhatsApp',
    'Visit'
);

CREATE TABLE contact_history (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id       UUID NOT NULL REFERENCES customers(id),
    project_id        UUID NOT NULL REFERENCES projects(id),   -- denormalized for tenant filter perf
    type              contact_type NOT NULL,
    subject           VARCHAR(500),
    content           TEXT,
    outcome           VARCHAR(300),
    contacted_at      TIMESTAMPTZ NOT NULL,
    created_by_user_id UUID REFERENCES users(id),
    legacy_id         VARCHAR(50),                              -- old int ID from migration
    created_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted        BOOLEAN NOT NULL DEFAULT false
);

CREATE INDEX idx_ch_customer_id  ON contact_history(customer_id) WHERE is_deleted = false;
CREATE INDEX idx_ch_project_id   ON contact_history(project_id) WHERE is_deleted = false;
CREATE INDEX idx_ch_contacted_at ON contact_history(project_id, contacted_at DESC) WHERE is_deleted = false;
CREATE INDEX idx_ch_legacy_id    ON contact_history(legacy_id) WHERE legacy_id IS NOT NULL;
```

**EF Core Entity:** `ContactHistory`
**Migration source:** `dbo.CustomerInterviews` + `dbo.AppointedInterviews`

---

### 6. `tasks`
```sql
CREATE TYPE task_priority AS ENUM (
    'Low',
    'Medium',
    'High',
    'Critical'
);

CREATE TYPE task_status AS ENUM (
    'Todo',
    'InProgress',
    'Done',
    'Cancelled'
);

CREATE TABLE tasks (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id      UUID NOT NULL REFERENCES customers(id),
    project_id       UUID NOT NULL REFERENCES projects(id),
    title            VARCHAR(500) NOT NULL,
    description      TEXT,
    due_date         TIMESTAMPTZ,
    priority         task_priority NOT NULL DEFAULT 'Medium',
    status           task_status NOT NULL DEFAULT 'Todo',
    assigned_user_id UUID REFERENCES users(id),
    created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted       BOOLEAN NOT NULL DEFAULT false
);

CREATE INDEX idx_tasks_project_id       ON tasks(project_id) WHERE is_deleted = false;
CREATE INDEX idx_tasks_customer_id      ON tasks(customer_id) WHERE is_deleted = false;
CREATE INDEX idx_tasks_assigned_user_id ON tasks(assigned_user_id) WHERE is_deleted = false;
CREATE INDEX idx_tasks_due_date         ON tasks(project_id, due_date) WHERE is_deleted = false AND status != 'Done';
```

**EF Core Entity:** `CustomerTask`
**Note:** Named `CustomerTask` in C# to avoid conflict with `System.Threading.Tasks.Task`

---

### 7. `opportunities`
```sql
CREATE TYPE opportunity_stage AS ENUM (
    'Prospecting',
    'Qualification',
    'Proposal',
    'Negotiation',
    'ClosedWon',
    'ClosedLost'
);

CREATE TABLE opportunities (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_id         UUID NOT NULL REFERENCES customers(id),
    project_id          UUID NOT NULL REFERENCES projects(id),
    title               VARCHAR(500) NOT NULL,
    value               DECIMAL(18, 2),
    stage               opportunity_stage NOT NULL DEFAULT 'Prospecting',
    probability         SMALLINT CHECK (probability >= 0 AND probability <= 100),
    expected_close_date DATE,
    assigned_user_id    UUID REFERENCES users(id),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted          BOOLEAN NOT NULL DEFAULT false
);

CREATE INDEX idx_opp_project_id       ON opportunities(project_id) WHERE is_deleted = false;
CREATE INDEX idx_opp_customer_id      ON opportunities(customer_id) WHERE is_deleted = false;
CREATE INDEX idx_opp_assigned_user_id ON opportunities(assigned_user_id) WHERE is_deleted = false;
CREATE INDEX idx_opp_stage            ON opportunities(project_id, stage) WHERE is_deleted = false;
```

**EF Core Entity:** `Opportunity`

---

### 8. `sync_logs`
```sql
CREATE TYPE sync_source AS ENUM (
    'SaasA',
    'SaasB'
);

CREATE TYPE sync_direction AS ENUM (
    'Inbound',
    'Outbound'
);

CREATE TYPE sync_status AS ENUM (
    'Pending',
    'Success',
    'Failed',
    'Retrying'
);

CREATE TABLE sync_logs (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id    UUID NOT NULL REFERENCES projects(id),
    source        sync_source NOT NULL,
    direction     sync_direction NOT NULL,
    entity_type   VARCHAR(100) NOT NULL,
    entity_id     VARCHAR(100),
    status        sync_status NOT NULL DEFAULT 'Pending',
    error_message TEXT,
    retry_count   SMALLINT NOT NULL DEFAULT 0,
    synced_at     TIMESTAMPTZ,
    payload       JSONB,                                -- store raw payload for debugging/retry
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted    BOOLEAN NOT NULL DEFAULT false
);

CREATE INDEX idx_sync_project_id ON sync_logs(project_id, source, direction);
CREATE INDEX idx_sync_status     ON sync_logs(status) WHERE status IN ('Pending', 'Retrying');
CREATE INDEX idx_sync_created_at ON sync_logs(created_at DESC);
```

**EF Core Entity:** `SyncLog`

---

### 9. `refresh_tokens`
```sql
CREATE TABLE refresh_tokens (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token       VARCHAR(500) NOT NULL UNIQUE,   -- SHA-256 hashed token
    expires_at  TIMESTAMPTZ NOT NULL,
    is_revoked  BOOLEAN NOT NULL DEFAULT false,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted  BOOLEAN NOT NULL DEFAULT false
);

CREATE UNIQUE INDEX idx_rt_token   ON refresh_tokens(token) WHERE is_deleted = false;
CREATE INDEX idx_rt_user_id        ON refresh_tokens(user_id) WHERE is_deleted = false;
CREATE INDEX idx_rt_expires_at     ON refresh_tokens(expires_at) WHERE is_revoked = false;
```

**EF Core Entity:** `RefreshToken`
**Security:** Store hashed tokens only — never raw token in DB

---

## Enum Summary (C# Enums)

```csharp
public enum UserRole        { ProjectAdmin, SalesManager, SalesRep, Accounting }
public enum CustomerStatus  { Lead, Active, Inactive, Churned }
public enum CustomerSegment { SME, Enterprise, Startup, Government, Individual }
public enum ContactType     { Call, Email, Meeting, Note, WhatsApp, Visit }
public enum TaskPriority    { Low, Medium, High, Critical }
public enum TaskStatus      { Todo, InProgress, Done, Cancelled }
public enum OpportunityStage { Prospecting, Qualification, Proposal, Negotiation, ClosedWon, ClosedLost }
public enum SyncSource      { SaasA, SaasB }
public enum SyncDirection   { Inbound, Outbound }
public enum SyncStatus      { Pending, Success, Failed, Retrying }
```

---

## Entity Relationship Summary

```
projects (1) ──────────────────────────────────────────── (N) user_project_roles
users    (1) ──────────────────────────────────────────── (N) user_project_roles

projects (1) ──────────────────────────────────────────── (N) customers
users    (1) ─── assigned_user_id ─────────────────────── (N) customers

customers (1) ─────────────────────────────────────────── (N) contact_history
customers (1) ─────────────────────────────────────────── (N) tasks
customers (1) ─────────────────────────────────────────── (N) opportunities

projects  (1) ─────────────────────────────────────────── (N) sync_logs
users     (1) ─────────────────────────────────────────── (N) refresh_tokens
```

---

## Multi-Tenancy Strategy

**Global Query Filter (EF Core)** applied to all business entities:
```csharp
// Applied automatically in IonCrmDbContext.OnModelCreating()
modelBuilder.Entity<Customer>()
    .HasQueryFilter(e => e.ProjectId == _currentTenantId && !e.IsDeleted);
```

**SuperAdmin bypass:** When `ICurrentUser.IsSuperAdmin == true`, query filters are disabled via `IgnoreQueryFilters()`.

**JWT Payload:**
```json
{
  "userId": "uuid",
  "email": "user@example.com",
  "isSuperAdmin": false,
  "projectRoles": {
    "project-uuid-1": "SalesRep",
    "project-uuid-2": "ProjectAdmin"
  }
}
```

---

## Migration Notes (One-Time Data Migration)

### Source → Target Mapping

**EMS.dbo.Companies → customers**
| Old Field | New Field | Transform |
|-----------|-----------|-----------|
| ID | legacy_id | Cast int to string |
| Name | company_name | Direct copy |
| Phone | phone | Direct copy |
| Email | email | Lowercase + trim |
| Adress | address | Direct copy (fix typo) |
| TaxNumber | tax_number | Direct copy |
| TaxUnit | tax_unit | Direct copy |
| — | status | Default: 'Active' |
| — | project_id | Set by admin during migration run |

**dbo.PotentialCustomers → customers**
| Old Field | New Field | Transform |
|-----------|-----------|-----------|
| ID | legacy_id | Cast int to string, prefix "PC-" |
| CompanyName | company_name | Direct copy |
| ContactName | contact_name | Direct copy |
| Address | address | Direct copy |
| Email | email | Lowercase + trim |
| Phone | phone | Direct copy |
| — | status | Default: 'Lead' |
| — | project_id | Set by admin during migration run |

**dbo.CustomerInterviews → contact_history**
| Old Field | New Field | Transform |
|-----------|-----------|-----------|
| ID | legacy_id | Cast int to string |
| Date | contacted_at | Convert to UTC |
| Description | content | Direct copy |
| Type | type | Map: 0=Visit→Meeting, 1=Phone→Call, etc. |
| ProductDescription | subject | Direct copy |
| UserId | created_by_user_id | Lookup new user by legacy mapping |
| Status | outcome | Map status text |
