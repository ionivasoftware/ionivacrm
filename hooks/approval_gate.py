"""
Approval Gate
=============
Pauses execution and waits for human approval at critical points.
Logs decisions for audit trail.
"""

import os
import json
from datetime import datetime
from rich.console import Console
from rich.panel import Panel
from rich.prompt import Confirm
from rich.table import Table
from rich import box

console = Console()

APPROVALS_LOG = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "logs", "approvals.log"
)


def log_approval(gate_name: str, approved: bool, notes: str = ""):
    os.makedirs(os.path.dirname(APPROVALS_LOG), exist_ok=True)
    timestamp = datetime.now().isoformat()
    status = "APPROVED" if approved else "REJECTED"
    with open(APPROVALS_LOG, "a") as f:
        f.write(f"[{timestamp}] {status} | {gate_name}\n")
        if notes:
            f.write(f"  Notes: {notes}\n")


def approval_gate(
    gate_name: str,
    summary: str,
    details: list[dict] = None,
    warning: str = None
) -> bool:
    """
    Shows a summary to the human and waits for approval.

    Args:
        gate_name: Name of this checkpoint
        summary: What is being approved
        details: List of {key, value} dicts to show in table
        warning: Optional warning message

    Returns:
        True if approved, False if rejected
    """
    console.print()
    console.print(Panel(
        f"[bold yellow]⏸  APPROVAL REQUIRED[/bold yellow]\n\n"
        f"[white]{summary}[/white]",
        title=f"[bold cyan]✋ {gate_name}[/bold cyan]",
        border_style="yellow",
        padding=(1, 2)
    ))

    if details:
        table = Table(box=box.ROUNDED, border_style="dim")
        table.add_column("Item", style="cyan", no_wrap=True)
        table.add_column("Value", style="white")
        for item in details:
            table.add_row(item.get("key", ""), str(item.get("value", "")))
        console.print(table)

    if warning:
        console.print(Panel(
            f"[bold red]⚠️  {warning}[/bold red]",
            border_style="red"
        ))

    console.print()

    try:
        approved = Confirm.ask(
            "[bold green]Do you approve? (y=continue, n=stop)[/bold green]"
        )
    except KeyboardInterrupt:
        console.print("\n[red]Interrupted — stopping.[/red]")
        approved = False

    notes = ""
    if not approved:
        try:
            notes = input("Reason for rejection (optional): ").strip()
        except Exception:
            pass

    log_approval(gate_name, approved, notes)

    if approved:
        console.print("[bold green]✅ Approved — continuing...[/bold green]\n")
    else:
        console.print("[bold red]❌ Rejected — stopping agent team.[/bold red]\n")
        if notes:
            console.print(f"[dim]Reason: {notes}[/dim]\n")

    return approved


# ── Predefined gates ───────────────────────────────────────────

def gate_sprint_plan(sprint_data: dict) -> bool:
    details = [
        {"key": "Sprint", "value": sprint_data.get("name", "")},
        {"key": "Goal", "value": sprint_data.get("goal", "")},
        {"key": "Stories", "value": sprint_data.get("story_count", 0)},
        {"key": "Agents", "value": sprint_data.get("agents", "")},
        {"key": "Est. Duration", "value": sprint_data.get("duration", "")},
        {"key": "Est. Token Cost", "value": sprint_data.get("token_cost", "")},
    ]
    return approval_gate(
        gate_name="Sprint Plan Approval",
        summary=f"The Orchestrator has planned {sprint_data.get('name')}.\n"
                f"Review the sprint contents before agents start coding.",
        details=details
    )


def gate_db_schema(schema_summary: str) -> bool:
    return approval_gate(
        gate_name="Database Schema Approval",
        summary="The Architect has designed the database schema.\n"
                "Review before EF Core migrations are generated and run.",
        details=[{"key": "Schema", "value": schema_summary}],
        warning="Once approved, EF Core will create these tables in your Supabase database."
    )


def gate_sprint_complete(sprint_name: str, completed: list[str]) -> bool:
    details = [{"key": f"Story {i+1}", "value": s} for i, s in enumerate(completed)]
    return approval_gate(
        gate_name=f"{sprint_name} — Completion Review",
        summary=f"{sprint_name} is complete.\n"
                f"Review what was built before starting the next sprint.",
        details=details
    )


def gate_deployment(env: str, version: str) -> bool:
    return approval_gate(
        gate_name="Deployment Approval",
        summary=f"Ready to deploy version {version} to {env}.\n"
                f"This will make changes live on Railway.",
        details=[
            {"key": "Environment", "value": env},
            {"key": "Version", "value": version},
            {"key": "Platform", "value": "Railway"},
        ],
        warning="This will deploy to PRODUCTION. Ensure all tests pass first."
    )
