"""
ION CRM Agent Runner
=====================
Usage:
    python run.py              # Orchestrator reads todo.md and runs all agents
    python run.py --dry-run    # Only show the plan, don't execute
    python run.py --costs      # Show cost history and exit
"""

import asyncio
import argparse
import sys
import os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from hooks.cost_tracker import CostTracker
from rich.console import Console

console = Console()


async def main(dry_run: bool = False, show_costs: bool = False):
    tracker = CostTracker()

    if show_costs:
        tracker.print_history()
        return

    from agents.orchestrator import OrchestratorAgent
    from agents.session_memory import clear_session_log, SESSION_LOG

    orch = OrchestratorAgent(cost_tracker=tracker)

    if dry_run:
        # Only generate and show the plan
        console.print("\n[bold yellow]🔍 Dry run — sadece plan gösterilecek[/bold yellow]\n")
        plan = await orch._generate_plan()
        orch._print_plan(plan)
        console.print(f"[dim]Session log: {SESSION_LOG}[/dim]")
    else:
        await orch.orchestrate()

    tracker.print_summary()
    tracker.save()
    tracker.warn_if_expensive(threshold_usd=5.0)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="ION CRM Agent Runner")
    parser.add_argument("--dry-run",  action="store_true", help="Show plan only, don't execute")
    parser.add_argument("--costs",    action="store_true", help="Show cost history and exit")
    args = parser.parse_args()

    asyncio.run(main(dry_run=args.dry_run, show_costs=args.costs))
