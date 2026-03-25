"""
ION CRM — CI/CD Setup Script
==============================
Çalıştır: python setup_cicd.py

Bu script:
1. GitHub Actions workflow'larını oluşturur
2. Neon preview branch entegrasyonu kurar
3. Railway production deploy (manual approve) ayarlar
4. GitHub secrets/variables ekler
5. Production environment protection kurar
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
from rich.prompt import Prompt

WORKSPACE = os.path.dirname(os.path.abspath(__file__))
OUTPUT_DIR = os.path.join(WORKSPACE, "output")

SYSTEM_PROMPT = f"""
You are a Senior DevOps Engineer setting up CI/CD for ION CRM.

Working directory: {WORKSPACE}
Repo: omercakmakci/ionivacrm

YOUR TASKS:

## TASK 1: Create preview.yml
Write this file to {WORKSPACE}/.github/workflows/preview.yml

The workflow should:
- Trigger on pull_request (opened, reopened, synchronize, closed)
- On open/reopen/sync:
  1. Get branch name using tj-actions/branch-names@v8
  2. Create Neon branch using neondatabase/create-branch-action@v6
     - project_id: ${{{{ vars.NEON_PROJECT_ID }}}}
     - branch_name: preview/pr-${{{{ github.event.number }}}}-${{{{ needs.setup.outputs.branch }}}}
     - api_key: ${{{{ secrets.NEON_API_KEY }}}}
     - expires_at: 14 days from now
  3. Run EF Core migrations:
     - Setup .NET
     - Run: dotnet ef database update --project src/IonCrm.Infrastructure --startup-project src/IonCrm.API
     - Use DB URL from Neon branch output as ConnectionStrings__DefaultConnection
     - Also set Jwt__Key from secrets
  4. Deploy to Railway preview service via GraphQL API
  5. Comment on PR with preview URL
- On close: Delete Neon branch using neondatabase/delete-branch-action@v3

## TASK 2: Create deploy.yml
Write this file to {WORKSPACE}/.github/workflows/deploy.yml

Jobs:
1. test-backend (ubuntu-latest):
   - actions/checkout@v4
   - actions/setup-dotnet@v4 with dotnet-version: 8.0.x
   - working-directory: ./output
   - Steps: dotnet restore, dotnet build --configuration Release, dotnet test --configuration Release

2. test-frontend (ubuntu-latest):
   - actions/checkout@v4
   - actions/setup-node@v4 with node-version: 20.x, cache: npm
   - cache-dependency-path: output/frontend/package-lock.json
   - working-directory: ./output/frontend
   - Steps: npm ci, npm run build
   - env: VITE_API_URL: ${{{{ secrets.VITE_API_URL }}}}

3. security-scan (ubuntu-latest, needs test-backend):
   - dotnet list package --vulnerable --include-transitive
   - grep check for hardcoded secrets

4. deploy-production (needs all above, only on push to main):
   - environment: name: production, url: https://ion-crm-api-production.up.railway.app
   - Deploy backend via Railway GraphQL API:
     SERVICE_ID="987799b6-18b9-4223-81c6-505ffc6717ba"
     curl -s -X POST https://backboard.railway.app/graphql/v2
       -H "Authorization: Bearer $RAILWAY_TOKEN"
       -H "Content-Type: application/json"
       -d '{{"query":"mutation {{ serviceInstanceDeploy(serviceId: \\"SERVICE_ID\\") }}"}}'
   - Wait 45 seconds
   - Health check: curl https://ion-crm-api-production.up.railway.app/health
   - Deploy frontend via Railway GraphQL API (separate service)

## TASK 3: Setup GitHub
Run these commands:

1. Get GitHub user ID:
   gh api user --jq '.id'

2. Create production environment with protection:
   gh api repos/omercakmakci/ionivacrm/environments/production -X PUT -H "Accept: application/vnd.github+json"

3. Add reviewer to production environment (use user ID from step 1):
   gh api repos/omercakmakci/ionivacrm/environments/production -X PUT \\
     -H "Accept: application/vnd.github+json" \\
     -f "reviewers[0][type]=User" \\
     -F "reviewers[0][id]=USER_ID"

4. Add GitHub variable:
   gh variable set NEON_PROJECT_ID --body "NEON_PROJECT_ID_VALUE" --repo omercakmakci/ionivacrm

5. Add GitHub secret:
   gh secret set NEON_API_KEY --body "NEON_API_KEY_VALUE" --repo omercakmakci/ionivacrm

Use NEON_PROJECT_ID and NEON_API_KEY from the credentials provided to you.

## TASK 4: Commit and Push
cd {WORKSPACE}
git add .github/workflows/
git commit -m "ci: add Neon preview branches and manual approval for production deploy"
git push origin main

## TASK 5: Verify
gh workflow list --repo omercakmakci/ionivacrm

Report all done with:
SETUP_COMPLETE:preview_workflow,deploy_workflow,github_environment
"""


class CICDSetupAgent(BaseAgent):
    name = "DevOps Engineer"
    emoji = "⚙️"
    color = "cyan"
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
Available credentials (use these directly in the commands):
{creds}

Execute all 5 tasks in order. Write the workflow files first, then setup GitHub, then commit and push.
Make sure the workflow files are complete and correct before committing.
"""


async def main():
    console.print(Panel(
        "[bold cyan]⚙️  ION CRM — CI/CD Setup[/bold cyan]\n\n"
        "Bu script:\n"
        "  ✅ preview.yml — PR'larda Neon branch + Railway preview deploy\n"
        "  ✅ deploy.yml  — Push to main → testler → manual approve → prod\n"
        "  ✅ GitHub production environment protection\n"
        "  ✅ Neon API entegrasyonu",
        border_style="cyan"
    ))

    console.print("\n[bold]Gerekli bilgiler:[/bold]\n")

    neon_project_id = Prompt.ask("🔑 Neon Project ID (console.neon.tech → Settings → General)")
    neon_api_key = getpass.getpass("🔑 Neon API Key (console.neon.tech → Account → API Keys): ")

    credentials = {
        "NEON_PROJECT_ID": neon_project_id,
        "NEON_API_KEY": neon_api_key,
        "NEON_PROJECT_ID_VALUE": neon_project_id,
        "NEON_API_KEY_VALUE": neon_api_key,
    }

    console.print("\n[dim]CI/CD kurulumu başlıyor...[/dim]\n")

    tracker = CostTracker()
    agent = CICDSetupAgent(credentials=credentials, cost_tracker=tracker)

    try:
        result = await agent.run(agent._build_prompt(), WORKSPACE)

        if "SETUP_COMPLETE" in result:
            console.print(Panel(
                "[bold green]🎉 CI/CD kurulumu tamamlandı![/bold green]\n\n"
                "Artık:\n"
                "  → PR açınca Neon branch + preview deploy otomatik\n"
                "  → main'e push → testler → senden onay → prod deploy\n\n"
                "Test etmek için:\n"
                "  1. Yeni bir branch aç\n"
                "  2. Küçük bir değişiklik yap\n"
                "  3. PR aç → preview URL gelecek\n"
                "  4. main'e merge → onay bekleyecek",
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
