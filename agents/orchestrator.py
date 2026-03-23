"""
Orchestrator Agent (PM)
========================
Reads the product spec, plans sprints, creates Jira tickets,
manages the team, and coordinates approval gates.
"""

import os
import sys
import json

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from agents.base_agent import BaseAgent, console
from hooks.approval_gate import gate_sprint_plan

WORKSPACE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

SYSTEM_PROMPT = """
You are the Orchestrator (Product Manager) for ION CRM — a multi-tenant SaaS CRM.

Your responsibilities:
1. Read CLAUDE.md for project rules
2. Analyze requirements and old DB data (if provided)
3. Break product into Epics → Stories → Tasks
4. Plan sprints with clear goals and agent assignments
5. Create Jira tickets for every story
6. Coordinate agents and track progress
7. ALWAYS pause for human approval before starting a sprint

ION CRM Context:
- Multi-tenant: SuperAdmin sees all, users scoped to their project(s)
- Two SaaS projects sync every 15 minutes INTO ION CRM
- ION CRM sends instant updates BACK to SaaS (subscriptions etc.)
- One-time migration of old customer data from MSSQL .bak file
- Stack: .NET Core 8, Clean Architecture, React, shadcn/ui, PostgreSQL

Sprint Structure (suggest this, human approves):
SPRINT 0 — Analysis & Architecture (Architect agent)
  - Analyze old .bak file
  - Design new DB schema
  - Define API contracts
  - Project structure

SPRINT 1 — Foundation
  - Solution scaffold
  - Auth (JWT, roles, multi-project)
  - Base entities & migrations
  - CI/CD pipeline

SPRINT 2 — Customer Core
  - Customer CRUD (multi-tenant)
  - Contact history
  - Notes & tasks

SPRINT 3 — Sync Service
  - Background service
  - SaaS A & B sync endpoints
  - Instant callback to SaaS

SPRINT 4 — Sales Pipeline
  - Opportunities
  - Pipeline board
  - Performance tracking

SPRINT 5 — Frontend
  - React app
  - Dark mode
  - Mobile responsive
  - All screens

SPRINT 6 — Migration & Testing
  - One-time data migration from .bak
  - Full test suite
  - Security audit
  - Deploy to Railway

When creating Jira tickets, format each as:
  Title: [AGENT] Short description
  Description: Detailed requirements
  Story Points: 1-8
  Sprint: Sprint name
  Labels: backend/frontend/devops/testing

Always produce a sprint plan as a JSON summary:
{
  "name": "Sprint X — Name",
  "goal": "What will be achieved",
  "story_count": N,
  "agents": "agent1, agent2",
  "duration": "2-3 days",
  "token_cost": "~$X-Y",
  "stories": [...]
}

Read CLAUDE.md first before doing anything else.
"""


class OrchestratorAgent(BaseAgent):
    name = "Orchestrator"
    emoji = "🧠"
    color = "yellow"
    ALLOWED_TOOLS = [
        "Read", "Write", "Glob", "WebSearch",
        "Bash",   # for git operations and Jira CLI
    ]

    def get_system_prompt(self) -> str:
        return SYSTEM_PROMPT

    async def plan_sprints(self) -> dict:
        """Read project context and produce sprint plan."""
        prompt = f"""
        Read the following files first:
        1. {WORKSPACE}/CLAUDE.md
        2. {WORKSPACE}/input/notes.txt (if exists)
        3. List files in {WORKSPACE}/input/database/ to see what DB files exist

        Then produce a complete sprint plan for ION CRM with:
        - All sprints listed (Sprint 0 through Sprint 6)
        - Each sprint has: name, goal, stories list, agent assignments, estimated duration
        - Stories are detailed enough for developers to implement
        - Jira ticket format for each story

        Focus on:
        - Multi-tenant architecture (SuperAdmin + Project-scoped users)
        - Two SaaS projects syncing every 15min → CRM
        - CRM instant callbacks → SaaS (subscriptions etc.)
        - One-time migration from old MSSQL .bak file
        - Mobile-first React frontend with dark mode

        Output the full sprint plan as structured text, then a JSON summary
        of Sprint 0 (the first one to start with) for approval.
        """

        result = await self.run(prompt)
        return self._extract_sprint_json(result)

    def _extract_sprint_json(self, text: str) -> dict:
        """Extract sprint JSON from agent output."""
        import re
        # Look for JSON block in output
        match = re.search(r'\{[^{}]*"name"[^{}]*"goal"[^{}]*\}', text, re.DOTALL)
        if match:
            try:
                return json.loads(match.group())
            except Exception:
                pass
        # Return default if not found
        return {
            "name": "Sprint 0 — Analysis & Architecture",
            "goal": "Analyze old DB, design new schema, scaffold project",
            "story_count": 6,
            "agents": "Architect, .NET Dev",
            "duration": "1-2 days",
            "token_cost": "~$8-12",
        }

    async def create_jira_tickets(self, sprint_data: dict):
        """Create Jira tickets for approved sprint."""
        jira_host  = os.getenv("JIRA_HOST", "")
        jira_email = os.getenv("JIRA_EMAIL", "")
        jira_token = os.getenv("JIRA_API_TOKEN", "")
        project    = os.getenv("JIRA_PROJECT_KEY", "IONCRM")

        if not all([jira_host, jira_email, jira_token]):
            console.print("[yellow]⚠️  Jira credentials not set — skipping ticket creation[/yellow]")
            return

        prompt = f"""
        Create Jira tickets for the following sprint using the Jira REST API.

        Jira settings:
        - Host: {jira_host}
        - Email: {jira_email}
        - API Token: (use JIRA_API_TOKEN env var)
        - Project Key: {project}

        Sprint data:
        {json.dumps(sprint_data, indent=2)}

        For each story, use curl to POST to Jira API:
        POST {jira_host}/rest/api/3/issue
        Authorization: Basic base64(email:token)
        Content-Type: application/json

        Create: Epic first, then Stories under the Epic.
        Set sprint, story points, and labels correctly.
        Print the created ticket IDs when done.
        """

        await self.run(prompt)
