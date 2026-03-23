"""
QA Agent
=========
Writes and runs unit + integration tests.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from agents.base_agent import BaseAgent

WORKSPACE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUTPUT_DIR = os.path.join(WORKSPACE, "output")

SYSTEM_PROMPT = """
You are a Senior QA Engineer for ION CRM.

Your responsibilities:
- Write xUnit tests for all .NET backend code
- Write React Testing Library tests for frontend
- Run tests and fix failures
- Ensure 80%+ code coverage on business logic
- Test multi-tenant isolation (CRITICAL)
- Test auth edge cases
- Test sync service logic

Test Priorities:
1. MULTI-TENANT ISOLATION — most critical
   - User from Project A cannot see Project B data
   - SuperAdmin can see everything
   - Queries always include tenant filter

2. AUTH TESTS
   - Login success/failure
   - Token expiry
   - Refresh token rotation
   - Role-based access (401 vs 403)
   - Project-based access

3. CUSTOMER CRUD
   - Create, read, update, delete
   - Validation errors
   - Soft delete (IsDeleted flag)

4. SYNC SERVICE
   - Incoming data mapped correctly
   - Duplicate handling
   - Retry logic
   - Outbound callbacks triggered

5. MIGRATION SERVICE
   - Idempotent (safe to run twice)
   - Data mapped correctly
   - Errors handled gracefully

Test Patterns:
- Arrange-Act-Assert strictly
- Use InMemory database for unit tests
- Use Moq for all dependencies
- FluentAssertions for readable assertions
- TestServer for integration tests
- Never test framework code — test YOUR code

Always run: dotnet test after writing tests.
Fix all failures before reporting done.
"""


class QAAgent(BaseAgent):
    name = "QA Engineer"
    emoji = "🧪"
    color = "cyan"
    ALLOWED_TOOLS = ["Read", "Write", "Edit", "Glob", "Bash"]

    def get_system_prompt(self) -> str:
        return SYSTEM_PROMPT

    async def run_tests(self) -> str:
        prompt = f"""
        Read CLAUDE.md at {WORKSPACE}/CLAUDE.md
        Read all source files in {OUTPUT_DIR}/src/
        Read existing tests in {OUTPUT_DIR}/tests/

        Write comprehensive tests for ION CRM focusing on:

        1. Multi-tenant isolation tests (MOST IMPORTANT):
           - Customer queries filtered by projectId
           - SuperAdmin bypass works
           - Cross-tenant data access returns 403

        2. Auth service tests:
           - LoginCommand: valid credentials, wrong password, user not found
           - Token generation: claims correct, expiry correct
           - Refresh token: valid rotation, expired token rejected

        3. Customer CRUD tests:
           - Create with all required fields
           - Create missing required fields → validation error
           - Update own project's customer → success
           - Update other project's customer → 403
           - Soft delete: IsDeleted=true, not returned in queries

        4. Sync service tests:
           - Incoming customer data mapped to entity correctly
           - Duplicate customer not created (upsert)
           - SyncLog created for each sync
           - Failed sync → retry 3 times

        Run all tests:
        cd {OUTPUT_DIR}
        dotnet test --logger "console;verbosity=detailed"

        Fix all failing tests.
        Report: X tests passed, Y failed, Z skipped.
        Coverage report if possible.
        """
        return await self.run(prompt, OUTPUT_DIR)
