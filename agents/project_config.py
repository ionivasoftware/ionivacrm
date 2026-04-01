"""
Project Config
==============
Single source of truth for project-specific paths.
Everything is derived from CLAUDE.md — agents never hardcode paths.

How it works:
- Reads "**Kod dizini:**" from CLAUDE.md to find the code root
- Scans code root for .sln files (backend) and package.json (frontend)
- All agents import from here instead of hardcoding paths

To use this system in a new project:
1. Copy .claude/ and agents/ to the new project root
2. Update .claude/projectFiles/ with the new project's details
3. Set "**Kod dizini:** `/path/to/code/`" in CLAUDE.md
4. Run: python run.py
"""

import os
import re
import glob
from functools import lru_cache

WORKSPACE  = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CLAUDE_MD  = os.path.join(WORKSPACE, ".claude", "CLAUDE.md")


@lru_cache(maxsize=1)
def get_code_dir() -> str:
    """
    Parse **Kod dizini:** from CLAUDE.md.
    Falls back to WORKSPACE/output or WORKSPACE if CLAUDE.md is missing.
    """
    try:
        with open(CLAUDE_MD, encoding="utf-8") as f:
            content = f.read()
        match = re.search(r'\*\*Kod dizini:\*\*\s*[`"]([^`"]+)[`"]', content)
        if match:
            path = match.group(1).rstrip("/").rstrip("\\")
            if os.path.isabs(path) and os.path.exists(path):
                return path
    except Exception:
        pass

    # Fallback hierarchy
    fallback = os.path.join(WORKSPACE, "output")
    return fallback if os.path.exists(fallback) else WORKSPACE


@lru_cache(maxsize=1)
def find_solution_file() -> str | None:
    """Find the first .sln file in the code directory. Returns None if not a .NET project."""
    code_dir = get_code_dir()
    slns = glob.glob(os.path.join(code_dir, "*.sln"))
    return slns[0] if slns else None


@lru_cache(maxsize=1)
def find_frontend_dir() -> str | None:
    """
    Find the frontend directory (a dir with package.json).
    Checks common names first, then scans one level deep.
    Returns None if no frontend found.
    """
    code_dir = get_code_dir()

    # Check common frontend folder names
    for name in ("frontend", "client", "web", "app", "ui"):
        candidate = os.path.join(code_dir, name)
        if os.path.exists(os.path.join(candidate, "package.json")):
            return candidate

    # Scan one level deep
    try:
        for entry in os.scandir(code_dir):
            if entry.is_dir() and os.path.exists(os.path.join(entry.path, "package.json")):
                return entry.path
    except Exception:
        pass

    # Maybe the code root itself is a frontend project
    if os.path.exists(os.path.join(code_dir, "package.json")):
        return code_dir

    return None


def is_dotnet_project() -> bool:
    return find_solution_file() is not None


def is_frontend_project() -> bool:
    return find_frontend_dir() is not None


def summary() -> str:
    """Human-readable summary of detected project layout."""
    code_dir = get_code_dir()
    sln      = find_solution_file()
    frontend = find_frontend_dir()
    lines = [
        f"Kod dizini : {code_dir}",
        f"Backend    : {os.path.basename(sln) if sln else '(yok)'}",
        f"Frontend   : {os.path.relpath(frontend, code_dir) if frontend else '(yok)'}",
    ]
    return "\n".join(lines)
