# ION CRM — REST API Contracts
**Version:** 1.0.0
**Date:** 2026-03-24
**Base URL (dev):** `https://ion-crm-api-development.up.railway.app`
**Base URL (prod):** `https://ion-crm-api-production.up.railway.app`

---

## Authentication

All endpoints (except `login`, `refresh`, and SaaS webhook receivers) require:

```
Authorization: Bearer {access_token}
```

Access tokens expire in **15 minutes**. Use `POST /auth/refresh` with a valid refresh token to get a new pair.

---

## Standard Response Envelope

All responses use `ApiResponse<T>`:

```json
{
  "success": true,
  "data": { ... },
  "message": "Optional message",
  "statusCode": 200,
  "errors": []
}
```

Failure response:
```json
{
  "success": false,
  "data": null,
  "message": null,
  "statusCode": 400,
  "errors": ["Validation error message"]
}
```

---

## Auth Endpoints — `/api/v1/auth`

### `POST /api/v1/auth/login`
Login with email and password. Returns access + refresh tokens.

**Request body:**
```json
{
  "email": "user@example.com",
  "password": "SecurePassword123!"
}
```

**Response 200:**
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "refreshToken": "a7f3c2e1-...",
    "expiresAt": "2026-03-24T10:15:00Z",
    "user": {
      "id": "uuid",
      "email": "user@example.com",
      "firstName": "John",
      "lastName": "Doe",
      "isSuperAdmin": false,
      "projectRoles": {
        "project-uuid": "SalesRep"
      }
    }
  }
}
```

**Errors:**
- `401` — Invalid credentials

---

### `POST /api/v1/auth/refresh`
Exchange a valid refresh token for a new access + refresh token pair.

**Request body:**
```json
{
  "refreshToken": "a7f3c2e1-..."
}
```

**Response 200:** Same as login response.

**Errors:**
- `401` — Token expired, revoked, or invalid

---

### `POST /api/v1/auth/logout`
Revoke the refresh token. Requires Authorization header.

**Request body:**
```json
{
  "refreshToken": "a7f3c2e1-..."
}
```

**Response 200:**
```json
{ "success": true, "data": { "message": "Logged out successfully" } }
```

---

### `GET /api/v1/auth/me`
Get the currently authenticated user's profile.

**Response 200:**
```json
{
  "success": true,
  "data": {
    "id": "uuid",
    "email": "user@example.com",
    "firstName": "John",
    "lastName": "Doe",
    "isSuperAdmin": false,
    "projectRoles": { "project-uuid": "SalesRep" }
  }
}
```

---

### `POST /api/v1/auth/register` *(SuperAdmin only)*
Create a new user account.

**Request body:**
```json
{
  "email": "newuser@example.com",
  "password": "SecurePassword123!",
  "firstName": "Jane",
  "lastName": "Smith",
  "isSuperAdmin": false
}
```

**Response 200:** Returns `UserDto`.

---

## Customer Endpoints — `/api/v1/customers`

All endpoints require authentication. Tenant isolation is enforced by the active project in JWT claims.

---

### `GET /api/v1/customers`
Get paginated, filtered customer list.

**Query parameters:**

| Param | Type | Description |
|-------|------|-------------|
| `search` | string? | Full-text search on companyName, contactName, email, phone |
| `status` | enum? | `Lead`, `Active`, `Inactive`, `Churned` |
| `segment` | enum? | `SME`, `Enterprise`, `Startup`, `Government`, `Individual` |
| `assignedUserId` | guid? | Filter by assigned sales rep |
| `page` | int | Default: 1 |
| `pageSize` | int | Default: 20, Max: 100 |

**Response 200:**
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "id": "uuid",
        "projectId": "uuid",
        "code": "C-001",
        "companyName": "Acme Corp",
        "contactName": "John Smith",
        "email": "john@acme.com",
        "phone": "+90 532 000 0000",
        "address": "Istanbul",
        "taxNumber": "1234567890",
        "taxUnit": "Kadıköy",
        "status": "Active",
        "segment": "SME",
        "assignedUserId": "uuid",
        "createdAt": "2026-01-01T00:00:00Z",
        "updatedAt": "2026-01-01T00:00:00Z"
      }
    ],
    "totalCount": 639,
    "page": 1,
    "pageSize": 20,
    "totalPages": 32
  }
}
```

---

### `POST /api/v1/customers`
Create a new customer.

**Request body:**
```json
{
  "companyName": "Acme Corp",
  "contactName": "John Smith",
  "email": "john@acme.com",
  "phone": "+90 532 000 0000",
  "address": "Istanbul",
  "taxNumber": "1234567890",
  "taxUnit": "Kadıköy",
  "status": "Lead",
  "segment": "SME",
  "assignedUserId": "uuid"
}
```

**Response 201:** Returns created `CustomerDto`.

**Validation errors:**
- `companyName` is required, max 300 chars
- `email` must be valid format if provided

---

### `GET /api/v1/customers/{id}`
Get customer by ID.

**Response 200:** Returns `CustomerDto` (same as list item above).

**Errors:**
- `404` — Customer not found or not in tenant

---

### `PUT /api/v1/customers/{id}`
Update a customer.

**Request body:** Same fields as `POST` (all optional).

**Response 200:** Returns updated `CustomerDto`.

**Errors:**
- `404` — Not found
- `400` — Validation failure

---

### `DELETE /api/v1/customers/{id}`
Soft-delete a customer.

**Response 200:**
```json
{ "success": true, "data": null, "message": "Customer deleted." }
```

---

### `GET /api/v1/customers/{id}/contact-histories`
Get all contact history records for a customer.

**Response 200:**
```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "customerId": "uuid",
      "type": "Call",
      "subject": "Follow-up call",
      "content": "Discussed renewal terms",
      "outcome": "Interested — send proposal",
      "contactedAt": "2026-03-20T14:00:00Z",
      "createdByUserId": "uuid",
      "createdAt": "2026-03-20T14:05:00Z"
    }
  ]
}
```

---

### `POST /api/v1/customers/{id}/contact-histories`
Log a new interaction.

**Request body:**
```json
{
  "type": "Call",
  "subject": "Follow-up call",
  "content": "Discussed renewal terms",
  "outcome": "Interested — send proposal",
  "contactedAt": "2026-03-20T14:00:00Z"
}
```

`type` enum: `Call`, `Email`, `Meeting`, `Note`, `WhatsApp`, `Visit`

**Response 201:** Returns created `ContactHistoryDto`.

---

### `PUT /api/v1/customers/{customerId}/contact-histories/{id}`
Update a contact history record.

**Request body:** Same as POST (all optional).

**Response 200:** Returns updated `ContactHistoryDto`.

---

### `DELETE /api/v1/customers/{customerId}/contact-histories/{id}`
Soft-delete a contact history record.

**Response 200:** Success envelope.

---

### `GET /api/v1/customers/{id}/tasks`
Get all tasks for a customer.

**Response 200:**
```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "customerId": "uuid",
      "title": "Send proposal",
      "description": "Prepare and send Q2 proposal",
      "dueDate": "2026-04-01T09:00:00Z",
      "priority": "High",
      "status": "Todo",
      "assignedUserId": "uuid",
      "createdAt": "2026-03-24T00:00:00Z"
    }
  ]
}
```

---

### `POST /api/v1/customers/{id}/tasks`
Create a task for a customer.

**Request body:**
```json
{
  "title": "Send proposal",
  "description": "Prepare and send Q2 proposal",
  "dueDate": "2026-04-01T09:00:00Z",
  "priority": "High",
  "assignedUserId": "uuid"
}
```

`priority` enum: `Low`, `Medium`, `High`, `Critical`

**Response 201:** Returns created `CustomerTaskDto`.

---

### `PUT /api/v1/customers/{customerId}/tasks/{id}`
Update a task.

**Request body:** Any subset of task fields including `status`.

`status` enum: `Todo`, `InProgress`, `Done`, `Cancelled`

**Response 200:** Returns updated `CustomerTaskDto`.

---

### `DELETE /api/v1/customers/{customerId}/tasks/{id}`
Soft-delete a task.

**Response 200:** Success envelope.

---

## Sync Endpoints — `/api/v1/sync`

---

### `POST /api/v1/sync/saas-a` *(API-key secured, NOT JWT)*
Receive a webhook event from SaaS A.

**Required headers:**
```
X-Api-Key: {configured SaasA:WebhookApiKey}
X-Project-Id: {target-project-uuid}   (optional if configured in appsettings)
```

**Request body (flexible JSON — SaaS A format):**
```json
{
  "eventType": "customer.updated",
  "entityType": "Customer",
  "entityId": "12345",
  "data": { ... }
}
```

**Response 200:** `{ "success": true, "data": {} }`

**Errors:**
- `401` — Invalid or missing API key
- `400` — Cannot resolve ProjectId

---

### `POST /api/v1/sync/saas-b` *(API-key secured, NOT JWT)*
Receive a webhook event from SaaS B.

**Required headers:**
```
X-Api-Key: {configured SaasB:WebhookApiKey}
X-Project-Id: {target-project-uuid}
```

**Request body (SaaS B format):**
```json
{
  "event": "invoice.created",
  "type": "Invoice",
  "id": "INV-9999",
  "payload": { ... }
}
```

**Response 200:** Success envelope.

---

### `GET /api/v1/sync/logs` *(Authenticated)*
View sync history with filtering.

**Query parameters:**

| Param | Type | Description |
|-------|------|-------------|
| `page` | int | Default: 1 |
| `pageSize` | int | Default: 20 |
| `projectId` | guid? | SuperAdmin only — filter by project |
| `source` | enum? | `SaasA`, `SaasB` |
| `direction` | enum? | `Inbound`, `Outbound` |
| `status` | enum? | `Pending`, `Success`, `Failed`, `Retrying` |

**Response 200:** Returns paged `SyncLogDto[]`.

---

### `POST /api/v1/sync/trigger` *(SuperAdmin only)*
Manually trigger a full sync cycle (enqueues Hangfire job).

**Response 200:**
```json
{
  "success": true,
  "data": { "jobId": "1" },
  "message": "Sync job enqueued. It will run in the background shortly."
}
```

---

## Migration Endpoints — `/api/v1/migration`

All endpoints require `SuperAdmin` policy.

---

### `POST /api/v1/migration/run`
Start the one-time data migration from legacy MSSQL CRM database.
Idempotent — records already migrated (matched by `legacy_id`) are skipped.

**Request body:**
```json
{
  "projectId": "target-project-uuid",
  "mssqlConnectionString": "Server=192.168.1.10;Database=IONCRM;User Id=sa;Password=***;TrustServerCertificate=True"
}
```

**Response 202 Accepted:**
```json
{
  "success": true,
  "statusCode": 202,
  "data": {
    "status": "Running",
    "startedAt": "2026-03-24T10:00:00Z",
    "totalCustomers": 0,
    "migratedCustomers": 0,
    "totalHistories": 0,
    "migratedHistories": 0,
    "errors": []
  },
  "message": "Migration job started. Poll GET /api/v1/migration/status for progress."
}
```

**Errors:**
- `400` — Validation failure (empty projectId or connection string)
- `409` — Migration already running

---

### `GET /api/v1/migration/status`
Get current migration progress snapshot (reads in-memory state — safe to poll).

**Response 200:**
```json
{
  "success": true,
  "data": {
    "status": "Completed",
    "startedAt": "2026-03-24T10:00:00Z",
    "completedAt": "2026-03-24T10:05:32Z",
    "totalCustomers": 776,
    "migratedCustomers": 776,
    "totalHistories": 892,
    "migratedHistories": 892,
    "errors": []
  }
}
```

`status` values: `Idle`, `Running`, `Completed`, `Failed`

---

## Admin Endpoints — `/api/v1/admin`

All endpoints require `SuperAdmin` policy.

---

### `GET /api/v1/admin/projects`
List all projects (tenants).

**Response 200:**
```json
{
  "success": true,
  "data": [
    {
      "id": "uuid",
      "name": "Ioniva Muhasebe",
      "description": "Muhasebe departmanı CRM",
      "isActive": true,
      "createdAt": "2026-01-01T00:00:00Z"
    }
  ]
}
```

---

### `POST /api/v1/admin/projects`
Create a new project (tenant).

**Request body:**
```json
{
  "name": "Ioniva Satis",
  "description": "Satış ekibi CRM"
}
```

**Response 201:** Returns created project.

---

### `GET /api/v1/admin/users`
List all users across all projects.

**Query parameters:**

| Param | Type | Description |
|-------|------|-------------|
| `page` | int | Default: 1 |
| `pageSize` | int | Default: 20 |
| `projectId` | guid? | Filter by project |

**Response 200:** Returns paged `UserDto[]`.

---

### `POST /api/v1/admin/users`
Create a new user (equivalent to `/auth/register` but explicitly admin-facing).

**Request body:**
```json
{
  "email": "newuser@example.com",
  "password": "SecurePassword123!",
  "firstName": "Jane",
  "lastName": "Smith",
  "isSuperAdmin": false
}
```

**Response 201:** Returns `UserDto`.

---

### `DELETE /api/v1/admin/users/{id}`
Soft-delete a user.

**Response 200:** Success envelope.

---

### `PUT /api/v1/admin/users/{id}/roles`
Assign or update a user's role in a specific project.

**Request body:**
```json
{
  "projectId": "uuid",
  "role": "SalesManager"
}
```

`role` enum: `ProjectAdmin`, `SalesManager`, `SalesRep`, `Accounting`

**Response 200:** Returns updated `UserDto` with new role.

**Notes:**
- If user already has a role in the project, it is updated (not duplicated)
- A user can have at most one role per project
- SuperAdmin users have implicit access to all projects regardless of roles

---

## HTTP Status Codes Used

| Code | Meaning |
|------|---------|
| 200 | Success |
| 201 | Created |
| 202 | Accepted (async job started) |
| 400 | Bad request / Validation failure |
| 401 | Unauthenticated |
| 403 | Forbidden (wrong role or not in project) |
| 404 | Not found (or not in tenant scope) |
| 409 | Conflict (e.g., migration already running) |
| 429 | Rate limited |
| 500 | Internal server error |

---

## Rate Limiting

Configured via `AspNetCoreRateLimit`:
- General: **100 requests / minute** per IP
- Auth endpoints: **10 requests / minute** per IP (brute-force protection)

---

## JWT Token Structure

```json
{
  "sub": "user-uuid",
  "email": "user@example.com",
  "isSuperAdmin": "false",
  "projectRoles": "{\"project-uuid\":\"SalesRep\"}",
  "iat": 1711234567,
  "exp": 1711235467,
  "iss": "IonCrm",
  "aud": "IonCrm"
}
```

---

## Enums Reference

```
CustomerStatus:   Lead | Active | Inactive | Churned
CustomerSegment:  SME | Enterprise | Startup | Government | Individual
ContactType:      Call | Email | Meeting | Note | WhatsApp | Visit
TaskPriority:     Low | Medium | High | Critical
TaskStatus:       Todo | InProgress | Done | Cancelled
OpportunityStage: Prospecting | Qualification | Proposal | Negotiation | ClosedWon | ClosedLost
UserRole:         ProjectAdmin | SalesManager | SalesRep | Accounting
SyncSource:       SaasA | SaasB
SyncDirection:    Inbound | Outbound
SyncStatus:       Pending | Success | Failed | Retrying
```
