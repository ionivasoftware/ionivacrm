"""
Frontend Developer Agent
=========================
Builds React frontend features based on tasks in todo.md.
Reads CLAUDE.md for project-specific rules before every task.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from agents.base_agent import BaseAgent
from agents.project_config import get_code_dir, find_frontend_dir

WORKSPACE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

SYSTEM_PROMPT = """
You are a Senior React Frontend Developer.

Your expertise:
- React 18 + TypeScript
- Vite (build tool)
- shadcn/ui + Tailwind CSS
- Zustand (global state)
- TanStack Query / React Query (server state)
- React Router v6
- Axios with JWT interceptors
- React Hook Form + Zod (forms)
- Recharts (charts)

How you work:
1. Read CLAUDE.md first — it contains project rules, stack, existing pages, and design decisions
2. Read existing files before editing — understand current structure first
3. After every change run: npm run build
4. Fix all TypeScript errors before reporting done
5. Never use `any` types — use proper TypeScript

General coding rules:
- Use React Query for ALL API calls — no raw axios in components
- Keep API calls in src/api/ files, not inside components
- Types go in src/types/index.ts
- Follow existing file and folder naming conventions from the project

Always read CLAUDE.md before starting any task.
"""


class FrontendDevAgent(BaseAgent):
    name = "Frontend Developer"
    emoji = "🎨"
    color = "magenta"
    ALLOWED_TOOLS = [
        "Read", "Write", "Edit",
        "Glob", "Bash", "WebSearch"
    ]

    def get_system_prompt(self) -> str:
        return SYSTEM_PROMPT

    async def run_task(self, task: str) -> str:
        """Execute a specific frontend task."""
        frontend_dir = find_frontend_dir() or get_code_dir()
        claude_md    = os.path.join(WORKSPACE, ".claude", "CLAUDE.md")

        prompt = f"""
        Read this file first:
        1. {claude_md} — project rules, stack, existing pages and design system

        Implement the following task:
        {task}

        Steps:
        1. Read relevant existing files before making changes
        2. Implement following the project's conventions (types in src/types/, API calls in src/api/)
        3. Run: cd {frontend_dir} && npm run build
        4. Fix all TypeScript errors
        5. Report: what was created/modified and build result
        """
        return await self.run(prompt, frontend_dir, task_label=task)

    async def run_todo(self) -> str:
        """Read todo.md and implement all frontend tasks."""
        frontend_dir = find_frontend_dir() or get_code_dir()
        claude_md    = os.path.join(WORKSPACE, ".claude", "CLAUDE.md")
        todo_md      = os.path.join(WORKSPACE, ".claude", "projectFiles", "todo.md")

        prompt = f"""
        Read these files first:
        1. {claude_md} — project rules, stack, existing pages, design system
        2. {todo_md} — pending tasks

        Find all frontend tasks in todo.md and implement them one by one.

        For each task:
        1. Read relevant existing source files first
        2. Implement the change following the project's conventions
        3. Run npm run build after each task — fix errors before moving on
        4. Mark the task complete in todo.md

        After all tasks:
        cd {frontend_dir} && npm run build
        Report: which tasks were completed and build result.
        """
        return await self.run(prompt, frontend_dir)
