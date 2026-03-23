"""
Pre-Tool Safety Hook
====================
Runs BEFORE every agent tool call.
Blocks dangerous commands that could harm production or leak secrets.
"""

import re
import os
from datetime import datetime

# ── Blocked patterns ───────────────────────────────────────────
BLOCKED_BASH = [
    r"DROP\s+TABLE",
    r"DROP\s+DATABASE",
    r"DELETE\s+FROM\s+\w+\s*;",        # DELETE without WHERE
    r"TRUNCATE\s+TABLE",
    r"rm\s+-rf\s+/",
    r"rm\s+-rf\s+~",
    r">\s*/etc/",
    r"chmod\s+777",
    r"curl.*\|\s*bash",                 # curl pipe to bash
    r"wget.*\|\s*bash",
    r"ssh\s+\w+@(?!localhost)",         # SSH to remote (not localhost)
    r"scp\s+.*@(?!localhost)",
    r"cat\s+.*\.env",                   # reading .env files
    r"echo.*ANTHROPIC_API_KEY",
    r"printenv.*KEY",
    r"printenv.*SECRET",
    r"printenv.*PASSWORD",
    r"export.*=.*sk-ant",               # leaking Anthropic key
    r"git\s+push.*--force.*main",       # force push to main
    r"git\s+push.*-f.*main",
]

BLOCKED_FILE_PATHS = [
    "/etc/passwd",
    "/etc/shadow",
    "/etc/ssh",
    "~/.ssh",
    "/root/.ssh",
    ".env.production",
    ".env.prod",
]

ALLOWED_DELETE_PATTERNS = [
    r"rm\s+.*\.pyc",         # Python cache files OK
    r"rm\s+.*__pycache__",   # Python cache OK
    r"rm\s+.*\.log",         # Log files OK
    r"rm\s+.*node_modules",  # node_modules OK
    r"rm\s+.*\.tmp",         # Temp files OK
]

LOG_FILE = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "logs", "tool_calls.log"
)


def log_tool_call(tool_name: str, input_data: dict, blocked: bool, reason: str = ""):
    """Log every tool call for audit trail."""
    os.makedirs(os.path.dirname(LOG_FILE), exist_ok=True)
    timestamp = datetime.now().isoformat()
    status = "BLOCKED" if blocked else "ALLOWED"
    with open(LOG_FILE, "a") as f:
        f.write(f"[{timestamp}] {status} | {tool_name} | {reason or 'OK'}\n")
        if blocked:
            f.write(f"  Input: {str(input_data)[:200]}\n")


def check_bash_command(command: str) -> tuple[bool, str]:
    """Returns (is_blocked, reason)"""

    # Check if it's an allowed delete pattern first
    for allowed in ALLOWED_DELETE_PATTERNS:
        if re.search(allowed, command, re.IGNORECASE):
            return False, ""

    # Check blocked patterns
    for pattern in BLOCKED_BASH:
        if re.search(pattern, command, re.IGNORECASE):
            return True, f"Matched blocked pattern: {pattern}"

    return False, ""


def check_file_path(path: str) -> tuple[bool, str]:
    """Returns (is_blocked, reason)"""
    for blocked_path in BLOCKED_FILE_PATHS:
        if blocked_path in path:
            return True, f"Blocked file path: {blocked_path}"
    return False, ""


def pre_tool_hook(tool_name: str, tool_input: dict) -> dict:
    """
    Main hook function called before every tool use.
    Returns dict with 'allow' bool and optional 'message'.
    """
    blocked = False
    reason = ""

    try:
        if tool_name == "Bash":
            command = tool_input.get("command", "")
            blocked, reason = check_bash_command(command)

        elif tool_name in ("Read", "Write", "Edit", "MultiEdit"):
            path = tool_input.get("path", "") or tool_input.get("file_path", "")
            blocked, reason = check_file_path(path)

        elif tool_name == "Write":
            # Also check content for secret leakage
            content = tool_input.get("content", "")
            secret_patterns = [
                r"sk-ant-api",
                r"ghp_[A-Za-z0-9]{36}",
                r"github_pat_",
                r"AKIA[0-9A-Z]{16}",  # AWS key
            ]
            for pattern in secret_patterns:
                if re.search(pattern, content):
                    blocked = True
                    reason = "Content contains what appears to be a secret/API key"
                    break

    except Exception as e:
        # Never block on hook errors — just log
        log_tool_call(tool_name, tool_input, False, f"Hook error (allowed): {e}")
        return {"allow": True}

    log_tool_call(tool_name, tool_input, blocked, reason)

    if blocked:
        return {
            "allow": False,
            "message": f"🚫 BLOCKED by safety hook: {reason}\n"
                       f"Tool: {tool_name}\n"
                       f"If this is intentional, modify hooks/pre_tool_safety.py"
        }

    return {"allow": True}
