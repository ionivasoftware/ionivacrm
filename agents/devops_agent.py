"""
DevOps Agent
=============
Deployment yapar, hataları kendi düzeltir.
Credential gerekince senden sorar, gerisini halleder.
"""

import asyncio
import os
import sys
import getpass

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from agents.base_agent import BaseAgent, console
from rich.prompt import Prompt, Confirm
from rich.panel import Panel

WORKSPACE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUTPUT_DIR = os.path.join(WORKSPACE, "output")

SYSTEM_PROMPT = """
You are a Senior DevOps Engineer specializing in .NET Core deployments.

Your job:
1. Deploy ION CRM to Railway
2. Fix ALL errors autonomously without asking the user
3. Only ask user for: passwords, API keys, tokens (never guess these)
4. Keep trying until deployment succeeds

Tech stack:
- .NET 8 API (IonCrm.API)
- React frontend
- PostgreSQL (Neon or Supabase)
- Railway for hosting
- GitHub Actions for CI/CD

Working directory: {output_dir}

DEPLOYMENT CHECKLIST:
□ Connection string is in .NET format (Host=...;Database=...;Username=...;Password=...;Port=5432)
  NOT postgresql:// format
□ Hangfire needs PostgreSQL - if DB unreachable, disable it via config
□ JWT key must be set (min 32 chars)
□ ASPNETCORE_URLS=http://+:8080 in Dockerfile
□ Railway PORT must match Dockerfile EXPOSE
□ All NuGet packages compatible with net8.0
□ dotnet build must succeed before deploy
□ EF Core migrations must run successfully
□ railway up --service ion-crm-api must succeed

ERROR HANDLING RULES:
- "Network is unreachable IPv6" → connection string needs IPv4 host
- "Format of initialization string" → connection string wrong format, convert to Host=... format
- "TypeLoadException MetricsServiceExtensions" → package version mismatch, fix versions
- "Deploy crashed" → check railway logs, fix root cause
- "JWT key not configured" → add Jwt__Key environment variable
- "Host can't be null" → connection string empty or wrong format
- "IRepository not registered" → add to DependencyInjection.cs
- Package version conflicts → downgrade to net8.0 compatible versions

RAILWAY COMMANDS:
railway variables set "KEY=VALUE" --service ion-crm-api
railway logs --service ion-crm-api --tail 50
railway up --service ion-crm-api
railway variables --service ion-crm-api

ALWAYS:
1. Check current state first (railway logs, railway variables)
2. Fix issues one by one
3. Test locally with dotnet build before deploying
4. Commit fixes to git before railway up
5. Verify deployment with curl after success

When you need credentials, output EXACTLY:
NEED_CREDENTIAL:key_name:description
Example: NEED_CREDENTIAL:DB_PASSWORD:Neon database password

When deployment succeeds, output:
DEPLOYMENT_SUCCESS:url
Example: DEPLOYMENT_SUCCESS:https://ion-crm-api-production.up.railway.app
""".replace("{output_dir}", OUTPUT_DIR)


class DevOpsAgent(BaseAgent):
    name = "DevOps Engineer"
    emoji = "🚀"
    color = "bright_blue"
    ALLOWED_TOOLS = [
        "Read", "Write", "Edit", "MultiEdit",
        "Glob", "Bash", "WebSearch"
    ]

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.credentials = {}

    def get_system_prompt(self) -> str:
        return SYSTEM_PROMPT

    def _inject_credentials(self, prompt: str) -> str:
        """Inject collected credentials into prompt."""
        if self.credentials:
            creds_text = "\n".join(
                f"{k}={v}" for k, v in self.credentials.items()
            )
            return f"Available credentials:\n{creds_text}\n\n{prompt}"
        return prompt

    async def deploy(self) -> str:
        """Main deployment loop — keeps trying until success."""

        console.print(Panel(
            "[bold bright_blue]🚀 DevOps Agent Starting[/bold bright_blue]\n\n"
            "Bu ajan:\n"
            "  ✅ Railway deploy yapar\n"
            "  ✅ Hataları kendi düzeltir\n"
            "  ✅ Sadece şifre/token için sana sorar\n"
            "  ✅ Başarana kadar devam eder",
            border_style="bright_blue"
        ))

        # Collect initial credentials
        await self._collect_initial_credentials()

        prompt = self._inject_credentials(f"""
        Deploy ION CRM API to Railway. Follow these steps:

        1. Check current state:
           - Run: railway variables --service ion-crm-api
           - Run: railway logs --service ion-crm-api --tail 20
           - Check: cat {OUTPUT_DIR}/src/IonCrm.API/appsettings.json

        2. Fix connection string format:
           Convert to .NET format:
           Host=ep-purple-sound-a9vyag84-pooler.gwc.azure.neon.tech;
           Database=ioncrm;
           Username=neondb_owner;
           Password=NEON_PASSWORD;
           SSL Mode=Require;
           Trust Server Certificate=true

        3. Update Railway variables:
           railway variables set "ConnectionStrings__DefaultConnection=..." --service ion-crm-api
           railway variables set "Jwt__Key=..." --service ion-crm-api
           railway variables set "ASPNETCORE_ENVIRONMENT=Production" --service ion-crm-api
           railway variables set "Hangfire__Enabled=false" --service ion-crm-api

        4. Fix DependencyInjection.cs to make Hangfire optional:
           Read: {OUTPUT_DIR}/src/IonCrm.Infrastructure/DependencyInjection.cs
           Wrap Hangfire registration with:
           var enableHangfire = configuration.GetValue<bool>("Hangfire:Enabled", false);
           if (enableHangfire) {{ ... hangfire code ... }}

        5. Run migration against Neon:
           export ConnectionStrings__DefaultConnection="NEON_DOTNET_FORMAT"
           export ASPNETCORE_ENVIRONMENT=Development
           export Jwt__Key="fYxPg1WpWyPCjuBWlNB1gif30yS3dl9S//IfGhs/D+Q="
           cd {OUTPUT_DIR}
           dotnet ef database update --project src/IonCrm.Infrastructure --startup-project src/IonCrm.API

        6. Build and deploy:
           cd {OUTPUT_DIR}
           dotnet build (must succeed)
           git add -A
           git commit -m "fix: deployment fixes"
           git push origin main
           railway up --service ion-crm-api

        7. Verify:
           curl https://ion-crm-api-production.up.railway.app/health

        Fix every error you encounter. Keep trying.
        If you need the Neon password, output: NEED_CREDENTIAL:NEON_PASSWORD:Neon database password
        """)

        result = ""
        max_attempts = 3

        for attempt in range(1, max_attempts + 1):
            console.print(f"\n[dim]Attempt {attempt}/{max_attempts}[/dim]\n")

            result = await self.run(prompt, OUTPUT_DIR)

            # Check if credential needed
            if "NEED_CREDENTIAL:" in result:
                await self._handle_credential_request(result)
                prompt = self._inject_credentials(prompt)
                continue

            # Check success
            if "DEPLOYMENT_SUCCESS:" in result:
                url = result.split("DEPLOYMENT_SUCCESS:")[1].split()[0]
                console.print(Panel(
                    f"[bold green]🎉 Deployment başarılı![/bold green]\n\n"
                    f"API URL: {url}\n"
                    f"Swagger: {url}/swagger\n"
                    f"Health: {url}/health",
                    border_style="green"
                ))
                return url

            # Check if crashed and retry
            if "Deploy crashed" in result or "crashed" in result.lower():
                if attempt < max_attempts:
                    console.print(f"[yellow]⚠️  Deploy crashed, retrying ({attempt+1}/{max_attempts})...[/yellow]")
                    prompt = self._inject_credentials(
                        f"Previous attempt failed. Check railway logs and fix the issue.\n\n"
                        f"Previous output summary:\n{result[-500:]}\n\n"
                        f"Try again from where it failed."
                    )

        return result

    async def _collect_initial_credentials(self):
        """Ask for known required credentials upfront."""
        console.print("\n[bold]Deployment için gerekli bilgiler:[/bold]\n")

        # Neon password
        neon_pass = getpass.getpass("🔑 Neon DB şifresi: ")
        if neon_pass:
            self.credentials["NEON_PASSWORD"] = neon_pass
            self.credentials["NEON_CONNECTION_DOTNET"] = (
                f"Host=ep-purple-sound-a9vyag84-pooler.gwc.azure.neon.tech;"
                f"Database=ioncrm;"
                f"Username=neondb_owner;"
                f"Password={neon_pass};"
                f"SSL Mode=Require;"
                f"Trust Server Certificate=true"
            )

        console.print("[dim]Credentials alındı, deployment başlıyor...[/dim]\n")

    async def _handle_credential_request(self, agent_output: str):
        """Parse NEED_CREDENTIAL requests and ask user."""
        lines = agent_output.split("\n")
        for line in lines:
            if "NEED_CREDENTIAL:" in line:
                parts = line.split("NEED_CREDENTIAL:")[1].split(":")
                key = parts[0].strip()
                desc = parts[1].strip() if len(parts) > 1 else key

                if key not in self.credentials:
                    value = getpass.getpass(f"🔑 {desc}: ")
                    self.credentials[key] = value
                    console.print(f"[green]✅ {key} alındı[/green]")
