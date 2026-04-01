"""
Base Agent
==========
Shared functionality for all agents.

Each run():
  1. Reads learned memory (cross-session facts)
  2. Reads truncated session log (what others did this session)
  3. Builds context snapshot (git state, build status)
  4. Estimates appropriate max_turns from task complexity
  5. Appends result to session log after completion
"""

import asyncio
import os
import sys
from rich.console import Console
from rich.panel import Panel

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from hooks.pre_tool_safety import pre_tool_hook
from hooks.cost_tracker import CostTracker
from agents.project_config import get_code_dir

try:
    from claude_agent_sdk import query, ClaudeAgentOptions
except ImportError:
    raise ImportError(
        "claude_agent_sdk not found.\n"
        "Run: pip install claude-agent-sdk"
    )

console = Console()

WORKSPACE           = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AGENTS_DIR          = os.path.join(WORKSPACE, "agents")
LEARNED_MEMORY_FILE = os.path.join(AGENTS_DIR, ".learned.md")

# Keywords that indicate task complexity → drives max_turns
_COMPLEX_KW = {"implement", "scaffold", "migrate", "refactor", "architecture",
               "full", "complete", "build sync", "create system", "design", "audit"}
_SIMPLE_KW  = {"fix", "add field", "rename", "update text", "delete", "remove",
               "typo", "change color", "update label", "correct"}


class BaseAgent:
    name:  str = "Base Agent"
    emoji: str = "🤖"
    color: str = "cyan"

    ALLOWED_TOOLS = [
        "Read", "Write", "Edit", "MultiEdit",
        "Glob", "Bash", "WebSearch"
    ]

    def __init__(self, cost_tracker: CostTracker = None):
        self.cost_tracker = cost_tracker or CostTracker()
        self.output_dir = get_code_dir()
        os.makedirs(self.output_dir, exist_ok=True)

    # ------------------------------------------------------------------
    # Subclasses implement this
    # ------------------------------------------------------------------

    def get_system_prompt(self) -> str:
        raise NotImplementedError

    # ------------------------------------------------------------------
    # Options — max_turns driven by task complexity
    # ------------------------------------------------------------------

    def get_options(self, max_turns: int = 30) -> ClaudeAgentOptions:
        return ClaudeAgentOptions(
            allowed_tools=self.ALLOWED_TOOLS,
            permission_mode="acceptEdits",
            system_prompt=self.get_system_prompt(),
            max_turns=max_turns,
        )

    def _estimate_turns(self, task: str) -> int:
        """Estimate appropriate max_turns from task description keywords."""
        t = task.lower()
        if any(k in t for k in _COMPLEX_KW):
            return 60
        if any(k in t for k in _SIMPLE_KW):
            return 15
        return 30

    # ------------------------------------------------------------------
    # Context building — what do agents know before they start?
    # ------------------------------------------------------------------

    def _build_context_prefix(self) -> str:
        """
        Builds the context block injected before every prompt:
        1. Learned memory (cross-session facts)
        2. Truncated session log (this session's activity)
        """
        from agents.session_memory import (
            read_learned_memory, read_session_log_truncated,
            ensure_learned_memory_exists
        )

        ensure_learned_memory_exists()
        parts = []

        # 1. Learned memory
        learned = read_learned_memory()
        if learned:
            parts.append(
                "## Kalıcı Bellek (Önceki Oturumlardan Öğrenilen)\n"
                "Bu bilgiler geçmiş oturumlarda keşfedildi — dikkate al:\n\n"
                f"{learned}"
            )

        # 2. Session log
        session = read_session_log_truncated()
        if session and session.strip():
            parts.append(
                "## Bu Oturumda Yapılanlar\n"
                "Bunları tekrar yapma — bunların üzerine inşa et:\n\n"
                f"{session}"
            )

        if not parts:
            return ""

        return "\n\n---\n" + "\n\n---\n".join(parts) + "\n---\n\n"

    def _learned_memory_instruction(self) -> str:
        """Standard suffix telling agents to write non-obvious discoveries."""
        return (
            f"\n\n---\n"
            f"**Kalıcı Bellek Talimatı:** Bu görev sırasında keşfettiğin ve "
            f"OBVIOUS OLMAYAN teknik bilgileri (framework quirks, config gotchas, "
            f"proje-spesifik kural) Write aracıyla şu dosyaya ekle:\n"
            f"{LEARNED_MEMORY_FILE}\n"
            f"Format: yeni satır → `- [Kategori] Kısa açıklama`\n"
            f"Örnek: `- [Backend] Background job'larda .IgnoreQueryFilters() şart`\n"
            f"Zorunlu değil — sadece gerçekten non-obvious şeyleri yaz."
        )

    # ------------------------------------------------------------------
    # Safety hook
    # ------------------------------------------------------------------

    def _safe_pre_tool(self, tool_name: str, tool_input: dict) -> bool:
        result = pre_tool_hook(tool_name, tool_input)
        if not result.get("allow", True):
            console.print(f"[red]{result.get('message', 'Blocked')}[/red]")
            return False
        return True

    # ------------------------------------------------------------------
    # Main run loop
    # ------------------------------------------------------------------

    async def run(
        self,
        prompt: str,
        working_dir: str = None,
        task_label: str = None,
        max_turns: int = None,
    ) -> str:
        work_dir = working_dir or self.output_dir
        os.makedirs(work_dir, exist_ok=True)

        # Estimate turns if not explicitly provided
        turns = max_turns or self._estimate_turns(task_label or prompt)

        # Build full prompt: context prefix + task + learned memory instruction
        ctx = self._build_context_prefix()
        full_prompt = (
            f"Working directory: {work_dir}\n"
            f"{ctx}"
            f"{prompt}"
            f"{self._learned_memory_instruction()}"
        )

        display = prompt[:120] + "..." if len(prompt) > 120 else prompt
        console.print(Panel(
            f"[bold {self.color}]{self.emoji} {self.name}[/bold {self.color}] "
            f"[dim](max_turns={turns})[/dim]\n"
            f"[dim]{display}[/dim]",
            border_style=self.color
        ))

        result_text = ""
        input_tokens  = 0
        output_tokens = 0

        try:
            options = self.get_options(max_turns=turns)

            async for message in query(prompt=full_prompt, options=options):

                if hasattr(message, "content"):
                    for block in message.content:
                        if hasattr(block, "text") and block.text:
                            console.print(block.text, end="", markup=False)
                            result_text += block.text

                elif hasattr(message, "tool_use"):
                    tool = message.tool_use
                    inp  = getattr(tool, "input", {})
                    if not self._safe_pre_tool(tool.name, inp or {}):
                        continue
                    if isinstance(inp, dict):
                        if "command" in inp:
                            console.print(f"\n[dim cyan]🔧 {tool.name}: $ {inp['command'][:80]}[/dim cyan]")
                        elif "path" in inp:
                            console.print(f"\n[dim cyan]📄 {tool.name}: {inp['path']}[/dim cyan]")
                        else:
                            console.print(f"\n[dim cyan]🔧 {tool.name}[/dim cyan]")

                elif hasattr(message, "usage"):
                    usage = message.usage
                    input_tokens  += getattr(usage, "input_tokens",  0)
                    output_tokens += getattr(usage, "output_tokens", 0)

                elif hasattr(message, "result"):
                    console.print(f"\n[bold green]✅ {self.name} tamamlandı[/bold green]\n")

        except Exception as e:
            console.print(f"\n[bold red]❌ {self.name} hata: {e}[/bold red]\n")
            raise

        finally:
            self.cost_tracker.record(self.name, input_tokens, output_tokens)

        # Write completion summary to session log
        from agents.session_memory import append_to_session_log
        label   = task_label or prompt[:150]
        summary = result_text.strip()[:600] or "Tamamlandı (çıktı yok)"
        append_to_session_log(self.name, label, summary)

        return result_text

    def run_sync(self, prompt: str, working_dir: str = None) -> str:
        return asyncio.run(self.run(prompt, working_dir))
