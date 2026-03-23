"""
Security Agent
==============
Audits code for vulnerabilities, checks secrets management,
validates auth implementation, produces security report.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from agents.base_agent import BaseAgent

WORKSPACE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUTPUT_DIR = os.path.join(WORKSPACE, "output")

SYSTEM_PROMPT = """
You are a Senior Security Engineer auditing ION CRM.

You READ code and REPORT issues — you fix ONLY critical vulnerabilities.
For non-critical issues, you create Jira tickets.

Security Checklist for ION CRM:

AUTHENTICATION & AUTHORIZATION
□ JWT secret not hardcoded (comes from env var)
□ JWT expiry is short (15 min access, 7 day refresh)
□ Refresh tokens stored securely (hashed in DB)
□ Passwords hashed with bcrypt (cost >= 12)
□ Rate limiting on /auth/login (prevent brute force)
□ Account lockout after N failed attempts
□ Multi-tenant isolation enforced on ALL endpoints
□ SuperAdmin routes protected with [Authorize(Roles="SuperAdmin")]

DATA SECURITY
□ No secrets in code or config files
□ Connection strings from environment variables only
□ No passwords/tokens in logs
□ Sensitive data not in JWT payload (no passwords)
□ Soft delete (IsDeleted) not exposing data

INPUT VALIDATION
□ FluentValidation on ALL commands
□ SQL injection not possible (EF Core parameterized)
□ XSS prevention (output encoding)
□ File upload restrictions (if applicable)
□ Request size limits configured

INFRASTRUCTURE
□ HTTPS enforced (HSTS)
□ CORS locked to specific origins
□ Swagger disabled in production
□ Error messages don't leak stack traces
□ Database user has minimum permissions
□ No direct DB access from frontend

SYNC SERVICE
□ SaaS API keys stored in env vars
□ Webhook endpoints validate request signatures
□ Outbound callbacks use HTTPS only
□ Sync logs don't contain sensitive payload data

DEPENDENCIES
□ NuGet packages up to date
□ No known CVEs in dependencies
□ dotnet-outdated check

Output Format:
CRITICAL (fix now): issues that could lead to data breach
HIGH (fix this sprint): serious vulnerabilities  
MEDIUM (next sprint): significant weaknesses
LOW (backlog): improvements
INFO: recommendations

For CRITICAL issues: fix them directly in the code.
For others: document in {output_dir}/docs/security_report.md
"""


class SecurityAgent(BaseAgent):
    name = "Security Auditor"
    emoji = "🔒"
    color = "red"
    # Read-only mostly — only writes security report and fixes critical issues
    ALLOWED_TOOLS = ["Read", "Write", "Edit", "Glob", "Bash"]

    def get_system_prompt(self) -> str:
        return SYSTEM_PROMPT.replace("{output_dir}", OUTPUT_DIR)

    async def audit(self) -> str:
        prompt = f"""
        Read CLAUDE.md at {WORKSPACE}/CLAUDE.md
        
        Perform a complete security audit of ION CRM.
        
        Read ALL files in:
        - {OUTPUT_DIR}/src/
        - {OUTPUT_DIR}/frontend/src/
        - {OUTPUT_DIR}/.github/
        
        Check every item in your security checklist.
        
        Specific checks for ION CRM:
        
        1. MULTI-TENANT ISOLATION (CRITICAL)
           - Check every controller endpoint
           - Verify ProjectId filter applied everywhere
           - Check for any endpoints that could leak cross-tenant data
        
        2. AUTH IMPLEMENTATION
           - Check JWT configuration
           - Verify refresh token rotation
           - Check bcrypt cost factor
        
        3. SYNC SERVICE SECURITY  
           - Check SaaS API key handling
           - Verify incoming webhook validation
           - Check that sync logs don't contain sensitive data
        
        4. SECRETS SCAN
           Run: grep -r "password" {OUTPUT_DIR}/src --include="*.cs" -i
           Run: grep -r "secret" {OUTPUT_DIR}/src --include="*.cs" -i
           Run: grep -r "apikey" {OUTPUT_DIR}/src --include="*.cs" -i
           Look for hardcoded values — report any found
        
        5. DEPENDENCY AUDIT
           cd {OUTPUT_DIR}
           dotnet list package --vulnerable 2>/dev/null || true
        
        Fix any CRITICAL issues directly.
        Write full report to {OUTPUT_DIR}/docs/security_report.md
        
        End with a summary: X critical, Y high, Z medium, W low issues found.
        """
        return await self.run(prompt, OUTPUT_DIR)
