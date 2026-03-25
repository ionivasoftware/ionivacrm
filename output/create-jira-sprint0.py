#!/usr/bin/env python3
"""
ION CRM — Sprint 0 Jira Ticket Creator  (v2)
Creates: Epic + 6 Stories for Sprint 0 — Analysis & Architecture
under the existing PROD project.

NOTE: The PROD project must already exist in Jira.
If it doesn't exist, create it manually at:
  https://ofcakmakci.atlassian.net  →  Projects  →  Create project
  Type: Scrum software development
  Key:  PROD
  Name: ION CRM
"""

import requests
import os
import json
import sys
import time
from typing import Optional

# ── CONFIG ────────────────────────────────────────────────────────────────────
EMAIL       = "ofcakmakci@gmail.com"
API_TOKEN   = os.environ.get("JIRA_API_TOKEN", "")
BASE_URL    = "https://ofcakmakci.atlassian.net"
PROJECT_KEY = "PROD"
SPRINT_NAME = "Sprint 0 — Analysis & Architecture"

AUTH    = (EMAIL, API_TOKEN)
HEADERS = {"Accept": "application/json", "Content-Type": "application/json"}

# ── HELPERS ───────────────────────────────────────────────────────────────────
def jira_get(path: str, params: dict = None):
    return requests.get(f"{BASE_URL}{path}", auth=AUTH, headers=HEADERS, params=params)

def jira_post(path: str, payload: dict):
    return requests.post(f"{BASE_URL}{path}", auth=AUTH, headers=HEADERS, json=payload)

def check_project() -> Optional[dict]:
    """Verify the PROD project exists and is accessible."""
    r = jira_get(f"/rest/api/3/project/{PROJECT_KEY}")
    if r.status_code == 200:
        data = r.json()
        print(f"✅ Project found: {data['key']} — {data['name']} (id: {data['id']})")
        return data
    elif r.status_code == 404:
        print(f"❌ Project '{PROJECT_KEY}' not found.")
        print()
        print("  → Please create it manually:")
        print(f"     1. Open: {BASE_URL}")
        print(f"     2. Projects → Create project")
        print(f"     3. Template: Scrum software development")
        print(f"     4. Project key: PROD")
        print(f"     5. Project name: ION CRM")
        print(f"     6. Re-run this script")
        return None
    else:
        print(f"❌ Unexpected status {r.status_code}: {r.text[:200]}")
        return None

def get_issue_types(project_id: str) -> dict:
    """Return dict of issueType name → id for the project."""
    r = jira_get("/rest/api/3/issuetype/project", params={"projectId": project_id})
    if r.status_code == 200:
        types = {t["name"]: t["id"] for t in r.json()}
        print(f"📋 Issue types available: {list(types.keys())}")
        return types
    # Fallback: get from project meta
    r2 = jira_get(f"/rest/api/3/project/{PROJECT_KEY}")
    if r2.status_code == 200:
        itypes = r2.json().get("issueTypes", [])
        return {t["name"]: t["id"] for t in itypes}
    return {}

def get_board_id() -> Optional[int]:
    r = jira_get("/rest/agile/1.0/board", params={"projectKeyOrId": PROJECT_KEY, "type": "scrum"})
    if r.status_code == 200:
        boards = r.json().get("values", [])
        if boards:
            bid = boards[0]["id"]
            print(f"✅ Scrum board: {boards[0]['name']} (id: {bid})")
            return bid
    print("⚠️  No Scrum board found — sprint field will be skipped")
    return None

def get_or_create_sprint(board_id: int) -> Optional[dict]:
    r = jira_get(f"/rest/agile/1.0/board/{board_id}/sprint", params={"state": "active,future"})
    if r.status_code == 200:
        sprints = r.json().get("values", [])
        for s in sprints:
            if "Sprint 0" in s.get("name", ""):
                print(f"✅ Sprint found: {s['name']} (id: {s['id']})")
                return s
    # Create sprint
    r2 = jira_post("/rest/agile/1.0/sprint", {
        "name": SPRINT_NAME,
        "originBoardId": board_id,
        "goal": "Analyze old DB, design new schema, define API contracts, scaffold project"
    })
    if r2.status_code in (200, 201):
        data = r2.json()
        print(f"✅ Sprint created: {data['name']} (id: {data['id']})")
        return data
    print(f"⚠️  Sprint creation returned {r2.status_code} — {r2.text[:150]}")
    return None

def adf(text: str) -> dict:
    """Convert plain text to Atlassian Document Format."""
    paragraphs = [p.strip() for p in text.strip().split("\n") if p.strip()]
    return {
        "type": "doc", "version": 1,
        "content": [
            {"type": "paragraph", "content": [{"type": "text", "text": p}]}
            for p in paragraphs
        ]
    }

def create_issue(payload: dict, label: str) -> Optional[str]:
    """POST /rest/api/3/issue with graceful field-stripping retry."""
    r = jira_post("/rest/api/3/issue", payload)
    if r.status_code in (200, 201):
        key = r.json()["key"]
        print(f"  ✅ {key}  {label}")
        return key

    # Retry: strip custom fields that may not be configured
    print(f"  ⚠️  First attempt {r.status_code} — retrying without optional custom fields...")
    stripped = {k: v for k, v in payload["fields"].items()
                if not k.startswith("customfield_") and k != "parent"}
    r2 = jira_post("/rest/api/3/issue", {"fields": stripped})
    if r2.status_code in (200, 201):
        key = r2.json()["key"]
        print(f"  ✅ {key} (simplified)  {label}")
        return key

    print(f"  ❌ Failed — {r2.status_code}: {r2.text[:200]}")
    return None

# ── DATA ──────────────────────────────────────────────────────────────────────
STORIES = [
    {
        "id": "ION-S0-01",
        "title": "[ARCHITECT] Analyze MSSQL .bak file and extract legacy schema",
        "points": 5,
        "labels": ["backend", "migration", "analysis"],
        "description": (
            "Restore / mount the 4.4 MB MSSQL backup at /root/my-product-team/input/database/crm.bak.\n"
            "Extract all DDL (CREATE TABLE, indexes, FKs, constraints).\n"
            "Document every table name, column name, data type, nullable flag, default value.\n"
            "Map legacy customer + contact-history columns to new schema field names.\n"
            "Flag all PII fields: name, phone, email, address.\n"
            "Map MSSQL-specific types (NVARCHAR, DATETIME2, UNIQUEIDENTIFIER...) to PostgreSQL equivalents.\n"
            "Estimate row counts per table from backup metadata.\n\n"
            "Outputs:\n"
            "  /root/my-product-team/output/docs/legacy-schema.md\n"
            "  /root/my-product-team/output/docs/migration-mapping.md\n\n"
            "Acceptance Criteria:\n"
            "  All legacy tables documented with estimated row counts.\n"
            "  Column mapping: old_name -> new_name defined for Customers and ContactHistory.\n"
            "  PII fields identified and flagged.\n"
            "  MSSQL types mapped to PostgreSQL equivalents."
        ),
    },
    {
        "id": "ION-S0-02",
        "title": "[ARCHITECT] Design PostgreSQL target schema",
        "points": 8,
        "labels": ["backend", "database", "architecture"],
        "description": (
            "Design the full PostgreSQL schema for ION CRM covering all entities.\n\n"
            "Required entities:\n"
            "  Projects, Users, UserProjectRoles, Customers, ContactHistory,\n"
            "  Notes, CustomerTasks, Opportunities, SyncLogs, RefreshTokens, AuditLogs.\n\n"
            "Design rules:\n"
            "  Every table: Id (UUID PK), CreatedAt (timestamptz), UpdatedAt (timestamptz), IsDeleted (bool).\n"
            "  Every tenant table: ProjectId (UUID FK -> Projects.Id).\n"
            "  Indexes on: ProjectId, Email, ExternalId, IsDeleted.\n"
            "  All enums listed with all possible values.\n"
            "  FK relationships fully defined with ON DELETE strategy.\n\n"
            "Outputs:\n"
            "  /root/my-product-team/output/docs/db-schema.md\n"
            "  /root/my-product-team/output/docs/entity-stubs/ (one .cs stub per entity)\n\n"
            "Acceptance Criteria:\n"
            "  All entities have standard base fields (Id, CreatedAt, UpdatedAt, IsDeleted).\n"
            "  All FK relationships defined with cascade strategy.\n"
            "  Composite indexes identified.\n"
            "  All enum types listed with values.\n"
            "  Schema validated against migration mapping from ION-S0-01."
        ),
    },
    {
        "id": "ION-S0-03",
        "title": "[ARCHITECT] Define API contracts and OpenAPI spec",
        "points": 5,
        "labels": ["backend", "api", "architecture"],
        "description": (
            "Define all REST endpoints for the ION CRM API.\n\n"
            "Endpoint groups:\n"
            "  Auth: POST /auth/login, POST /auth/logout, POST /auth/refresh, GET /auth/me\n"
            "  SuperAdmin: Projects CRUD, User management, Role assignment\n"
            "  Customers: CRUD, search, filter, label/status management, merge\n"
            "  ContactHistory: CRUD, bulk query by customer, by date range\n"
            "  Notes: CRUD per customer\n"
            "  CustomerTasks: CRUD, completion tracking\n"
            "  Opportunities & Pipeline: CRUD, stage transitions\n"
            "  Sync (inbound): POST /sync/saas-a, POST /sync/saas-b\n"
            "  Sync (outbound callbacks): defined payload format\n"
            "  Dashboard: GET /dashboard/widgets, GET /dashboard/charts\n\n"
            "For each endpoint specify:\n"
            "  HTTP method, path, auth required (JWT), allowed roles, request body schema,\n"
            "  response schema, possible error codes (401, 403, 404, 422, 500).\n\n"
            "Define SaaS A and SaaS B inbound payload formats.\n"
            "Define outbound callback payload format (customer created/updated events).\n"
            "Document ApiResponse<T> wrapper: { success, data, error, timestamp }.\n\n"
            "Output:\n"
            "  /root/my-product-team/output/docs/api-contracts.md\n\n"
            "Acceptance Criteria:\n"
            "  All endpoints listed with method, path, auth, roles.\n"
            "  Request/response schemas defined for each endpoint.\n"
            "  Standard error codes and error response format documented.\n"
            "  Sync payloads (SaaS A, SaaS B, outbound) fully defined.\n"
            "  ApiResponse<T> wrapper format documented."
        ),
    },
    {
        "id": "ION-S0-04",
        "title": "[ARCHITECT] Define solution folder structure and Clean Architecture scaffold",
        "points": 3,
        "labels": ["backend", "devops", "architecture"],
        "description": (
            "Document the exact .NET solution folder structure following Clean Architecture.\n\n"
            "Backend layers:\n"
            "  IonCrm.Domain      — Entities, Enums, Exceptions, ValueObjects, Domain Events\n"
            "  IonCrm.Application — Commands, Queries (MediatR CQRS), DTOs, Validators (FluentValidation), Interfaces\n"
            "  IonCrm.Infrastructure — EF Core DbContext, Repositories, BackgroundServices, External HTTP clients\n"
            "  IonCrm.Api         — Controllers, Middleware (exception, auth, tenant), DI Registration, Program.cs\n"
            "  IonCrm.Tests       — Unit tests (Domain + Application), Integration tests (API)\n\n"
            "Frontend structure:\n"
            "  React 18 + TypeScript + shadcn/ui + Tailwind CSS + Zustand + React Query\n"
            "  src/pages/, src/components/ui/, src/stores/, src/api/, src/types/, src/hooks/\n\n"
            "CI/CD:\n"
            "  .github/workflows/ci.yml    — dotnet build, test, npm build, lint\n"
            "  .github/workflows/deploy.yml — push to Railway (dev on main, prod on tags)\n\n"
            "Docker Compose services: postgres, mssql (migration only), api, frontend.\n\n"
            "Outputs:\n"
            "  /root/my-product-team/output/docs/project-structure.md\n"
            "  /root/my-product-team/output/.env.example\n\n"
            "Acceptance Criteria:\n"
            "  Every project layer mapped to Clean Architecture responsibility.\n"
            "  DI registration strategy described per layer.\n"
            "  Docker Compose services listed with ports and dependencies.\n"
            "  .env.example contains all required variable names with no real values."
        ),
    },
    {
        "id": "ION-S0-05",
        "title": "[ARCHITECT] Define Sync Service architecture (SaaS A & B ↔ ION CRM)",
        "points": 5,
        "labels": ["backend", "sync", "architecture"],
        "description": (
            "Design the complete bi-directional sync architecture between ION CRM and the two SaaS products.\n\n"
            "Inbound sync (SaaS → ION CRM):\n"
            "  SaaS A pushes customer/subscription data every 15 minutes via webhook.\n"
            "  SaaS B pushes customer/subscription data every 15 minutes via webhook.\n"
            "  ION CRM receives via POST /sync/saas-a and POST /sync/saas-b.\n"
            "  Design: upsert logic by ExternalId, conflict resolution strategy, idempotency.\n\n"
            "Outbound sync (ION CRM → SaaS):\n"
            "  When a customer is created/updated/deleted in ION CRM, push instant callback to SaaS.\n"
            "  Design retry mechanism: 3 retries with exponential back-off.\n"
            "  Design SyncLog entity to record every sync event (status, payload, error).\n\n"
            "Background Service:\n"
            "  IHostedService for scheduled polling if webhook delivery fails.\n"
            "  Configurable interval via appsettings.\n\n"
            "Output:\n"
            "  /root/my-product-team/output/docs/sync-architecture.md\n\n"
            "Acceptance Criteria:\n"
            "  Inbound payload schemas for SaaS A and B defined.\n"
            "  Upsert + conflict resolution logic described.\n"
            "  Outbound callback payload and retry strategy documented.\n"
            "  SyncLog entity fields defined.\n"
            "  Background service lifecycle and error handling described."
        ),
    },
    {
        "id": "ION-S0-06",
        "title": "[ARCHITECT] Document ADRs, tech decisions, and dev environment setup",
        "points": 3,
        "labels": ["architecture", "documentation", "devops"],
        "description": (
            "Record all architectural decisions (ADRs) made during Sprint 0.\n\n"
            "ADRs to document:\n"
            "  ADR-001: Why Clean Architecture (vs MVC)\n"
            "  ADR-002: Why MediatR + CQRS\n"
            "  ADR-003: Why Neon PostgreSQL (vs self-hosted)\n"
            "  ADR-004: Why Railway for deployment\n"
            "  ADR-005: Multi-tenancy strategy (ProjectId on every row vs schema-per-tenant)\n"
            "  ADR-006: Sync strategy (webhook push vs polling fallback)\n"
            "  ADR-007: JWT + Refresh Token vs session-based auth\n\n"
            "Dev environment setup guide:\n"
            "  Prerequisites: .NET 8 SDK, Node 20, Docker, PostgreSQL 16.\n"
            "  Step-by-step: clone, set env vars, dotnet run, npm run dev.\n"
            "  How to connect to Neon DB (dev and prod).\n"
            "  How to run EF Core migrations.\n\n"
            "Outputs:\n"
            "  /root/my-product-team/output/docs/adr/ (one .md per ADR)\n"
            "  /root/my-product-team/output/docs/dev-setup.md\n\n"
            "Acceptance Criteria:\n"
            "  All 7 ADRs written with: Context, Decision, Consequences.\n"
            "  Dev setup guide runnable by a new engineer in < 30 minutes.\n"
            "  All env vars documented."
        ),
    },
]

# ── MAIN ──────────────────────────────────────────────────────────────────────
def main():
    created_keys = []

    print("=" * 65)
    print("  ION CRM — Sprint 0 Jira Ticket Creator  (v2)")
    print("=" * 65)
    print(f"  Project : {PROJECT_KEY}")
    print(f"  Sprint  : {SPRINT_NAME}")
    print(f"  Stories : {len(STORIES)} + 1 Epic")
    print("=" * 65)
    print()

    # 1. Verify project exists
    project = check_project()
    if not project:
        sys.exit(1)
    project_id = project["id"]

    # 2. Issue types
    issue_types = get_issue_types(project_id)
    epic_type_id  = issue_types.get("Epic")
    story_type_id = issue_types.get("Story") or issue_types.get("Task")
    if not story_type_id and issue_types:
        story_type_id = list(issue_types.values())[0]

    # 3. Board + Sprint
    board_id  = get_board_id()
    sprint    = get_or_create_sprint(board_id) if board_id else None
    sprint_id = sprint["id"] if sprint else None

    # 4. Create Epic
    print(f"\n📌 Creating Epic...")
    epic_payload = {
        "fields": {
            "project":   {"key": PROJECT_KEY},
            "summary":   "Sprint 0 — Analysis & Architecture",
            "issuetype": {"id": epic_type_id} if epic_type_id else {"name": "Epic"},
            "description": adf(
                "Sprint 0 — Analysis & Architecture\n\n"
                "Goal: Analyze the legacy MSSQL database, design the target PostgreSQL schema, "
                "define all API contracts, design the sync service, and document all architectural decisions.\n\n"
                "No production code is written in this sprint. All output is documentation "
                "for human review before Sprint 1 begins.\n\n"
                "Agents: Architect Agent, .NET Dev Agent\n"
                "Duration: 1-2 days\n"
                "Stories: 6\n"
                "Estimated cost: ~$8-12"
            ),
            "labels":                ["architecture", "sprint-0"],
            "customfield_10014":     "Sprint 0 — Analysis & Architecture",  # Epic Name
            "customfield_10016":     29,   # Story Points (sum of all stories)
        }
    }
    if sprint_id:
        epic_payload["fields"]["customfield_10020"] = sprint_id

    epic_key = create_issue(epic_payload, "Sprint 0 — Analysis & Architecture (Epic)")
    if epic_key:
        created_keys.append(epic_key)

    # 5. Create Stories
    print(f"\n📝 Creating {len(STORIES)} Stories...")
    for story in STORIES:
        payload = {
            "fields": {
                "project":     {"key": PROJECT_KEY},
                "summary":     story["title"],
                "issuetype":   {"id": story_type_id} if story_type_id else {"name": "Story"},
                "description": adf(story["description"]),
                "labels":      story["labels"],
                "customfield_10016": story["points"],  # Story Points
            }
        }
        if epic_key:
            payload["fields"]["customfield_10014"] = epic_key   # Epic Link (classic)
            payload["fields"]["parent"]            = {"key": epic_key}  # Next-gen parent
        if sprint_id:
            payload["fields"]["customfield_10020"] = sprint_id

        key = create_issue(payload, f"{story['id']} — {story['title'][:55]}...")
        if key:
            created_keys.append(key)

        time.sleep(0.4)

    # 6. Summary
    print()
    print("=" * 65)
    print("  🎉 SPRINT 0 TICKETS CREATED")
    print("=" * 65)
    labels = ["EPIC "] + [f"S{i+1}   " for i in range(len(STORIES))]
    story_ids = ["ION-S0-00 (Epic)"] + [s["id"] for s in STORIES]
    for i, key in enumerate(created_keys):
        tag  = "EPIC " if i == 0 else f"S{i}    "
        sid  = story_ids[i] if i < len(story_ids) else ""
        url  = f"{BASE_URL}/browse/{key}"
        print(f"  🎫 {tag} {key:12s}  {url}")

    print()
    print(f"  Total tickets : {len(created_keys)} / {len(STORIES)+1}")
    print(f"  Sprint        : {SPRINT_NAME}")
    print(f"  Board         : {BASE_URL}/jira/software/projects/{PROJECT_KEY}/boards")

    # Save results
    result = {
        "sprint": SPRINT_NAME,
        "project": PROJECT_KEY,
        "created_keys": created_keys,
        "epic_key": created_keys[0] if created_keys else None,
        "story_keys": created_keys[1:],
        "links": [f"{BASE_URL}/browse/{k}" for k in created_keys],
        "board": f"{BASE_URL}/jira/software/projects/{PROJECT_KEY}/boards"
    }
    out_path = "/root/my-product-team/output/jira-sprint0-result.json"
    with open(out_path, "w") as f:
        json.dump(result, f, indent=2)
    print(f"\n  💾 Results saved → {out_path}")

    return created_keys

if __name__ == "__main__":
    main()
