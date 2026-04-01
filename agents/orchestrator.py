"""
Orchestrator Agent (PM)
========================
1. Builds a project state snapshot (git, build status)
2. Reads CLAUDE.md + todo.md via LLM → writes task plan JSON
3. Shows plan, then dispatches tasks to specialist agents
4. Independent tasks run in parallel; dependent tasks wait
5. After each task: validates build, retries once on failure
6. Agents share session memory — they learn from each other
"""

import asyncio
import json
import os
import sys
import re

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from agents.base_agent import BaseAgent, console
from agents.session_memory import (
    clear_session_log, write_task_plan, read_task_plan,
    append_to_session_log, ensure_learned_memory_exists,
)
from agents.context_builder import build_project_snapshot, run_build_check
from agents.project_config import get_code_dir
from rich.panel import Panel
from rich.table import Table
from rich import box

WORKSPACE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

SYSTEM_PROMPT = """
You are the Orchestrator (Tech Lead / Project Manager) for a software development team.

Your ONLY job in this step:
1. Read CLAUDE.md to understand the project stack and rules
2. Read todo.md to find all pending tasks (lines with "- [ ]")
3. For each pending task, assign the right agent
4. Write the task plan as JSON to: agents/.task_plan.json

Agent types:
- "backend"   → .NET Core, C#, API, EF Core, repositories, DB migrations
- "frontend"  → React, TypeScript, UI pages, components, API hooks
- "mobile"    → Flutter or React Native mobile app (framework from CLAUDE.md)
- "devops"    → Deployment, Railway, CI/CD, Docker, env vars
- "qa"        → Unit/integration tests, xUnit, test coverage
- "security"  → Security audit, vulnerabilities, secrets scan
- "architect" → Solution design, DB schema, scaffolding

Task plan JSON format:
{
  "session_goal": "One line: what this session achieves",
  "tasks": [
    {
      "id": 1,
      "description": "Exact what needs to be done",
      "agent": "backend",
      "depends_on": [],
      "context": "Extra context for the agent (optional)"
    }
  ]
}

Rules:
- "depends_on": IDs that must complete before this task starts
- Tasks with empty "depends_on" run in PARALLEL
- If todo.md is empty: {"session_goal": "Yapılacak görev yok", "tasks": []}
- Write ONLY valid JSON to the file — nothing else
- If there are any "backend" tasks, always add a final "qa" task that depends on ALL backend task IDs. The qa task should write/update unit tests for the changed handlers and repositories in output/tests/IonCrm.Tests/.

After writing the file, output: PLAN_READY
"""


class OrchestratorAgent(BaseAgent):
    name  = "Orchestrator"
    emoji = "🧠"
    color = "yellow"
    ALLOWED_TOOLS = ["Read", "Write", "Glob", "Bash"]

    # Set False to skip post-task build validation (faster but less safe)
    VALIDATE_TASKS = True

    def get_system_prompt(self) -> str:
        return SYSTEM_PROMPT

    # ------------------------------------------------------------------
    # Entry point
    # ------------------------------------------------------------------

    async def orchestrate(self) -> None:
        console.print(Panel(
            "[bold yellow]🧠 Orchestrator Başlatılıyor[/bold yellow]\n\n"
            "  1. Proje durumu snapshot alınıyor\n"
            "  2. todo.md analiz ediliyor\n"
            "  3. Görev planı oluşturuluyor\n"
            "  4. Ajanlar çalıştırılıyor",
            border_style="yellow"
        ))

        # Fresh session
        clear_session_log()
        ensure_learned_memory_exists()

        # Write project snapshot as the first session log entry
        snapshot = build_project_snapshot()
        if snapshot:
            append_to_session_log("Sistem", "Proje durumu snapshot", snapshot)
            console.print(f"[dim]{snapshot[:300]}[/dim]")

        # Generate plan
        plan = await self._generate_plan()
        if not plan or not plan.get("tasks"):
            console.print("[dim]Yapılacak görev bulunamadı. todo.md kontrol edin.[/dim]")
            return

        self._print_plan(plan)

        # Execute
        results = await self._execute_plan(plan)

        passed = sum(1 for ok in results.values() if ok)
        total  = len(results)

        # Auto commit + push if any task succeeded
        if passed > 0:
            await self._git_commit_push(plan.get("session_goal", "chore: session tasks"))

        color  = "green" if passed == total else "yellow"
        console.print(Panel(
            f"[bold {color}]{'✅' if passed == total else '⚠️'} Oturum Tamamlandı[/bold {color}]\n\n"
            f"Hedef  : {plan.get('session_goal', '')}\n"
            f"Sonuç  : {passed}/{total} görev başarılı",
            border_style=color
        ))

    # ------------------------------------------------------------------
    # Plan generation
    # ------------------------------------------------------------------

    async def _generate_plan(self) -> dict:
        claude_md      = os.path.join(WORKSPACE, ".claude", "CLAUDE.md")
        todo_md        = os.path.join(WORKSPACE, ".claude", "projectFiles", "todo.md")
        task_plan_file = os.path.join(WORKSPACE, "agents", ".task_plan.json")

        prompt = (
            f"Read these files:\n"
            f"1. {claude_md}\n"
            f"2. {todo_md}\n\n"
            f"Find all lines starting with '- [ ]' (pending tasks).\n"
            f"Write the task plan JSON to: {task_plan_file}\n"
            f"Then output: PLAN_READY"
        )

        await self.run(prompt, working_dir=get_code_dir(), task_label="todo.md → görev planı", max_turns=10)

        plan = read_task_plan()
        if plan:
            return plan

        console.print("[yellow]⚠️  Plan dosyası oluşturulamadı.[/yellow]")
        return {"session_goal": "Plan oluşturulamadı", "tasks": []}

    # ------------------------------------------------------------------
    # Plan display
    # ------------------------------------------------------------------

    def _print_plan(self, plan: dict):
        table = Table(
            title=f"📋 {plan.get('session_goal', 'Görev Planı')}",
            box=box.ROUNDED, border_style="yellow"
        )
        table.add_column("ID",      style="dim",   width=4)
        table.add_column("Görev",   style="white", no_wrap=False)
        table.add_column("Ajan",    style="cyan",  width=12)
        table.add_column("Bağımlı", style="dim",   width=10)

        for t in plan["tasks"]:
            deps = ", ".join(str(d) for d in t.get("depends_on", [])) or "—"
            table.add_row(str(t["id"]), t["description"], t["agent"], deps)

        console.print()
        console.print(table)
        console.print()

    # ------------------------------------------------------------------
    # Execution — parallel waves, dependency-aware
    # ------------------------------------------------------------------

    async def _execute_plan(self, plan: dict) -> dict[int, bool]:
        """
        Execute tasks in dependency order.
        Tasks whose depends_on are all satisfied run in parallel.
        Returns {task_id: success_bool}.
        """
        tasks     = plan["tasks"]
        completed: set[int]  = set()
        results:   dict[int, bool] = {}

        while len(completed) < len(tasks):
            ready = [
                t for t in tasks
                if t["id"] not in completed
                and all(dep in completed for dep in t.get("depends_on", []))
            ]

            if not ready:
                console.print("[red]⚠️  Çözülemeyen bağımlılık. Duruyorum.[/red]")
                break

            if len(ready) == 1:
                t = ready[0]
                console.print(f"\n[bold]▶ Görev {t['id']}: {t['description']}[/bold]")
                ok = await self._run_task(t)
                results[t["id"]] = ok
                completed.add(t["id"])
            else:
                ids = [t["id"] for t in ready]
                console.print(f"\n[bold]▶ Paralel görevler: {ids}[/bold]")
                oks = await asyncio.gather(*[self._run_task(t) for t in ready])
                for t, ok in zip(ready, oks):
                    results[t["id"]] = ok
                    completed.add(t["id"])

        return results

    async def _run_task(self, task: dict) -> bool:
        """Run one task; validate build result; retry once on failure."""
        agent_type  = task["agent"]
        description = task["description"]
        context     = task.get("context", "")
        full_task   = f"{description}\n\n{context}".strip() if context else description

        agent = self._get_agent(agent_type)
        try:
            await agent.run_task(full_task)
        except Exception as e:
            console.print(
                f"\n[bold red]❌ Görev {task['id']} agent hatası: {e}[/bold red]\n"
                f"[dim]Görev atlanıyor, bir sonrakine geçiliyor.[/dim]\n"
            )
            return False

        if not self.VALIDATE_TASKS:
            self._mark_todo_done(description)
            return True

        ok, error = run_build_check(agent_type)
        if ok:
            self._mark_todo_done(description)
            return True

        # --- Retry once with error context ---
        from rich.markup import escape as rich_escape
        console.print(
            f"\n[yellow]⚠️  Görev {task['id']} build doğrulama başarısız — retry...[/yellow]\n"
            + rich_escape(error[:300]) + "\n"
        )
        try:
            await agent.run_task(
                f"Build hatası oluştu. Sadece bu hatayı düzelt:\n\n"
                f"```\n{error[:600]}\n```\n\n"
                f"Orijinal görev: {description}"
            )
        except Exception as e:
            console.print(f"[red]❌ Retry agent hatası: {e}[/red]\n")
            return False

        ok2, error2 = run_build_check(agent_type)
        if not ok2:
            console.print(
                f"[red]❌ Görev {task['id']} retry sonrası da başarısız. Devam ediliyor.[/red]\n"
                + rich_escape(error2[:200])
            )
            return False

        self._mark_todo_done(description)
        return True

    def _mark_todo_done(self, description: str) -> None:
        """Mark the matching todo item as done (- [ ] → - [x])."""
        todo_md = os.path.join(WORKSPACE, ".claude", "projectFiles", "todo.md")
        try:
            with open(todo_md, encoding="utf-8") as f:
                content = f.read()

            lines = content.splitlines(keepends=True)
            desc_lower = description.lower()

            # Build a list of candidate substrings to search for:
            # 1. First 60 chars of description
            # 2. First 30 chars
            # 3. Any 20-char window from the description
            candidates = [
                description[:60].strip().lower(),
                description[:30].strip().lower(),
            ]
            # Also try words 3-8 (skip "[BACKEND]"/[FRONTEND] prefix words)
            words = description.split()
            if len(words) >= 5:
                candidates.append(" ".join(words[1:5]).lower())
                candidates.append(" ".join(words[2:6]).lower())

            for i, line in enumerate(lines):
                if not line.startswith("- [ ]"):
                    continue
                line_lower = line.lower()
                if any(c and c in line_lower for c in candidates):
                    lines[i] = line.replace("- [ ]", "- [x]", 1)
                    with open(todo_md, "w", encoding="utf-8") as f:
                        f.writelines(lines)
                    console.print(f"[dim green]✓ todo.md güncellendi: {description[:50]}[/dim green]")
                    return
        except Exception as e:
            console.print(f"[dim yellow]todo.md güncellenemedi: {e}[/dim yellow]")

    # ------------------------------------------------------------------
    # Git commit + push
    # ------------------------------------------------------------------

    async def _git_commit_push(self, session_goal: str) -> None:
        """Stage all changes, commit, and push to origin main."""
        import subprocess
        code_dir = get_code_dir()

        # Build commit message from session goal
        goal = session_goal.strip()
        if not goal.lower().startswith(("feat:", "fix:", "chore:", "refactor:", "ci:")):
            goal = f"feat: {goal}"

        console.print(f"\n[bold cyan]🔀 Git commit + push başlatılıyor...[/bold cyan]")
        try:
            # Check if there's anything to commit
            status = subprocess.run(
                ["git", "status", "--porcelain"],
                cwd=code_dir, capture_output=True, text=True
            )
            if not status.stdout.strip():
                console.print("[dim]Git: değişiklik yok, commit atlanıyor.[/dim]")
                return

            subprocess.run(["git", "add", "-A"], cwd=code_dir, check=True)
            subprocess.run(
                ["git", "commit", "-m", goal],
                cwd=code_dir, check=True
            )
            subprocess.run(
                ["git", "push", "origin", "main"],
                cwd=code_dir, check=True
            )
            console.print(f"[bold green]✅ Git push tamamlandı: {goal}[/bold green]")
        except subprocess.CalledProcessError as e:
            console.print(f"[bold red]❌ Git işlemi başarısız: {e}[/bold red]")

    # ------------------------------------------------------------------
    # Agent factory
    # ------------------------------------------------------------------

    def _get_agent(self, agent_type: str) -> BaseAgent:
        from agents.dotnet_dev     import DotNetDevAgent
        from agents.frontend_dev   import FrontendDevAgent
        from agents.devops_agent   import DevOpsAgent
        from agents.qa_agent       import QAAgent
        from agents.security_agent import SecurityAgent
        from agents.architect      import ArchitectAgent
        from agents.mobile_dev     import MobileDevAgent

        mapping = {
            "backend":   DotNetDevAgent,
            "frontend":  FrontendDevAgent,
            "mobile":    MobileDevAgent,
            "devops":    DevOpsAgent,
            "qa":        QAAgent,
            "security":  SecurityAgent,
            "architect": ArchitectAgent,
        }
        AgentClass = mapping.get(agent_type.lower(), DotNetDevAgent)
        return AgentClass(cost_tracker=self.cost_tracker)
