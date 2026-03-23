"""
ION CRM — Autonomous Development Team
======================================
Run this file to start the full agent team.

Usage:
  source .venv/bin/activate
  python main.py

The team will:
1. Orchestrator plans sprints → YOU APPROVE
2. Architect designs DB & scaffolds solution → YOU APPROVE
3. .NET Dev implements sprint by sprint → YOU APPROVE each sprint
4. Frontend Dev builds React app
5. QA runs all tests
6. Security audits code
7. GitHub Actions deploys to Railway → YOU APPROVE
"""

import asyncio
import os
import sys
from datetime import datetime
from dotenv import load_dotenv
from rich.console import Console
from rich.panel import Panel
from rich.rule import Rule

# ── Load environment ────────────────────────────────────────
load_dotenv()

# ── Validate required env vars ──────────────────────────────
REQUIRED_VARS = [
    "ANTHROPIC_API_KEY",
    "GITHUB_TOKEN",
    "GITHUB_REPO",
]

missing = [v for v in REQUIRED_VARS if not os.getenv(v)]
if missing:
    print(f"❌ Missing required environment variables: {', '.join(missing)}")
    print("   Check your .env file.")
    sys.exit(1)

# ── Imports ─────────────────────────────────────────────────
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from hooks.cost_tracker import CostTracker
from hooks.approval_gate import (
    gate_sprint_plan, gate_db_schema,
    gate_sprint_complete, gate_deployment
)
from agents.orchestrator import OrchestratorAgent
from agents.architect import ArchitectAgent
from agents.dotnet_dev import DotNetDevAgent
from agents.frontend_dev import FrontendDevAgent
from agents.qa_agent import QAAgent
from agents.security_agent import SecurityAgent

console = Console()

WORKSPACE = os.path.dirname(os.path.abspath(__file__))
OUTPUT_DIR = os.path.join(WORKSPACE, "output")


def print_banner():
    console.print(Panel(
        "[bold cyan]ION CRM[/bold cyan] — Autonomous Development Team\n\n"
        "[dim]6 AI agents working together to build your product[/dim]\n\n"
        "  🧠 Orchestrator  →  Sprint planning & Jira\n"
        "  🏗️  Architect     →  DB design & scaffolding\n"
        "  💻 .NET Dev      →  Backend API & services\n"
        "  🎨 Frontend      →  React + dark mode UI\n"
        "  🧪 QA Engineer   →  Tests & quality\n"
        "  🔒 Security      →  Audit & hardening",
        title="[bold]Welcome[/bold]",
        border_style="cyan",
        padding=(1, 4)
    ))


def print_phase(name: str, description: str):
    console.print()
    console.print(Rule(f"[bold cyan]{name}[/bold cyan]"))
    console.print(f"[dim]{description}[/dim]\n")


async def run_team():
    print_banner()

    # ── Initialize ──────────────────────────────────────────
    run_id = datetime.now().strftime("%Y%m%d_%H%M%S")
    tracker = CostTracker(run_id)
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    os.makedirs(os.path.join(WORKSPACE, "logs"), exist_ok=True)

    console.print(f"\n[dim]Run ID: {run_id}[/dim]")
    console.print(f"[dim]Output: {OUTPUT_DIR}[/dim]\n")

    try:
        # ════════════════════════════════════════════════════
        # PHASE 0 — Sprint Planning
        # ════════════════════════════════════════════════════
        print_phase(
            "PHASE 0 — Sprint Planning",
            "Orchestrator reads project context and plans all sprints"
        )

        orchestrator = OrchestratorAgent(tracker)
        sprint_data = await orchestrator.plan_sprints()

        # ✋ APPROVAL GATE 1 — Sprint Plan
        if not gate_sprint_plan(sprint_data):
            console.print("[red]Sprint plan rejected. Edit CLAUDE.md or notes.txt and re-run.[/red]")
            tracker.print_summary()
            return

        # Create Jira tickets for approved sprint
        await orchestrator.create_jira_tickets(sprint_data)

        # ════════════════════════════════════════════════════
        # PHASE 1 — Architecture & Scaffold
        # ════════════════════════════════════════════════════
        print_phase(
            "PHASE 1 — Architecture & Scaffold",
            "Architect analyzes old DB, designs schema, scaffolds .NET solution"
        )

        architect = ArchitectAgent(tracker)
        await architect.analyze_and_scaffold()

        # ✋ APPROVAL GATE 2 — DB Schema
        schema_path = os.path.join(OUTPUT_DIR, "docs", "schema.md")
        schema_summary = "See output/docs/schema.md for full schema"
        if os.path.exists(schema_path):
            with open(schema_path) as f:
                schema_summary = f.read()[:500] + "..."

        if not gate_db_schema(schema_summary):
            console.print("[red]Schema rejected. Architect will need to redesign.[/red]")
            tracker.print_summary()
            return

        # ════════════════════════════════════════════════════
        # PHASE 2 — Backend Foundation (Auth + Core)
        # ════════════════════════════════════════════════════
        print_phase(
            "PHASE 2 — Backend Foundation",
            ".NET Dev implements auth, multi-tenancy, base entities"
        )

        dotnet_dev = DotNetDevAgent(tracker)
        await dotnet_dev.implement_auth()

        if not gate_sprint_complete(
            "Sprint 1 — Foundation",
            [
                "JWT authentication with refresh tokens",
                "Multi-tenant role system",
                "Base entities + EF Core migrations",
                "Program.cs with full DI setup",
            ]
        ):
            tracker.print_summary()
            return

        # ════════════════════════════════════════════════════
        # PHASE 3 — Customer Core + Sync Service
        # ════════════════════════════════════════════════════
        print_phase(
            "PHASE 3 — Customer Core & Sync",
            ".NET Dev implements customer CRUD and 15-min sync service"
        )

        await dotnet_dev.implement_sprint(
            "Sprint 2 — Customer Core",
            [
                "Customer CRUD with multi-tenant isolation",
                "Contact history (calls, emails, meetings, notes)",
                "Task management per customer",
                "Search and filter with pagination",
            ]
        )

        await dotnet_dev.implement_sync_service()

        if not gate_sprint_complete(
            "Sprint 2 — Customer Core & Sync",
            [
                "Customer CRUD with tenant isolation",
                "Contact history endpoints",
                "15-minute SaaS sync service (Hangfire)",
                "Instant CRM → SaaS callbacks",
                "SyncLogs table and API",
            ]
        ):
            tracker.print_summary()
            return

        # ════════════════════════════════════════════════════
        # PHASE 4 — Migration Service
        # ════════════════════════════════════════════════════
        print_phase(
            "PHASE 4 — Data Migration Service",
            ".NET Dev implements one-time migration from old MSSQL data"
        )

        await dotnet_dev.implement_migration_service()

        # ════════════════════════════════════════════════════
        # PHASE 5 — Frontend
        # ════════════════════════════════════════════════════
        print_phase(
            "PHASE 5 — React Frontend",
            "Frontend Dev builds the full React app with dark mode"
        )

        frontend_dev = FrontendDevAgent(tracker)
        await frontend_dev.scaffold_frontend()
        await frontend_dev.implement_customer_module()

        if not gate_sprint_complete(
            "Sprint 5 — Frontend",
            [
                "React app with dark mode",
                "Login, Dashboard, Customers pages",
                "Customer detail with history timeline",
                "Mobile responsive (WebView ready)",
                "shadcn/ui components",
            ]
        ):
            tracker.print_summary()
            return

        # ════════════════════════════════════════════════════
        # PHASE 6 — QA & Security
        # ════════════════════════════════════════════════════
        print_phase(
            "PHASE 6 — Quality & Security",
            "QA writes tests, Security audits code"
        )

        qa_agent = QAAgent(tracker)
        security_agent = SecurityAgent(tracker)

        # Run QA and Security in parallel
        await asyncio.gather(
            qa_agent.run_tests(),
            security_agent.audit(),
        )

        if not gate_sprint_complete(
            "Sprint 6 — QA & Security",
            [
                "Unit tests written and passing",
                "Multi-tenant isolation tests",
                "Auth security tests",
                "Security audit report generated",
                "Critical vulnerabilities fixed",
            ]
        ):
            tracker.print_summary()
            return

        # ════════════════════════════════════════════════════
        # PHASE 7 — Deploy
        # ════════════════════════════════════════════════════
        print_phase(
            "PHASE 7 — Deploy to Railway",
            "Push to GitHub → GitHub Actions → Railway"
        )

        # ✋ APPROVAL GATE — Deployment
        if not gate_deployment("Railway Production", run_id):
            console.print("[yellow]Deployment skipped. Push to main branch manually when ready.[/yellow]")
        else:
            # Push to GitHub → triggers GitHub Actions
            push_prompt = f"""
            cd {WORKSPACE}
            git add output/ .github/
            git commit -m "feat: ION CRM {run_id} — full build"
            git push origin main
            
            Then check GitHub Actions:
            gh run watch
            
            Report the deployment status.
            """
            await orchestrator.run(push_prompt)

        # ════════════════════════════════════════════════════
        # DONE
        # ════════════════════════════════════════════════════
        console.print()
        console.print(Panel(
            "[bold green]🎉 ION CRM Build Complete![/bold green]\n\n"
            f"[dim]Output: {OUTPUT_DIR}[/dim]\n"
            f"[dim]Security report: {OUTPUT_DIR}/docs/security_report.md[/dim]\n"
            f"[dim]Approvals log: {WORKSPACE}/logs/approvals.log[/dim]\n\n"
            "[cyan]Next steps:[/cyan]\n"
            "  1. Set DEV_DATABASE_URL in .env\n"
            "  2. cd output && dotnet ef database update\n"
            "  3. Add production secrets to GitHub Secrets\n"
            "  4. Push to main → auto deploys to Railway",
            title="[bold]✅ Complete[/bold]",
            border_style="green",
            padding=(1, 2)
        ))

    except KeyboardInterrupt:
        console.print("\n[yellow]⚠️  Interrupted by user[/yellow]")

    except Exception as e:
        console.print(f"\n[bold red]❌ Fatal error: {e}[/bold red]")
        raise

    finally:
        # Always print cost summary
        tracker.print_summary()
        tracker.save()
        console.print(f"[dim]Cost history saved to {WORKSPACE}/logs/costs.json[/dim]")


# ── Menu ────────────────────────────────────────────────────
def show_menu():
    console.print(Panel(
        "[bold]What would you like to do?[/bold]\n\n"
        "  [cyan]1[/cyan] — Run full team (all phases)\n"
        "  [cyan]2[/cyan] — Run single agent\n"
        "  [cyan]3[/cyan] — View cost history\n"
        "  [cyan]4[/cyan] — Check setup\n"
        "  [cyan]q[/cyan] — Quit",
        border_style="dim"
    ))

    choice = input("\nChoice: ").strip().lower()

    if choice == "1":
        asyncio.run(run_team())

    elif choice == "2":
        agents_menu()

    elif choice == "3":
        tracker = CostTracker()
        tracker.print_history()

    elif choice == "4":
        check_setup()

    elif choice == "q":
        return

    else:
        console.print("[red]Invalid choice[/red]")


def agents_menu():
    console.print("\nWhich agent?\n")
    console.print("  1 — 🧠 Orchestrator (plan sprints)")
    console.print("  2 — 🏗️  Architect (scaffold solution)")
    console.print("  3 — 💻 .NET Dev (implement feature)")
    console.print("  4 — 🎨 Frontend Dev (build UI)")
    console.print("  5 — 🧪 QA (run tests)")
    console.print("  6 — 🔒 Security (audit)")

    choice = input("\nChoice: ").strip()
    tracker = CostTracker()

    async def run_single():
        if choice == "1":
            agent = OrchestratorAgent(tracker)
            task = input("Task: ").strip()
            await agent.run(task)
        elif choice == "2":
            agent = ArchitectAgent(tracker)
            await agent.analyze_and_scaffold()
        elif choice == "3":
            agent = DotNetDevAgent(tracker)
            task = input("What to implement: ").strip()
            await agent.implement_sprint("Custom", [task])
        elif choice == "4":
            agent = FrontendDevAgent(tracker)
            await agent.scaffold_frontend()
        elif choice == "5":
            agent = QAAgent(tracker)
            await agent.run_tests()
        elif choice == "6":
            agent = SecurityAgent(tracker)
            await agent.audit()
        tracker.print_summary()
        tracker.save()

    asyncio.run(run_single())


def check_setup():
    console.print("\n[bold]Checking setup...[/bold]\n")

    checks = [
        ("ANTHROPIC_API_KEY", bool(os.getenv("ANTHROPIC_API_KEY")), "Required"),
        ("GITHUB_TOKEN",      bool(os.getenv("GITHUB_TOKEN")),      "Required"),
        ("GITHUB_REPO",       bool(os.getenv("GITHUB_REPO")),       "Required"),
        ("JIRA_HOST",         bool(os.getenv("JIRA_HOST")),         "Optional (Jira integration)"),
        ("DEV_DATABASE_URL",  bool(os.getenv("DEV_DATABASE_URL")),  "Optional (run migrations)"),
        ("RAILWAY_TOKEN",     bool(os.getenv("RAILWAY_TOKEN")),     "Optional (auto-deploy)"),
    ]

    for name, ok, note in checks:
        status = "[green]✅[/green]" if ok else "[red]❌[/red]"
        console.print(f"  {status} {name} [dim]({note})[/dim]")

    # Check output dir
    console.print()
    console.print(f"  [dim]Output dir: {OUTPUT_DIR}[/dim]")
    console.print(f"  [dim]Workspace: {WORKSPACE}[/dim]")

    # Check DB files
    db_dir = os.path.join(WORKSPACE, "input", "database")
    if os.path.exists(db_dir):
        files = os.listdir(db_dir)
        if files:
            console.print(f"\n  [green]✅ DB files found:[/green]")
            for f in files:
                size = os.path.getsize(os.path.join(db_dir, f))
                console.print(f"     {f} ({size/1024/1024:.1f} MB)")
        else:
            console.print("\n  [yellow]⚠️  No DB files in input/database/[/yellow]")


if __name__ == "__main__":
    show_menu()
