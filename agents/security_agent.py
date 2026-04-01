"""
Security Agent
==============
Audits code for vulnerabilities and produces a security report.
Reads CLAUDE.md for project context before auditing.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from agents.base_agent import BaseAgent
from agents.project_config import get_code_dir, find_frontend_dir

WORKSPACE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

SYSTEM_PROMPT = """
You are a Senior Security Engineer.

Your role: audit code for vulnerabilities, report findings, fix only CRITICAL issues.
For non-critical issues: document in the security report only.

How you work:
1. Read CLAUDE.md first — understand the project stack, auth system, and architecture
2. Read todo.md for specific security tasks
3. Read the actual source code — do not assume, verify
4. Fix CRITICAL issues directly in the code
5. Write a security report for everything else

Security checklist (adapt to the project's actual stack):

AUTHENTICATION & AUTHORIZATION
□ Secrets not hardcoded — come from env vars only
□ Token expiry is appropriately short
□ Passwords hashed with strong algorithm (bcrypt cost >= 12 or equivalent)
□ Rate limiting on auth endpoints
□ Authorization checks on every protected endpoint
□ Tenant/scope isolation enforced (users can't access other tenants' data)

DATA SECURITY
□ No secrets in committed code or config files
□ Connection strings from environment variables
□ No passwords or tokens in logs
□ Sensitive data not exposed in API responses unnecessarily

INPUT VALIDATION
□ Validation on all user inputs
□ SQL injection not possible (parameterized queries / ORM)
□ XSS prevention (output encoding)
□ Request size limits configured

INFRASTRUCTURE
□ HTTPS enforced
□ CORS restricted to known origins
□ Debug/dev tools disabled in production
□ Error messages don't leak stack traces or internal details

DEPENDENCIES
□ Known vulnerabilities in dependencies
□ Outdated packages with security patches available

Output format:
CRITICAL (fix now): data breach risk
HIGH (fix this sprint): serious vulnerability
MEDIUM (next sprint): significant weakness
LOW (backlog): improvement

Fix CRITICAL issues directly. Write full report to docs/security_report.md
Read CLAUDE.md first to understand the specific security model of this project.
"""


class SecurityAgent(BaseAgent):
    name = "Security Auditor"
    emoji = "🔒"
    color = "red"
    ALLOWED_TOOLS = ["Read", "Write", "Edit", "Glob", "Bash"]

    def get_system_prompt(self) -> str:
        return SYSTEM_PROMPT

    async def run_task(self, task: str) -> str:
        """Execute a specific security task."""
        code_dir  = get_code_dir()
        claude_md = os.path.join(WORKSPACE, ".claude", "CLAUDE.md")

        prompt = f"""
        Read this file first:
        1. {claude_md} — project security model and architecture

        Complete the following security task:
        {task}

        Fix CRITICAL issues directly in code.
        Document all findings in {code_dir}/docs/security_report.md
        """
        return await self.run(prompt, code_dir, task_label=task)

    async def audit(self) -> str:
        """Perform a full security audit of the project."""
        code_dir     = get_code_dir()
        frontend_dir = find_frontend_dir()
        claude_md    = os.path.join(WORKSPACE, ".claude", "CLAUDE.md")
        todo_md      = os.path.join(WORKSPACE, ".claude", "projectFiles", "todo.md")

        scan_paths = [f"{code_dir}/src/"]
        if frontend_dir:
            scan_paths.append(f"{frontend_dir}/src/")

        prompt = f"""
        Read these files first:
        1. {claude_md} — project stack, auth system, and architecture
        2. {todo_md} — any specific security tasks

        Perform a complete security audit.

        Read all source files in: {", ".join(scan_paths)}

        Check every item in your security checklist.
        Fix any CRITICAL issues directly.
        Write the full report to {code_dir}/docs/security_report.md

        End with: X critical, Y high, Z medium, W low issues found.
        """
        return await self.run(prompt, code_dir)

    async def run_todo(self) -> str:
        """Read todo.md and execute all security tasks."""
        code_dir  = get_code_dir()
        claude_md = os.path.join(WORKSPACE, ".claude", "CLAUDE.md")
        todo_md   = os.path.join(WORKSPACE, ".claude", "projectFiles", "todo.md")

        prompt = f"""
        Read these files first:
        1. {claude_md} — project security model and architecture
        2. {todo_md} — pending tasks

        Find all security tasks and complete them.
        Fix CRITICAL issues directly. Document others in the security report.
        """
        return await self.run(prompt, code_dir)
