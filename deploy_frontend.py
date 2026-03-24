"""Frontend Deploy Script"""
import asyncio, os, sys, getpass
from dotenv import load_dotenv
load_dotenv()
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from hooks.cost_tracker import CostTracker
from agents.base_agent import BaseAgent, console
from rich.panel import Panel

WORKSPACE = os.path.dirname(os.path.abspath(__file__))
OUTPUT_DIR = os.path.join(WORKSPACE, "output")

SYSTEM_PROMPT = f"""
You are a DevOps engineer. Deploy ONLY the React frontend to Railway.
Backend is already live at: https://ion-crm-api-production.up.railway.app

Use Railway GraphQL API (NOT CLI) with the RAILWAY_TOKEN env var.
API endpoint: https://backboard.railway.app/graphql/v2

Steps:
1. Get project ID:
   curl -s -X POST https://backboard.railway.app/graphql/v2 \
     -H "Authorization: Bearer $RAILWAY_TOKEN" \
     -H "Content-Type: application/json" \
     -d '{{"query":"{{ projects {{ edges {{ node {{ id name }} }} }} }}"}}'

2. Create frontend service in the project via GraphQL API

3. Create nixpacks.toml in {OUTPUT_DIR}/frontend:
[phases.setup]
nixPkgs = ["nodejs_20"]
[phases.install]
cmds = ["npm ci"]
[phases.build]
cmds = ["npm run build"]
[start]
cmd = "npx serve -s dist -l ${{PORT:-3000}}"

4. Set VITE_API_URL env var on the service via GraphQL API

5. Deploy using railway CLI with session token from ~/.railway/config.json:
   Use: RAILWAY_TOKEN=$(cat ~/.railway/config.json | python3 -c "import json,sys; print(json.load(sys.stdin).get('token',''))") railway up --service ion-crm-frontend

6. Generate domain via GraphQL API

7. Test with curl

Output DEPLOYMENT_SUCCESS:URL when done.
"""

class FrontendDeployAgent(BaseAgent):
    name = "Frontend DevOps"
    emoji = "🎨"
    color = "magenta"
    ALLOWED_TOOLS = ["Read", "Write", "Edit", "Glob", "Bash"]

    def get_system_prompt(self):
        return SYSTEM_PROMPT

async def main():
    console.print(Panel(
        "[bold magenta]🎨 Frontend Deploy Agent[/bold magenta]",
        border_style="magenta"
    ))

    tracker = CostTracker()
    agent = FrontendDeployAgent(tracker)

    try:
        result = await agent.run(
            "Deploy the React frontend to Railway now. Follow all steps.",
            OUTPUT_DIR
        )
        if "DEPLOYMENT_SUCCESS" in result:
            url = result.split("DEPLOYMENT_SUCCESS:")[1].strip().split()[0]
            console.print(f"\n[bold green]🎉 Frontend canlıda: {url}[/bold green]")
    except KeyboardInterrupt:
        console.print("\n[yellow]İptal[/yellow]")
    finally:
        tracker.print_summary()
        tracker.save()

if __name__ == "__main__":
    asyncio.run(main())
