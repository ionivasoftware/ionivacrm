# ION CRM — Agent Team Rules

## Project Overview
ION CRM is a multi-tenant SaaS CRM built with:
- **Backend**: ASP.NET Core 8, Clean Architecture, EF Core, PostgreSQL (Supabase)
- **Frontend**: React 18, shadcn/ui, Tailwind CSS, Dark Mode, Mobile Responsive
- **Auth**: JWT Bearer tokens, Role-based + Project-based access control
- **Sync**: Background service syncing from SaaS A & B every 15 minutes
- **Deploy**: Railway via GitHub Actions

## Architecture — Clean Architecture (STRICT)
```
IonCrm.Domain          → Entities, Interfaces, Enums, Value Objects
IonCrm.Application     → CQRS (MediatR), DTOs, Validators, Interfaces
IonCrm.Infrastructure  → EF Core, Repositories, Background Services, External APIs
IonCrm.API             → Controllers, Middleware, DI Registration, Swagger
IonCrm.Tests           → xUnit, Moq, Integration Tests
frontend/              → React 18, shadcn/ui, Zustand, React Query
```

## Multi-Tenancy & Role Model (CRITICAL)
```
SuperAdmin
  └── Sees ALL projects, ALL data, system settings

Project (e.g. "Ioniva Muhasebe", "Ioniva Satis")
  └── ProjectAdmin   → manages project users & settings
  └── SalesManager   → sees full team pipeline
  └── SalesRep       → sees own customers only
  └── Accounting     → sees invoices & payments only

Rules:
- One user CAN belong to multiple projects with different roles
- Project data is STRICTLY isolated (tenant filter on every query)
- SuperAdmin bypasses tenant filter
- JWT token contains: userId, projectIds[], roles{}
```

## Database Rules
- NEVER use raw SQL — always EF Core
- Every table has: Id (Guid), CreatedAt, UpdatedAt, IsDeleted (soft delete)
- Every query includes tenant filter (ProjectId) unless SuperAdmin
- Use migrations for ALL schema changes
- Connection string comes from environment variable only

## API Rules
- RESTful endpoints under /api/v1/
- Always return ApiResponse<T> wrapper
- Global exception middleware handles all errors
- FluentValidation on ALL commands
- Swagger enabled in Development only
- Rate limiting on auth endpoints

## Sync Service Rules
- Runs every 15 minutes via .NET BackgroundService + Hangfire
- SaaS A & B push to /api/v1/sync/saas-a and /api/v1/sync/saas-b
- CRM pushes INSTANTLY to SaaS when: subscription extended, status changed
- Sync logs stored in DB (SyncLog table)
- Failed syncs retry 3 times with exponential backoff

## Migration Rules (One-Time)
- Read .bak file from /input/database/
- Extract: customers, contact history ONLY
- Map to new schema (do NOT copy old structure)
- Run as standalone MigrationService
- Idempotent — can run multiple times safely

## Security Rules (NON-NEGOTIABLE)
- NEVER hardcode secrets — always environment variables
- NEVER log passwords, tokens, or connection strings
- bcrypt for password hashing (cost factor 12)
- JWT expiry: 15 minutes access token, 7 days refresh token
- HTTPS only in production
- CORS locked to specific origins
- Input sanitization on ALL endpoints
- SQL injection impossible (EF Core parameterized)

## Code Style
- async/await throughout — no sync over async
- XML doc comments on all public methods
- SOLID principles enforced
- No God classes — max 200 lines per class
- Repository pattern for all DB access
- Result<T> pattern for error handling (no exceptions for business logic)

## Frontend Rules
- Dark mode default, light mode toggle
- Mobile-first responsive (WebView compatible)
- shadcn/ui components only
- Zustand for global state
- React Query for all API calls (no raw fetch)
- Turkish language default

## Git Rules
- Branch per feature: feature/module-name
- Commit messages: feat/fix/chore/test: description
- PR required for main — no direct pushes
- Each agent commits to own branch

## Approval Gates (WAIT FOR HUMAN)
The orchestrator MUST pause and wait for human approval at:
1. After Sprint planning — before any code written
2. After DB schema design — before migrations run
3. After each Sprint completion — before next Sprint starts
4. Before any deployment to Railway

## Cost Control
- max_turns: 50 per agent session
- If task unclear → ask orchestrator, not user
- Prefer editing existing files over creating new ones
- Log token usage after each agent run
