"""
Architect Agent
===============
Analyzes old DB, designs new Clean Architecture structure,
DB schema, API contracts, and scaffolds the .NET solution.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from agents.base_agent import BaseAgent

WORKSPACE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUTPUT_DIR = os.path.join(WORKSPACE, "output")

SYSTEM_PROMPT = """
You are the Senior Solution Architect for ION CRM.

Your expertise:
- Clean Architecture for .NET Core 8
- Domain-Driven Design (DDD)
- Multi-tenant SaaS database design
- PostgreSQL / EF Core schema design
- REST API contract design
- Background service patterns

Your tasks for ION CRM:

1. ANALYZE OLD DATABASE
   - Read any .bak or .sql files in /input/database/
   - Identify: customer tables, contact history tables
   - Note what data needs migrating (customers + history ONLY)
   - DO NOT copy old schema — design fresh

2. DESIGN NEW DATABASE SCHEMA
   Multi-tenant tables needed:
   
   Projects (tenants)
   ├── id, name, description, isActive
   
   Users
   ├── id, email, passwordHash, firstName, lastName
   ├── isSuperAdmin (bypasses tenant filter)
   
   UserProjectRoles (many-to-many with role)
   ├── userId, projectId, role (enum)
   
   Customers
   ├── id, projectId (FK), code, companyName
   ├── contactName, email, phone, address
   ├── status, segment, assignedUserId
   ├── createdAt, updatedAt, isDeleted
   
   ContactHistory
   ├── id, customerId, projectId
   ├── type (call/email/meeting/note/whatsapp)
   ├── subject, content, outcome
   ├── contactedAt, createdByUserId
   
   Tasks
   ├── id, customerId, projectId
   ├── title, description, dueDate
   ├── priority, status, assignedUserId
   
   Opportunities
   ├── id, customerId, projectId
   ├── title, value, stage, probability
   ├── expectedCloseDate, assignedUserId
   
   SyncLogs
   ├── id, projectId, source (saas-a/saas-b)
   ├── direction (inbound/outbound)
   ├── entityType, entityId
   ├── status, errorMessage, syncedAt
   
   RefreshTokens
   ├── id, userId, token, expiresAt, isRevoked

3. SCAFFOLD .NET SOLUTION
   Create the full folder structure:
   
   output/
   ├── IonCrm.sln
   ├── src/
   │   ├── IonCrm.Domain/
   │   │   ├── IonCrm.Domain.csproj
   │   │   ├── Entities/
   │   │   ├── Interfaces/
   │   │   ├── Enums/
   │   │   └── Common/
   │   ├── IonCrm.Application/
   │   │   ├── IonCrm.Application.csproj
   │   │   ├── Features/ (CQRS per feature)
   │   │   ├── DTOs/
   │   │   ├── Interfaces/
   │   │   └── Common/
   │   ├── IonCrm.Infrastructure/
   │   │   ├── IonCrm.Infrastructure.csproj
   │   │   ├── Persistence/ (DbContext, Migrations)
   │   │   ├── Repositories/
   │   │   ├── Services/
   │   │   ├── BackgroundServices/
   │   │   └── ExternalApis/
   │   └── IonCrm.API/
   │       ├── IonCrm.API.csproj
   │       ├── Controllers/
   │       ├── Middleware/
   │       └── Program.cs
   ├── tests/
   │   └── IonCrm.Tests/
   └── frontend/
       └── (React app - handled by Frontend agent)

4. CREATE API CONTRACTS
   Document all endpoints before .NET Dev writes them:
   
   Auth:        POST /api/v1/auth/login
                POST /api/v1/auth/refresh
                POST /api/v1/auth/logout
   
   Customers:   GET/POST        /api/v1/customers
                GET/PUT/DELETE  /api/v1/customers/{id}
                GET             /api/v1/customers/{id}/history
                GET             /api/v1/customers/{id}/tasks
   
   Sync:        POST /api/v1/sync/saas-a
                POST /api/v1/sync/saas-b
                POST /api/v1/sync/callback/{saasId}
   
   Migration:   POST /api/v1/migration/run (SuperAdmin only)
   
   Admin:       GET/POST        /api/v1/admin/projects
                GET/POST/DELETE /api/v1/admin/users
                PUT             /api/v1/admin/users/{id}/roles

Always use dotnet CLI to create projects:
  dotnet new sln -n IonCrm -o {output_dir}
  dotnet new classlib -n IonCrm.Domain
  etc.

Add NuGet packages with dotnet add package.
Read CLAUDE.md first for all rules.
"""


class ArchitectAgent(BaseAgent):
    name = "Architect"
    emoji = "🏗️"
    color = "blue"
    ALLOWED_TOOLS = [
        "Read", "Write", "Edit", "MultiEdit",
        "Glob", "Bash", "WebSearch"
    ]

    def get_system_prompt(self) -> str:
        return SYSTEM_PROMPT.replace("{output_dir}", OUTPUT_DIR)

    async def analyze_and_scaffold(self) -> str:
        prompt = f"""
        Read CLAUDE.md at {WORKSPACE}/CLAUDE.md first.

        Then complete these tasks in order:

        TASK 1 — Analyze old database
        Check if there are any files in {WORKSPACE}/input/database/
        If .bak or .sql files exist, analyze them to understand:
        - What tables have customer data?
        - What tables have contact/communication history?
        - What fields are important to migrate?
        Write your findings to {WORKSPACE}/input/db_analysis.md

        TASK 2 — Design new schema
        Based on the ION CRM requirements in CLAUDE.md,
        design the complete new PostgreSQL schema.
        Write schema design to {OUTPUT_DIR}/docs/schema.md
        Include: table names, columns, types, indexes, foreign keys

        TASK 3 — Scaffold .NET solution
        Run these dotnet CLI commands to create the solution:
        
        cd {OUTPUT_DIR}
        dotnet new sln -n IonCrm
        dotnet new classlib -n IonCrm.Domain -o src/IonCrm.Domain
        dotnet new classlib -n IonCrm.Application -o src/IonCrm.Application
        dotnet new classlib -n IonCrm.Infrastructure -o src/IonCrm.Infrastructure
        dotnet new webapi -n IonCrm.API -o src/IonCrm.API --use-controllers
        dotnet new xunit -n IonCrm.Tests -o tests/IonCrm.Tests
        
        dotnet sln add src/IonCrm.Domain
        dotnet sln add src/IonCrm.Application
        dotnet sln add src/IonCrm.Infrastructure
        dotnet sln add src/IonCrm.API
        dotnet sln add tests/IonCrm.Tests

        Add project references:
        dotnet add src/IonCrm.Application reference src/IonCrm.Domain
        dotnet add src/IonCrm.Infrastructure reference src/IonCrm.Application
        dotnet add src/IonCrm.API reference src/IonCrm.Infrastructure

        TASK 4 — Add NuGet packages
        
        Infrastructure:
        dotnet add src/IonCrm.Infrastructure package Microsoft.EntityFrameworkCore
        dotnet add src/IonCrm.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
        dotnet add src/IonCrm.Infrastructure package Microsoft.EntityFrameworkCore.Design
        dotnet add src/IonCrm.Infrastructure package Hangfire.AspNetCore
        dotnet add src/IonCrm.Infrastructure package Hangfire.PostgreSql
        dotnet add src/IonCrm.Infrastructure package BCrypt.Net-Next
        
        Application:
        dotnet add src/IonCrm.Application package MediatR
        dotnet add src/IonCrm.Application package FluentValidation
        dotnet add src/IonCrm.Application package FluentValidation.DependencyInjectionExtensions
        dotnet add src/IonCrm.Application package AutoMapper
        
        API:
        dotnet add src/IonCrm.API package Microsoft.AspNetCore.Authentication.JwtBearer
        dotnet add src/IonCrm.API package Swashbuckle.AspNetCore
        dotnet add src/IonCrm.API package Serilog.AspNetCore
        dotnet add src/IonCrm.API package AspNetCoreRateLimit
        
        Tests:
        dotnet add tests/IonCrm.Tests package Moq
        dotnet add tests/IonCrm.Tests package FluentAssertions
        dotnet add tests/IonCrm.Tests package Microsoft.EntityFrameworkCore.InMemory

        TASK 5 — Create base Domain entities
        Write all entity classes in src/IonCrm.Domain/Entities/
        Based on the schema you designed in Task 2.
        Include: BaseEntity.cs with Id, CreatedAt, UpdatedAt, IsDeleted

        TASK 6 — Verify build
        cd {OUTPUT_DIR}
        dotnet build
        
        Fix any build errors before finishing.
        Report what was created.
        """

        return await self.run(prompt, OUTPUT_DIR)
