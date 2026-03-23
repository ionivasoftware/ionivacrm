"""
Cost Tracker
============
Tracks token usage and estimated cost per agent and per run.
Sonnet 4.6: $3/MTok input, $15/MTok output
"""

import json
import os
from datetime import datetime
from rich.console import Console
from rich.table import Table
from rich import box

console = Console()

COST_LOG = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "logs", "costs.json"
)

# Sonnet 4.6 pricing
INPUT_COST_PER_TOKEN  = 3.00  / 1_000_000
OUTPUT_COST_PER_TOKEN = 15.00 / 1_000_000


class CostTracker:
    def __init__(self, run_id: str = None):
        self.run_id = run_id or datetime.now().strftime("%Y%m%d_%H%M%S")
        self.agents: dict[str, dict] = {}
        self.total_input = 0
        self.total_output = 0

    def record(self, agent_name: str, input_tokens: int, output_tokens: int):
        if agent_name not in self.agents:
            self.agents[agent_name] = {"input": 0, "output": 0, "calls": 0}

        self.agents[agent_name]["input"]  += input_tokens
        self.agents[agent_name]["output"] += output_tokens
        self.agents[agent_name]["calls"]  += 1
        self.total_input  += input_tokens
        self.total_output += output_tokens

    def agent_cost(self, agent_name: str) -> float:
        a = self.agents.get(agent_name, {})
        return (a.get("input", 0) * INPUT_COST_PER_TOKEN +
                a.get("output", 0) * OUTPUT_COST_PER_TOKEN)

    def total_cost(self) -> float:
        return (self.total_input  * INPUT_COST_PER_TOKEN +
                self.total_output * OUTPUT_COST_PER_TOKEN)

    def print_summary(self):
        table = Table(
            title=f"💰 Token Usage & Cost — Run {self.run_id}",
            box=box.ROUNDED,
            border_style="cyan"
        )
        table.add_column("Agent",         style="cyan",  no_wrap=True)
        table.add_column("Input Tokens",  style="white", justify="right")
        table.add_column("Output Tokens", style="white", justify="right")
        table.add_column("API Calls",     style="dim",   justify="right")
        table.add_column("Cost (USD)",    style="green", justify="right")

        for agent, data in self.agents.items():
            cost = self.agent_cost(agent)
            table.add_row(
                agent,
                f"{data['input']:,}",
                f"{data['output']:,}",
                str(data['calls']),
                f"${cost:.4f}"
            )

        table.add_section()
        table.add_row(
            "[bold]TOTAL[/bold]",
            f"[bold]{self.total_input:,}[/bold]",
            f"[bold]{self.total_output:,}[/bold]",
            "",
            f"[bold green]${self.total_cost():.4f}[/bold green]"
        )

        console.print()
        console.print(table)
        console.print()

    def warn_if_expensive(self, threshold_usd: float = 5.0):
        cost = self.total_cost()
        if cost > threshold_usd:
            console.print(
                f"[bold yellow]⚠️  Cost warning: ${cost:.2f} spent this run "
                f"(threshold: ${threshold_usd:.2f})[/bold yellow]"
            )

    def save(self):
        os.makedirs(os.path.dirname(COST_LOG), exist_ok=True)
        try:
            with open(COST_LOG, "r") as f:
                history = json.load(f)
        except Exception:
            history = []

        history.append({
            "run_id":        self.run_id,
            "timestamp":     datetime.now().isoformat(),
            "total_input":   self.total_input,
            "total_output":  self.total_output,
            "total_cost":    round(self.total_cost(), 6),
            "agents":        self.agents
        })

        with open(COST_LOG, "w") as f:
            json.dump(history, f, indent=2)

    def print_history(self):
        try:
            with open(COST_LOG) as f:
                history = json.load(f)
        except Exception:
            console.print("[dim]No cost history yet.[/dim]")
            return

        table = Table(title="📊 Cost History", box=box.ROUNDED)
        table.add_column("Run ID")
        table.add_column("Date")
        table.add_column("Total Tokens", justify="right")
        table.add_column("Cost", justify="right", style="green")

        total = 0
        for run in history[-10:]:  # last 10 runs
            cost = run.get("total_cost", 0)
            total += cost
            table.add_row(
                run["run_id"],
                run["timestamp"][:16],
                f"{run['total_input'] + run['total_output']:,}",
                f"${cost:.4f}"
            )

        table.add_section()
        table.add_row("", "[bold]TOTAL (last 10)[/bold]", "", f"[bold green]${total:.4f}[/bold green]")
        console.print(table)
