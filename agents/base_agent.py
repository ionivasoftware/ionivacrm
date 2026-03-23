"""
Base Agent
==========
Shared functionality for all ION CRM agents.
"""

import asyncio
import os
import sys
from datetime import datetime
from rich.console import Console
from rich.panel import Panel
from rich.live import Live
from rich.spinner import Spinner
from rich.text import Text

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from hooks.pre_tool_safety import pre_tool_hook
from hooks.cost_tracker import CostTracker

try:
    from claude_agent_sdk import query, ClaudeAgentOptions
except ImportError:
    raise ImportError(
        "claude_agent_sdk not found.\n"
        "Run: pip install claude-agent-sdk"
    )

console = Console()

WORKSPACE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUTPUT_DIR = os.path.join(WORKSPACE, "output")


class BaseAgent:
    name: str = "Base Agent"
    emoji: str = "🤖"
    color: str = "cyan"

    ALLOWED_TOOLS = [
        "Read", "Write", "Edit", "MultiEdit",
        "Glob", "Bash", "WebSearch"
    ]

    def __init__(self, cost_tracker: CostTracker = None):
        self.cost_tracker = cost_tracker or CostTracker()
        self.output_dir = OUTPUT_DIR
        os.makedirs(self.output_dir, exist_ok=True)

    def get_system_prompt(self) -> str:
        raise NotImplementedError

    def get_options(self) -> ClaudeAgentOptions:
        return ClaudeAgentOptions(
            allowed_tools=self.ALLOWED_TOOLS,
            permission_mode="acceptEdits",
            system_prompt=self.get_system_prompt(),
            max_turns=50,
        )

    def _safe_pre_tool(self, tool_name: str, tool_input: dict) -> bool:
        result = pre_tool_hook(tool_name, tool_input)
        if not result.get("allow", True):
            console.print(f"[red]{result.get('message', 'Blocked')}[/red]")
            return False
        return True

    async def run(self, prompt: str, working_dir: str = None) -> str:
        work_dir = working_dir or self.output_dir
        os.makedirs(work_dir, exist_ok=True)

        console.print(Panel(
            f"[bold {self.color}]{self.emoji} {self.name}[/bold {self.color}]\n"
            f"[dim]{prompt[:120]}...[/dim]" if len(prompt) > 120 else
            f"[bold {self.color}]{self.emoji} {self.name}[/bold {self.color}]\n"
            f"[dim]{prompt}[/dim]",
            border_style=self.color
        ))

        result_text = ""
        input_tokens = 0
        output_tokens = 0

        try:
            options = self.get_options()
            # Set working directory in prompt context
            full_prompt = f"Working directory: {work_dir}\n\n{prompt}"

            async for message in query(prompt=full_prompt, options=options):

                # Extract text content
                if hasattr(message, "content"):
                    for block in message.content:
                        if hasattr(block, "text") and block.text:
                            console.print(block.text, end="", markup=False)
                            result_text += block.text

                # Show tool usage
                elif hasattr(message, "tool_use"):
                    tool = message.tool_use
                    inp = getattr(tool, "input", {})

                    # Safety check
                    if not self._safe_pre_tool(tool.name, inp or {}):
                        continue

                    # Display tool call
                    if isinstance(inp, dict):
                        if "command" in inp:
                            console.print(f"\n[dim cyan]🔧 {tool.name}: $ {inp['command'][:80]}[/dim cyan]")
                        elif "path" in inp:
                            console.print(f"\n[dim cyan]📄 {tool.name}: {inp['path']}[/dim cyan]")
                        else:
                            console.print(f"\n[dim cyan]🔧 {tool.name}[/dim cyan]")

                # Token tracking
                elif hasattr(message, "usage"):
                    usage = message.usage
                    input_tokens  += getattr(usage, "input_tokens", 0)
                    output_tokens += getattr(usage, "output_tokens", 0)

                # Final result
                elif hasattr(message, "result"):
                    console.print(f"\n[bold green]✅ {self.name} complete[/bold green]\n")

        except Exception as e:
            console.print(f"\n[bold red]❌ {self.name} error: {e}[/bold red]\n")
            raise

        finally:
            self.cost_tracker.record(self.name, input_tokens, output_tokens)

        return result_text

    def run_sync(self, prompt: str, working_dir: str = None) -> str:
        return asyncio.run(self.run(prompt, working_dir))
