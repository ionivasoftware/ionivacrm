"""
Architect Agent
===============
Designs solution structure, DB schema, and API contracts.
Reads CLAUDE.md for project context before every task.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from agents.base_agent import BaseAgent
from agents.project_config import get_code_dir

WORKSPACE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

SYSTEM_PROMPT = """
You are a Senior Solution Architect.

Your expertise:
- Clean Architecture for .NET Core
- Domain-Driven Design (DDD)
- Multi-tenant SaaS patterns
- PostgreSQL / EF Core schema design
- REST API contract design
- Background service patterns
- CI/CD and infrastructure design

How you work:
1. Read CLAUDE.md first — it contains the project stack, architecture decisions, and existing structure
2. Design before implementing — document decisions in docs/ before writing code
3. Use dotnet CLI to scaffold projects (never write .csproj by hand)
4. Add NuGet packages with: dotnet add package

General principles:
- Design for the actual requirements — do not over-engineer
- Follow the layer boundaries defined in CLAUDE.md
- Document DB schema changes in docs/ so other agents know what to expect
- Idempotent migrations — use ADD COLUMN IF NOT EXISTS patterns

Always read CLAUDE.md before starting any task.
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
        return SYSTEM_PROMPT

    async def run_task(self, task: str) -> str:
        """Execute a specific architecture task."""
        code_dir  = get_code_dir()
        claude_md = os.path.join(WORKSPACE, ".claude", "CLAUDE.md")

        prompt = f"""
        Read this file first:
        1. {claude_md} — project rules, stack, and existing architecture

        Complete the following task:
        {task}

        Document any schema or API contract changes in {code_dir}/docs/
        so other agents have a reference.

        Run dotnet build at the end to verify any scaffolding compiles.
        """
        return await self.run(prompt, code_dir, task_label=task)

    async def run_todo(self) -> str:
        """Read todo.md and execute all architecture tasks."""
        code_dir  = get_code_dir()
        claude_md = os.path.join(WORKSPACE, ".claude", "CLAUDE.md")
        todo_md   = os.path.join(WORKSPACE, ".claude", "projectFiles", "todo.md")

        prompt = f"""
        Read these files first:
        1. {claude_md} — project rules, stack, and existing architecture
        2. {todo_md} — pending tasks

        Find all architecture/design tasks in todo.md and complete them.

        For each task:
        1. Read relevant existing files first
        2. Design the solution and document it in {code_dir}/docs/
        3. Implement scaffolding or schema changes
        4. Verify with dotnet build

        Report: what was designed/created and any decisions made.
        """
        return await self.run(prompt, code_dir)
