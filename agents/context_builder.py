"""
Context Builder
===============
Builds a concise snapshot of the current project state (git, build status).
Uses project_config to find paths — no hardcoded filenames.
"""

import os
import subprocess

from agents.project_config import get_code_dir, find_solution_file, find_frontend_dir

WORKSPACE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def build_project_snapshot() -> str:
    """Return a markdown snapshot of current project state."""
    code_dir = get_code_dir()
    parts    = []

    # --- Git: last 5 commits ---
    git_log = _run(["git", "log", "--oneline", "-5"], WORKSPACE)
    if git_log:
        parts.append(f"**Son 5 commit:**\n```\n{git_log}\n```")

    # --- Git: uncommitted changes ---
    git_diff = _run(["git", "diff", "--stat", "HEAD"], WORKSPACE)
    if git_diff.strip():
        parts.append(f"**Uncommitted değişiklikler:**\n```\n{git_diff.strip()}\n```")

    # --- Backend build status ---
    sln = find_solution_file()
    if sln:
        out = _run(
            ["dotnet", "build", os.path.basename(sln), "--no-restore", "-v", "quiet"],
            code_dir
        )
        succeeded = "Build succeeded" in out or "Oluşturma başarılı oldu" in out
        status    = "✅ Başarılı" if succeeded else "❌ BAŞARISIZ"
        summary   = "\n".join(out.strip().splitlines()[-3:])
        parts.append(f"**Backend build ({os.path.basename(sln)}):** {status}\n```\n{summary}\n```")

    # --- Frontend status ---
    frontend = find_frontend_dir()
    if frontend:
        nm = os.path.join(frontend, "node_modules")
        if not os.path.exists(nm):
            parts.append(f"**Frontend ({os.path.relpath(frontend, code_dir)}):** ⚠️  node_modules yok — `npm install` gerekli")

    if not parts:
        return ""

    return "## Proje Durumu (Snapshot)\n\n" + "\n\n".join(parts) + "\n"


def run_build_check(agent_type: str) -> tuple[bool, str]:
    """
    Quick build/compile check after a task completes.
    Returns (success, error_output).
    """
    code_dir = get_code_dir()

    if agent_type in ("backend", "architect"):
        sln = find_solution_file()
        if not sln:
            return True, ""
        out = _run(
            ["dotnet", "build", os.path.basename(sln), "--no-restore", "-v", "quiet"],
            code_dir
        )
        if "Build succeeded" in out or "Oluşturma başarılı oldu" in out:
            return True, ""
        errors = [l for l in out.splitlines() if " error " in l.lower()]
        return False, "\n".join(errors[:20]) or out[-800:]

    if agent_type == "frontend":
        frontend = find_frontend_dir()
        if not frontend:
            return True, ""
        out = _run(["npm", "run", "build"], frontend)
        if "error TS" in out or "Build failed" in out:
            errors = [l for l in out.splitlines() if "error" in l.lower()]
            return False, "\n".join(errors[:20]) or out[-800:]
        return True, ""

    return True, ""


def _run(cmd: list[str], cwd: str, timeout: int = 60) -> str:
    try:
        r = subprocess.run(
            cmd, capture_output=True, text=True, cwd=cwd, timeout=timeout
        )
        return (r.stdout + r.stderr).strip()
    except Exception:
        return ""
