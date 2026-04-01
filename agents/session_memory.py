"""
Session Memory
==============
Two layers of agent memory:

1. SESSION LOG (.session_log.md)
   - Cleared at the start of each orchestration run
   - Each agent appends a summary of what it did
   - Injected into every subsequent agent's context (truncated to last 4 entries)
   - Prevents duplicate work, gives inter-agent awareness

2. LEARNED MEMORY (.learned.md)
   - Persists across all sessions — never cleared automatically
   - Agents write non-obvious technical discoveries here
   - Injected at the top of every agent's context
   - Grows over time; cap displayed entries to avoid bloat
"""

import os
import json
from datetime import datetime

WORKSPACE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AGENTS_DIR = os.path.join(WORKSPACE, "agents")

SESSION_LOG       = os.path.join(AGENTS_DIR, ".session_log.md")
TASK_PLAN_FILE    = os.path.join(AGENTS_DIR, ".task_plan.json")
LEARNED_MEMORY    = os.path.join(AGENTS_DIR, ".learned.md")

# How many session log entries to show each agent (keeps context lean)
SESSION_LOG_MAX_ENTRIES = 4

# How many lines of learned memory to show (prevents bloat over time)
LEARNED_MEMORY_MAX_LINES = 40


# ---------------------------------------------------------------------------
# Session log (per-run, cleared each time)
# ---------------------------------------------------------------------------

def clear_session_log():
    """Start a fresh session log at the beginning of each orchestration run."""
    os.makedirs(AGENTS_DIR, exist_ok=True)
    with open(SESSION_LOG, "w", encoding="utf-8") as f:
        f.write(
            f"# Session Log — {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n\n"
            "Ajanlar bu dosyayı okuyarak birbirlerinin çalışmalarından haberdar olur.\n\n---\n"
        )


def append_to_session_log(agent_name: str, task: str, result_summary: str):
    """Append an agent's completed work entry."""
    os.makedirs(AGENTS_DIR, exist_ok=True)
    ts = datetime.now().strftime("%H:%M:%S")
    entry = (
        f"\n## [{ts}] {agent_name}\n"
        f"**Görev:** {task.strip()[:200]}\n\n"
        f"**Sonuç:** {result_summary.strip()[:600]}\n\n"
        f"---\n"
    )
    with open(SESSION_LOG, "a", encoding="utf-8") as f:
        f.write(entry)


def read_session_log_truncated() -> str:
    """
    Return only the last SESSION_LOG_MAX_ENTRIES entries.
    Keeps the injected context lean even in long sessions.
    """
    try:
        with open(SESSION_LOG, encoding="utf-8") as f:
            content = f.read()
    except FileNotFoundError:
        return ""

    # Split on the separator; first chunk is the header
    parts = content.split("\n---\n")
    if len(parts) <= 2:
        return content  # short enough to show entirely

    header = parts[0] + "\n---\n"
    recent = parts[-SESSION_LOG_MAX_ENTRIES:]
    return header + "\n---\n".join(recent)


# ---------------------------------------------------------------------------
# Learned memory (cross-session, never cleared)
# ---------------------------------------------------------------------------

def read_learned_memory() -> str:
    """Return the learned memory, capped at LEARNED_MEMORY_MAX_LINES lines."""
    try:
        with open(LEARNED_MEMORY, encoding="utf-8") as f:
            lines = f.readlines()
    except FileNotFoundError:
        return ""

    if not lines:
        return ""

    capped = lines[-LEARNED_MEMORY_MAX_LINES:]
    return "".join(capped).strip()


def ensure_learned_memory_exists():
    """Create .learned.md with a header if it doesn't exist yet."""
    if os.path.exists(LEARNED_MEMORY):
        return
    os.makedirs(AGENTS_DIR, exist_ok=True)
    with open(LEARNED_MEMORY, "w", encoding="utf-8") as f:
        f.write(
            "# Learned Memory\n\n"
            "Ajanlar bu dosyaya proje boyunca öğrendikleri non-obvious teknik bilgileri yazar.\n"
            "Her oturumda otomatik okunur ve ajanlara enjekte edilir.\n\n"
            "Format: `- [Kategori] Öğrenilen şey`\n\n"
        )


# ---------------------------------------------------------------------------
# Task plan (written by orchestrator LLM, read by Python dispatcher)
# ---------------------------------------------------------------------------

def write_task_plan(plan: dict):
    os.makedirs(AGENTS_DIR, exist_ok=True)
    with open(TASK_PLAN_FILE, "w", encoding="utf-8") as f:
        json.dump(plan, f, ensure_ascii=False, indent=2)


def read_task_plan() -> dict | None:
    try:
        with open(TASK_PLAN_FILE, encoding="utf-8") as f:
            return json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        return None
