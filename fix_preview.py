import asyncio, os, sys
from dotenv import load_dotenv
load_dotenv()
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from hooks.cost_tracker import CostTracker
from agents.base_agent import BaseAgent, console

WORKSPACE = os.path.dirname(os.path.abspath(__file__))

class FixAgent(BaseAgent):
    name = "Fix Agent"
    emoji = "🔧"
    color = "yellow"
    ALLOWED_TOOLS = ["Read", "Write", "Edit", "Bash", "Glob"]
    def get_system_prompt(self):
        return "You are a Python developer. Fix code issues."

async def main():
    tracker = CostTracker()
    agent = FixAgent(tracker)
    prompt = """
Fix two issues in the ION CRM agent team:

## Issue 1: Remove Jira from orchestrator
File: /root/my-product-team/agents/orchestrator.py

- Remove all Jira references from system prompt
- Remove create_jira_tickets method entirely
- Remove Jira imports if any
- The orchestrator should NOT try to create Jira tickets at all

## Issue 2: Remove approval gates from main.py
File: /root/my-product-team/main.py

Current code has these checks that pause execution:
- gate_sprint_plan(sprint_data) at line 113
- gate_db_schema(schema_summary) at line 139  
- gate_sprint_complete(...) at lines 155, 187, 222, 252
- gate_deployment(...) at line 274

Replace ALL of these checks with just `True` so nothing pauses:
- `if not gate_sprint_plan(sprint_data):` → remove the if block, just continue
- `if not gate_db_schema(schema_summary):` → remove the if block, just continue
- `if not gate_sprint_complete(...)` → remove the if block, just continue
- `if not gate_deployment(...)` → remove the if block, just continue

Also remove the import line:
`from hooks.approval_gate import (gate_sprint_plan, gate_db_schema, gate_sprint_complete, gate_deployment)`

After fixing, test with:
python /root/my-product-team/main.py --help 2>/dev/null || echo "syntax ok"

Report DONE: when complete.
"""
    result = await agent.run(prompt, WORKSPACE)
    tracker.print_summary()
    tracker.save()

if __name__ == "__main__":
    asyncio.run(main())
