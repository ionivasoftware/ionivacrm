"""
.NET Core Backend Developer Agent
===================================
Implements backend features based on tasks in todo.md.
Reads CLAUDE.md for project-specific rules before every task.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from agents.base_agent import BaseAgent
from agents.project_config import get_code_dir

WORKSPACE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

SYSTEM_PROMPT = """
You are a Senior .NET Core Backend Developer.

Your expertise:
- ASP.NET Core 8 Web API
- Clean Architecture (Domain / Application / Infrastructure / API)
- CQRS with MediatR
- Entity Framework Core + PostgreSQL (Npgsql)
- JWT authentication with refresh tokens
- Role-based and multi-tenant authorization
- Background services and Hangfire
- FluentValidation
- Repository pattern
- xUnit + Moq testing

How you work:
1. Read CLAUDE.md first — it contains project rules, stack, coding conventions, and architecture decisions
2. Read existing files before editing — never overwrite blindly
3. After every change run: dotnet build
4. Fix all compiler errors before reporting done

General coding rules:
- Follow Clean Architecture layer boundaries strictly
- New DB column → add idempotent SQL in Program.cs startup block (ADD COLUMN IF NOT EXISTS)
- Background job DB queries → always use .IgnoreQueryFilters() (no HTTP context = tenant filter blocks all)
- External API dates → DateTime.SpecifyKind(dt, DateTimeKind.Utc) before saving
- Log EF SaveChanges errors with inner exception: ex.InnerException?.Message
- New entity field → update both DTO and mapping extension

Always read CLAUDE.md before starting any task.
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

    async def run_task(self, task: str) -> str:
        """Execute a specific backend task."""
        code_dir  = get_code_dir()
        claude_md = os.path.join(WORKSPACE, ".claude", "CLAUDE.md")

        prompt = f"""
        Read this file first:
        1. {claude_md} — project rules, stack, coding conventions

        Implement the following task:
        {task}

        Steps:
        1. Read all relevant existing files before making changes
        2. Implement following the project's Clean Architecture
        3. Run: cd {code_dir} && dotnet build
        4. Fix all errors
        5. Report: what was created/modified and build result
        """
        return await self.run(prompt, code_dir, task_label=task)

    async def run_todo(self) -> str:
        """Read todo.md and implement all backend tasks."""
        code_dir  = get_code_dir()
        claude_md = os.path.join(WORKSPACE, ".claude", "CLAUDE.md")
        todo_md   = os.path.join(WORKSPACE, ".claude", "projectFiles", "todo.md")

        prompt = f"""
        Read these files first:
        1. {claude_md} — project rules, stack, architecture
        2. {todo_md} — pending tasks

        Find all backend tasks in todo.md and implement them one by one.

        For each task:
        1. Read relevant existing source files
        2. Implement the change following the project's Clean Architecture
        3. Run dotnet build after each task — fix errors before moving on
        4. Mark the task complete in todo.md

        After all tasks:
        cd {code_dir} && dotnet build
        Report: which tasks were completed and build result.
        """
        return await self.run(prompt, code_dir)
