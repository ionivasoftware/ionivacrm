"""
.NET Core Backend Developer Agent
===================================
Implements the full backend: auth, CRUD, sync service,
migration service, EF Core, repositories, CQRS.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from agents.base_agent import BaseAgent

WORKSPACE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUTPUT_DIR = os.path.join(WORKSPACE, "output")

SYSTEM_PROMPT = """
You are a Senior .NET Core 8 Backend Developer building ION CRM.

Your expertise:
- ASP.NET Core 8 Web API
- Clean Architecture (Domain/Application/Infrastructure/API)
- CQRS with MediatR
- Entity Framework Core 8 + PostgreSQL (Npgsql)
- JWT authentication with refresh tokens
- Role-based + Project-based (multi-tenant) authorization
- Background services + Hangfire
- FluentValidation
- Repository pattern
- xUnit testing with Moq

ION CRM Specific Rules:
1. MULTI-TENANCY: Every query MUST include ProjectId filter
   - SuperAdmin: no filter (sees all)
   - Other roles: filter by their project memberships
   - Use ICurrentUserService to get current user's projectIds

2. TENANT FILTER: Use EF Core Global Query Filters
   modelBuilder.Entity<Customer>()
     .HasQueryFilter(c => !c.IsDeleted && 
       (_currentUser.IsSuperAdmin || 
        _currentUser.ProjectIds.Contains(c.ProjectId)));

3. JWT TOKEN STRUCTURE:
   Claims: userId, email, isSuperAdmin, 
           projectIds (comma-separated), 
           roles (json: {projectId: role})
   Access token: 15 minutes
   Refresh token: 7 days, stored in DB

4. API RESPONSE WRAPPER:
   public class ApiResponse<T>
   {
       public bool Success { get; set; }
       public T Data { get; set; }
       public string Message { get; set; }
       public List<string> Errors { get; set; }
       public int StatusCode { get; set; }
   }

5. SYNC SERVICE:
   - IHostedService + Hangfire for 15-min recurring job
   - SaaS A & B have different endpoint structures
   - Store each sync in SyncLogs table
   - Retry failed syncs 3 times (exponential backoff)
   - On CRM action (subscription etc.) → instant POST to SaaS callback

6. MIGRATION SERVICE:
   - Read old MSSQL data from connection string OR parsed SQL
   - Map old customers → new Customer entity
   - Map old contact history → new ContactHistory entity
   - Skip duplicates (check by email or legacy ID)
   - Run as one-time background job (POST /api/v1/migration/run)

7. SECURITY:
   - Passwords: BCrypt with cost 12
   - Connection strings: from IConfiguration (env vars only)
   - Log with Serilog — never log passwords/tokens
   - Rate limiting on /auth endpoints (10 req/min)
   - Input validation with FluentValidation on all commands

Always read existing files before editing them.
Run dotnet build after each major change.
Fix all compiler warnings, not just errors.
"""


class DotNetDevAgent(BaseAgent):
    name = ".NET Developer"
    emoji = "💻"
    color = "green"
    ALLOWED_TOOLS = [
        "Read", "Write", "Edit", "MultiEdit",
        "Glob", "Bash", "WebSearch"
    ]

    def get_system_prompt(self) -> str:
        return SYSTEM_PROMPT

    async def implement_sprint(self, sprint_name: str, stories: list[str]) -> str:
        stories_text = "\n".join(f"  - {s}" for s in stories)
        prompt = f"""
        Read CLAUDE.md at {WORKSPACE}/CLAUDE.md first.
        Read the existing solution structure in {OUTPUT_DIR}/src/
        Read {OUTPUT_DIR}/docs/schema.md for DB schema.

        Implement the following sprint: {sprint_name}

        Stories to implement:
        {stories_text}

        For each story:
        1. Create/update Domain entities if needed
        2. Create Application layer (Commands/Queries with MediatR)
        3. Create FluentValidation validators
        4. Create Infrastructure (Repository, EF Config)
        5. Create API Controller with proper auth attributes
        6. Write unit tests in IonCrm.Tests

        After implementing everything:
        cd {OUTPUT_DIR}
        dotnet build
        dotnet test

        Fix all errors before reporting done.
        Report: what was created/modified, test results.
        """
        return await self.run(prompt, OUTPUT_DIR)

    async def implement_auth(self) -> str:
        prompt = f"""
        Read CLAUDE.md at {WORKSPACE}/CLAUDE.md first.
        Read existing files in {OUTPUT_DIR}/src/

        Implement complete JWT authentication for ION CRM:

        1. Domain layer:
           - User.cs entity
           - UserProjectRole.cs entity  
           - Project.cs entity
           - RefreshToken.cs entity
           - IUserRepository interface
           - ITokenService interface
           - Enums: UserRole.cs

        2. Application layer:
           - LoginCommand + Handler + Validator
           - RefreshTokenCommand + Handler
           - LogoutCommand + Handler
           - RegisterUserCommand + Handler (SuperAdmin only)
           - GetCurrentUserQuery + Handler

        3. Infrastructure layer:
           - ApplicationDbContext with all entities
           - UserRepository implementation
           - TokenService (generates JWT + refresh tokens)
           - ICurrentUserService + implementation
           - DbContext global query filters for multi-tenancy
           - Initial EF Core migration

        4. API layer:
           - AuthController (login, refresh, logout)
           - UsersController (register, list, assign roles)
           - Program.cs with full DI registration:
             * JWT Bearer auth
             * MediatR
             * FluentValidation
             * EF Core + Npgsql
             * Hangfire
             * Serilog
             * Rate limiting
             * CORS
             * Swagger
           - GlobalExceptionMiddleware
           - TenantMiddleware (extracts projectIds from JWT)

        5. appsettings.json:
           {{
             "ConnectionStrings": {{
               "DefaultConnection": ""
             }},
             "JwtSettings": {{
               "Secret": "",
               "AccessTokenExpiryMinutes": 15,
               "RefreshTokenExpiryDays": 7,
               "Issuer": "IonCrm",
               "Audience": "IonCrmUsers"
             }},
             "SyncSettings": {{
               "IntervalMinutes": 15,
               "SaasABaseUrl": "",
               "SaasBBaseUrl": "",
               "SaasAApiKey": "",
               "SaasBApiKey": ""
             }}
           }}

        6. Run migration:
           cd {OUTPUT_DIR}
           dotnet ef migrations add InitialCreate --project src/IonCrm.Infrastructure --startup-project src/IonCrm.API
           
           (Don't run dotnet ef database update — user will do that with real connection string)

        Build and verify: dotnet build
        """
        return await self.run(prompt, OUTPUT_DIR)

    async def implement_sync_service(self) -> str:
        prompt = f"""
        Read CLAUDE.md at {WORKSPACE}/CLAUDE.md first.
        Read existing code in {OUTPUT_DIR}/src/

        Implement the Sync Service for ION CRM:

        1. SaaS → CRM (every 15 minutes via Hangfire):
           
           Infrastructure/BackgroundServices/SyncBackgroundService.cs
           - Implements IHostedService
           - Schedules Hangfire recurring job every 15 min
           
           Infrastructure/BackgroundServices/SaasSyncJob.cs
           - Calls SaaS A API → maps to CRM entities → upserts
           - Calls SaaS B API → maps to CRM entities → upserts
           - Logs each sync to SyncLogs table
           - Retry with exponential backoff (3 attempts)
           - Records: customers, contacts, subscriptions, orders

        2. CRM → SaaS (instant callbacks):
           
           Application/Features/Sync/Commands/NotifySaasCommand.cs
           - Called when: subscription extended, status changed, etc.
           - Posts to SaaS callback endpoint immediately
           - Logs outbound sync

        3. Sync API endpoints:
           
           API/Controllers/SyncController.cs
           POST /api/v1/sync/saas-a  (SaaS A pushes data here)
           POST /api/v1/sync/saas-b  (SaaS B pushes data here)
           GET  /api/v1/sync/logs    (SuperAdmin: see sync history)
           POST /api/v1/sync/trigger (SuperAdmin: manual trigger)

        4. SaaS API client:
           Infrastructure/ExternalApis/SaasAClient.cs
           Infrastructure/ExternalApis/SaasBClient.cs
           - HttpClient with API key auth
           - Typed responses
           - Retry policies with Polly

        Add Polly: dotnet add src/IonCrm.Infrastructure package Polly
        Add Polly.Extensions.Http

        Build and verify: dotnet build
        """
        return await self.run(prompt, OUTPUT_DIR)

    async def implement_migration_service(self) -> str:
        prompt = f"""
        Read CLAUDE.md at {WORKSPACE}/CLAUDE.md first.
        Read {WORKSPACE}/input/db_analysis.md for old DB structure.
        Read existing code in {OUTPUT_DIR}/src/

        Implement the one-time data migration service:

        Infrastructure/Services/DataMigrationService.cs
        - Reads old data from MSSQL connection string OR parses SQL files
        - Maps old customers → new Customer entities (per project)
        - Maps old contact history → new ContactHistory entities
        - Skips duplicates (check by email + legacy ID stored in LegacyId field)
        - Reports progress (X of N migrated)
        - Idempotent — safe to run multiple times

        API/Controllers/MigrationController.cs
        POST /api/v1/migration/run
        - SuperAdmin only
        - Accepts: {{projectId, mssqlConnectionString}} OR uses .bak analysis
        - Returns: migration job status
        GET  /api/v1/migration/status
        - Returns current migration progress

        Add a LegacyId field to Customer and ContactHistory entities
        for tracking which old records have been migrated.

        Add MSSQL package for reading old data:
        dotnet add src/IonCrm.Infrastructure package Microsoft.Data.SqlClient

        Build and verify: dotnet build
        """
        return await self.run(prompt, OUTPUT_DIR)
