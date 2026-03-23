# Old Database Analysis — crm.bak
**Analyzed on:** 2026-03-23
**Source file:** `/root/my-product-team/input/database/crm.bak` (4.4 MB SQL Server backup)
**Method:** String extraction from binary .bak (SQL Server backup format — no MSSQL instance available)

---

## Database Databases Identified

The backup contains two logical databases:
- **IONCRM** — The CRM application database (main focus)
- **EMS** — External/master company data (shared reference data)

---

## Tables Identified

### 1. `EMS.dbo.Companies` — PRIMARY CUSTOMER TABLE ✅ MIGRATE
Core company/customer records pulled from the EMS system.

| Column | Notes |
|--------|-------|
| ID | Primary key (int) |
| Name | Company/customer name |
| Phone | Phone number |
| Email | Email address |
| Adress | Physical address (note: typo in original) |
| TaxNumber | Tax identification number |
| TaxUnit | Tax office |
| CustomerAuthorization | Authorization level/type |

**Migration target:** → `Customers` table
**Fields to map:** Name→companyName, Phone→phone, Email→email, Adress→address, TaxNumber+TaxUnit→(notes or separate fields)

---

### 2. `dbo.PotentialCustomers` — LEAD/PROSPECT TABLE ✅ MIGRATE
Potential customers not yet in the EMS companies system.

| Column | Notes |
|--------|-------|
| ID | Primary key |
| CompanyName | Company or individual name |
| ContactName | Contact person name |
| Address | Physical address |
| Email | Email address |
| Phone | Phone number |
| CustomerId | Link to EMS.Companies (nullable — not yet converted) |
| isTourList | Boolean flag — on visit tour list |
| CreatedBy | User who created the record |

**Migration target:** → `Customers` table (with status = "Lead" or "Potential")
**Fields to map:** CompanyName→companyName, ContactName→contactName, Address→address, Email→email, Phone→phone

---

### 3. `dbo.CustomerInterviews` — CONTACT HISTORY TABLE ✅ MIGRATE
All customer interview/communication records (calls, visits, meetings).

| Column | Notes |
|--------|-------|
| ID | Primary key |
| UserId | Sales rep who conducted interview |
| Date | Date of interview/contact |
| Description | Notes/description of the interaction |
| Status | Interview status (accepted/rejected/pending) |
| CustomerId | FK to Companies OR PotentialCustomers |
| isPotantialCustomer | bit — flag to determine which FK table |
| RejectDescription | FK to InterviewRejectStatus |
| Type | Contact type (call/visit/meeting) |
| ProductDescription | Product discussed |
| ContactPersonName | Person contacted at company |
| ContactPersonNumber | Phone of contact person |
| CreatedBy | User who created record |
| CreatedOn | Creation timestamp |

**Migration target:** → `ContactHistory` table
**Fields to map:** UserId→createdByUserId, Date→contactedAt, Description→content, Status→outcome, Type→type, ProductDescription→subject, ContactPersonName+ContactPersonNumber→(notes in content)

---

### 4. `dbo.AppointedInterviews` — SCHEDULED APPOINTMENTS TABLE ⚠️ PARTIAL MIGRATE
Scheduled meetings and appointments.

| Column | Notes |
|--------|-------|
| ID | Primary key |
| UserId | Assigned user |
| Date | Appointment date |
| Note | Notes about appointment |
| Type | Appointment type |
| Status | Appointment status |
| CustomerId | FK to Companies OR PotentialCustomers |
| isPotentialCustomer | bit flag |

**Migration target:** → `ContactHistory` (type=meeting) and/or `Tasks` table
**Note:** Historical appointments → ContactHistory; future/pending → Tasks

---

### 5. `dbo.Users` — USERS TABLE ❌ DO NOT MIGRATE (rebuild fresh)

| Column | Notes |
|--------|-------|
| ID | Primary key |
| NameSurname | Full name |
| Role0 | FK to Companies (user's company assignment) |

**Decision:** Do NOT migrate — new Users table has different structure (email, passwordHash, projectId, etc.)

---

### 6. `dbo.InterviewRejectStatus` — LOOKUP TABLE ❌ DO NOT MIGRATE

| Column | Notes |
|--------|-------|
| ID | Primary key |
| StatusName | Status description text |

**Decision:** Static lookup — handled by enums in new schema, not needed as table.

---

## Views Identified (Not migrated — views only)

| View | Purpose |
|------|---------|
| `CustomerInterviewView` | Joins CustomerInterviews + Companies + PotentialCustomers + Users |
| `PotentialCustomerView` | PotentialCustomers with last interview info |
| `AppointedInterviewView` | AppointedInterviews with company name lookups |
| `CompanyView` | EMS.Companies with contact person + last interview |
| `_InterviewCompanies` | Union of Companies + PotentialCustomers for dropdowns |

---

## Migration Plan Summary

### What to Migrate
| Old Table | → New Table | Priority |
|-----------|-------------|----------|
| `EMS.dbo.Companies` | `customers` | HIGH |
| `dbo.PotentialCustomers` | `customers` (status=Lead) | HIGH |
| `dbo.CustomerInterviews` | `contact_history` | HIGH |
| `dbo.AppointedInterviews` | `contact_history` (historical) | MEDIUM |

### What NOT to Migrate
- `dbo.Users` — Rebuild fresh with new auth model
- `dbo.InterviewRejectStatus` — Replace with enum
- All Views — Not data, just query logic

### Key Migration Rules (per CLAUDE.md)
1. **Do NOT copy old schema** — map to new clean schema
2. Both `EMS.dbo.Companies` AND `dbo.PotentialCustomers` → merge into single `customers` table
3. `isPotantialCustomer` flag → drives `status` field (Lead vs Active)
4. All history records need `projectId` assigned during migration (SuperAdmin sets target project)
5. Migration is **idempotent** — safe to run multiple times (deduplicate by old ID stored in a migration tracking field)
6. `CustomerInterviews.Type` values likely: visit=meeting, phone=call → map to `ContactType` enum

### Important Data Quality Notes
- Original `Adress` field has a typo — new schema uses `address`
- `isPotantialCustomer` has a typo in original — new schema uses proper boolean `status` enum
- Some customers exist in both EMS.Companies AND PotentialCustomers (PotentialCustomers.CustomerId is set when converted) — deduplicate on migration
- Old IDs are int — new IDs will be Guid — store old int ID in `legacyId` field for traceability
