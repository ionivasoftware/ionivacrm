"""
DevOps Agent
=============
Handles deployments, CI/CD, and infrastructure.
Reads CLAUDE.md for project-specific environment and deploy rules.
Only asks the user for secrets — everything else it figures out autonomously.
"""

import asyncio
import os
import sys
import getpass

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from agents.base_agent import BaseAgent, console
from agents.project_config import get_code_dir
from rich.panel import Panel

WORKSPACE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

SYSTEM_PROMPT = """
You are a Senior DevOps Engineer.

Your job:
1. Read CLAUDE.md to understand the project's deploy targets, services, and environment setup
2. Deploy, fix issues, and configure infrastructure autonomously
3. Only ask the user for secrets (passwords, API keys, tokens) — never guess these
4. Keep trying until deployment succeeds

How you work:
- Always check current state first (logs, variables, health endpoints)
- Fix issues one by one — diagnose root cause, don't retry blindly
- Test locally (build succeeds) before deploying
- Commit fixes to git before deploying
- Verify deployment after success (health check endpoint)

Common deployment patterns:
- .NET API: dotnet build → docker build → push → deploy
- Frontend: npm run build → static deploy or container
- DB migrations: run against target DB after deploy
- Health check: GET /health must return 200

Error handling:
- Connection string format errors → check CLAUDE.md for correct format
- Port binding errors → check EXPOSE in Dockerfile matches runtime config
- Migration errors → check for schema drift, run idempotent migrations
- Package version errors → check compatibility with target framework version

When you need a credential, output EXACTLY:
NEED_CREDENTIAL:key_name:description
Example: NEED_CREDENTIAL:DB_PASSWORD:Production database password

When deployment succeeds, output:
DEPLOYMENT_SUCCESS:url
Example: DEPLOYMENT_SUCCESS:https://api.example.com

Always read CLAUDE.md before starting — it has service names, URLs, and deploy platform details.
"""


class DevOpsAgent(BaseAgent):
    name = "DevOps Engineer"
    emoji = "🚀"
    color = "bright_blue"
    ALLOWED_TOOLS = [
        "Read", "Write", "Edit",
        "Glob", "Bash", "WebSearch"
    ]

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.credentials = {}

    def get_system_prompt(self) -> str:
        return SYSTEM_PROMPT

    def _inject_credentials(self, prompt: str) -> str:
        if self.credentials:
            creds_text = "\n".join(f"{k}={v}" for k, v in self.credentials.items())
            return f"Available credentials:\n{creds_text}\n\n{prompt}"
        return prompt

    async def run_task(self, task: str) -> str:
        """Execute a specific DevOps task."""
        code_dir  = get_code_dir()
        claude_md = os.path.join(WORKSPACE, ".claude", "CLAUDE.md")

        prompt = self._inject_credentials(f"""
        Read this file first:
        1. {claude_md} — project deploy targets, services, and environment

        Complete the following task:
        {task}

        If you need a secret: NEED_CREDENTIAL:key_name:description
        """)
        return await self.run(prompt, code_dir, task_label=task)

    async def deploy(self) -> str:
        """Main deployment loop — reads CLAUDE.md and deploys."""
        code_dir  = get_code_dir()
        claude_md = os.path.join(WORKSPACE, ".claude", "CLAUDE.md")
        todo_md   = os.path.join(WORKSPACE, ".claude", "projectFiles", "todo.md")

        console.print(Panel(
            "[bold bright_blue]🚀 DevOps Agent Starting[/bold bright_blue]\n\n"
            "  ✅ Reads CLAUDE.md for deploy config\n"
            "  ✅ Fixes errors autonomously\n"
            "  ✅ Only asks for secrets\n"
            "  ✅ Keeps trying until success",
            border_style="bright_blue"
        ))

        prompt = self._inject_credentials(f"""
        Read these files first:
        1. {claude_md} — project deploy targets, service names, environment details
        2. {todo_md} — any pending deploy or infra tasks

        Then perform the deployment:
        1. Check current state (logs, variables, running services)
        2. Build the project locally
        3. Fix any build errors
        4. Deploy to the target environment described in CLAUDE.md
        5. Verify the deployment (health check)

        If you need a secret: NEED_CREDENTIAL:key_name:description
        When done: DEPLOYMENT_SUCCESS:url
        """)

        result = ""
        max_attempts = 3

        for attempt in range(1, max_attempts + 1):
            console.print(f"\n[dim]Attempt {attempt}/{max_attempts}[/dim]\n")
            result = await self.run(prompt, code_dir)

            if "NEED_CREDENTIAL:" in result:
                await self._handle_credential_request(result)
                prompt = self._inject_credentials(prompt)
                continue

            if "DEPLOYMENT_SUCCESS:" in result:
                url = result.split("DEPLOYMENT_SUCCESS:")[1].split()[0]
                console.print(Panel(
                    f"[bold green]🎉 Deployment successful![/bold green]\n\nURL: {url}",
                    border_style="green"
                ))
                return url

            if attempt < max_attempts and ("crashed" in result.lower() or "failed" in result.lower()):
                console.print(f"[yellow]⚠️  Attempt failed, retrying ({attempt+1}/{max_attempts})...[/yellow]")
                prompt = self._inject_credentials(
                    f"Previous attempt failed. Check logs and fix the root cause.\n\n"
                    f"Previous output (last 500 chars):\n{result[-500:]}\n\nTry again."
                )

        return result

    async def run_todo(self) -> str:
        """Read todo.md and execute all DevOps/infra tasks."""
        code_dir  = get_code_dir()
        claude_md = os.path.join(WORKSPACE, ".claude", "CLAUDE.md")
        todo_md   = os.path.join(WORKSPACE, ".claude", "projectFiles", "todo.md")

        prompt = self._inject_credentials(f"""
        Read these files first:
        1. {claude_md} — project deploy targets, services, and environment
        2. {todo_md} — pending tasks

        Find all DevOps/infrastructure tasks and execute them.
        Report what was done and current deployment status.
        """)
        return await self.run(prompt, code_dir)

    async def _handle_credential_request(self, agent_output: str):
        lines = agent_output.split("\n")
        for line in lines:
            if "NEED_CREDENTIAL:" in line:
                parts = line.split("NEED_CREDENTIAL:")[1].split(":")
                key   = parts[0].strip()
                desc  = parts[1].strip() if len(parts) > 1 else key
                if key not in self.credentials:
                    value = getpass.getpass(f"🔑 {desc}: ")
                    self.credentials[key] = value
                    console.print(f"[green]✅ {key} received[/green]")
