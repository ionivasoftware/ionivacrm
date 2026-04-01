"""
QA Agent
=========
Writes and runs tests based on the project in CLAUDE.md.
Reads todo.md for specific test tasks.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from agents.base_agent import BaseAgent
from agents.project_config import get_code_dir

WORKSPACE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

SYSTEM_PROMPT = """
You are a Senior QA Engineer.

Your responsibilities:
- Write xUnit tests for .NET backend code
- Write React Testing Library tests for frontend
- Run tests and fix failures
- Ensure meaningful coverage on business logic

How you work:
1. Read CLAUDE.md first — it contains project rules, stack, and architecture
2. Read todo.md to find testing tasks
3. Read the source code you're testing before writing tests
4. Run tests after writing them — fix all failures before reporting done

Test writing principles:
- Arrange-Act-Assert strictly
- Use InMemory database for unit tests (never hit real DB)
- Use Moq for all external dependencies
- Test YOUR code — not framework or library behavior
- Name tests: MethodName_Scenario_ExpectedResult

Priority order (most important first):
1. Security boundaries — auth, authorization, tenant isolation
2. Core business logic — the unique rules of this project
3. Data integrity — saves, updates, deletes work correctly
4. Error handling — invalid input returns correct errors

Always read CLAUDE.md before starting — it defines what the business logic actually is.
"""


class QAAgent(BaseAgent):
    name = "QA Engineer"
    emoji = "🧪"
    color = "cyan"
    ALLOWED_TOOLS = ["Read", "Write", "Edit", "Glob", "Bash"]

    def get_system_prompt(self) -> str:
        return SYSTEM_PROMPT

    async def run_task(self, task: str) -> str:
        """Execute a specific QA task."""
        code_dir  = get_code_dir()
        claude_md = os.path.join(WORKSPACE, ".claude", "CLAUDE.md")

        prompt = f"""
        Read this file first:
        1. {claude_md} — project rules, stack, and business logic

        Complete the following testing task:
        {task}

        Run tests after writing them:
        cd {code_dir} && dotnet test --logger "console;verbosity=normal"

        Fix all failures. Report: X passed, Y failed.
        """
        return await self.run(prompt, code_dir, task_label=task)

    async def run_todo(self) -> str:
        """Read todo.md and execute all QA tasks."""
        code_dir  = get_code_dir()
        claude_md = os.path.join(WORKSPACE, ".claude", "CLAUDE.md")
        todo_md   = os.path.join(WORKSPACE, ".claude", "projectFiles", "todo.md")

        prompt = f"""
        Read these files first:
        1. {claude_md} — project rules, stack, and business logic
        2. {todo_md} — pending tasks

        Find all QA/testing tasks and implement them.

        For each task:
        1. Read the source code being tested
        2. Write tests following the project's test conventions
        3. Run tests — fix failures before moving on

        Final run:
        cd {code_dir} && dotnet test --logger "console;verbosity=normal"

        Report: total tests, passed, failed, skipped.
        """
        return await self.run(prompt, code_dir)
