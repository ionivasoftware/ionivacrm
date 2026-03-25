"""
ION CRM — Deploy Script
========================
Çalıştır: python deploy.py
DevOps ajanı her şeyi halleder:
- Hangfire'ı devre dışı bırakır
- Connection string düzeltir
- git push yapar
- GitHub Actions izler
- Hataları kendi düzeltir
"""

import asyncio
import os
import sys
import getpass
from dotenv import load_dotenv

load_dotenv()
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from hooks.cost_tracker import CostTracker
from agents.base_agent import BaseAgent, console
from rich.panel import Panel

WORKSPACE = os.path.dirname(os.path.abspath(__file__))
OUTPUT_DIR = os.path.join(WORKSPACE, "output")

SYSTEM_PROMPT = f"""
You are a Senior DevOps Engineer deploying ION CRM to Railway via GitHub Actions.

Working directory: {OUTPUT_DIR}
Workspace: {WORKSPACE}

YOUR TASKS IN ORDER:

1. FIX appsettings.json — add Hangfire disabled:
   Read: {OUTPUT_DIR}/src/IonCrm.API/appsettings.json
   Add "Hangfire": {{"Enabled": false}} to the JSON

2. FIX DependencyInjection.cs — make Hangfire optional:
   Read: {OUTPUT_DIR}/src/IonCrm.Infrastructure/DependencyInjection.cs
   Wrap ALL Hangfire code with:
   var enableHangfire = configuration.GetValue<bool>("Hangfire:Enabled", false);
   if (enableHangfire)
   {{
       // ALL hangfire code here
   }}

3. VERIFY build:
   cd {OUTPUT_DIR}
   dotnet build 2>&1 | tail -5

4. RUN migration against Neon DB using NEON_CONNECTION_DOTNET credential

5. COMMIT and PUSH:
   cd {WORKSPACE}
   git config user.email "deploy@ioncrm.com" 2>/dev/null || true
   git config user.name "ION CRM Deploy" 2>/dev/null || true
   git add -A
   git commit -m "fix: disable Hangfire, fix deployment config"
   git push origin main

6. MONITOR GitHub Actions:
   gh run watch --repo omercakmakci/ionivacrm

7. VERIFY deployment:
   curl -s https://ion-crm-api-production.up.railway.app/health

Fix every error. Never give up.
Output DEPLOYMENT_SUCCESS:URL when done.
If you need credentials output: NEED_CREDENTIAL:KEY:description
"""


class DeployAgent(BaseAgent):
    name = "DevOps Engineer"
    emoji = "🚀"
    color = "bright_blue"
    ALLOWED_TOOLS = [
        "Read", "Write", "Edit", "MultiEdit",
        "Glob", "Bash"
    ]

    def __init__(self, credentials: dict, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.credentials = credentials

    def get_system_prompt(self) -> str:
        return SYSTEM_PROMPT

    def _build_prompt(self) -> str:
        creds = "\n".join(f"{k}={v}" for k, v in self.credentials.items())
        return f"""
Available credentials:
{creds}

Start deploying now. Follow all 7 steps. Fix any error before moving on.
"""

    async def deploy(self) -> str:
        result = await self.run(self._build_prompt(), OUTPUT_DIR)

        # Handle credential requests
        if "NEED_CREDENTIAL:" in result:
            for line in result.split("\n"):
                if "NEED_CREDENTIAL:" in line:
                    parts = line.split("NEED_CREDENTIAL:")[1].split(":")
                    key = parts[0].strip()
                    desc = parts[1].strip() if len(parts) > 1 else key
                    if key not in self.credentials:
                        value = getpass.getpass(f"🔑 {desc}: ")
                        self.credentials[key] = value
            result = await self.run(self._build_prompt(), OUTPUT_DIR)

        return result


async def main():
    console.print(Panel(
        "[bold bright_blue]🚀 ION CRM Deploy Agent[/bold bright_blue]\n\n"
        "Bu ajan:\n"
        "  ✅ Hangfire'ı devre dışı bırakır\n"
        "  ✅ Connection string'i düzeltir\n"
        "  ✅ git push yapar\n"
        "  ✅ GitHub Actions izler\n"
        "  ✅ Hataları kendi düzeltir",
        border_style="bright_blue"
    ))

    # .env'den oku
    prod_db = os.getenv("PROD_DATABASE_URL", "")
    dev_db = os.getenv("DEV_DATABASE_URL", "")

    credentials = {
        "PROD_DATABASE_URL": prod_db,
        "DEV_DATABASE_URL": dev_db,
        "NEON_CONNECTION_DOTNET": prod_db,
    }

    console.print("[dim]Başlıyor...[/dim]\n")

    tracker = CostTracker()
    agent = DeployAgent(credentials=credentials, cost_tracker=tracker)

    try:
        result = await agent.deploy()

        if "DEPLOYMENT_SUCCESS" in result:
            url = result.split("DEPLOYMENT_SUCCESS:")[1].strip().split()[0]
            console.print(Panel(
                f"[bold green]🎉 Deploy başarılı![/bold green]\n\n"
                f"API:     {url}\n"
                f"Swagger: {url}/swagger\n"
                f"Health:  {url}/health",
                border_style="green"
            ))
        else:
            console.print("[yellow]Tamamlandı — sonucu kontrol et[/yellow]")

    except KeyboardInterrupt:
        console.print("\n[yellow]İptal edildi[/yellow]")
    finally:
        tracker.print_summary()
        tracker.save()


if __name__ == "__main__":
    asyncio.run(main())
