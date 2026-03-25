"""
Orchestrator Agent (PM)
========================
Reads the product spec, plans sprints,
manages the team, and coordinates the agent workflow.
"""

import os
import sys
import json

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from agents.base_agent import BaseAgent, console

WORKSPACE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

SYSTEM_PROMPT = """
You are the Orchestrator (Product Manager) for ION CRM — a multi-tenant SaaS CRM.

Your responsibilities:
1. Read CLAUDE.md for project rules
2. Analyze requirements and old DB data (if provided)
3. Break product into Epics → Stories → Tasks
4. Plan sprints with clear goals and agent assignments
5. Coordinate agents and track progress

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
        "Bash",   # for git operations
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

