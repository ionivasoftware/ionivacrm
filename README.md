# ION CRM — Autonomous Development Team

6 AI agents that build ION CRM autonomously, sprint by sprint, with your approval at every major decision.

## Quick Start

```bash
# 1. Activate virtual environment
cd ~/my-product-team
source .venv/bin/activate

# 2. Copy all these files into your workspace
# (overwrite if needed)

# 3. Run
python main.py
```

## Team Structure

| Agent | Role | Owns |
|---|---|---|
| 🧠 Orchestrator | PM — sprint planning, Jira | Plans & coordinates |
| 🏗️ Architect | DB design, scaffolding | output/docs/, solution structure |
| 💻 .NET Dev | Backend API | output/src/ |
| 🎨 Frontend | React app | output/frontend/ |
| 🧪 QA | Tests | output/tests/ |
| 🔒 Security | Audit | output/docs/security_report.md |

## Approval Gates

You will be asked to approve at:
1. **Sprint Plan** — before any code is written
2. **DB Schema** — before migrations run
3. **Each Sprint** — before next sprint starts
4. **Deployment** — before going live on Railway

## Output Structure

```
output/
├── IonCrm.sln
├── src/
│   ├── IonCrm.Domain/
│   ├── IonCrm.Application/
│   ├── IonCrm.Infrastructure/
│   └── IonCrm.API/
├── tests/
│   └── IonCrm.Tests/
├── frontend/          (React app)
└── docs/
    ├── schema.md      (DB design)
    └── security_report.md
```

## After Build Completes

```bash
# 1. Set your real database URL
nano .env
# DEV_DATABASE_URL=your-supabase-url

# 2. Run EF Core migrations
cd output
dotnet ef database update --project src/IonCrm.Infrastructure --startup-project src/IonCrm.API

# 3. Add production secrets to GitHub
gh secret set PROD_DATABASE_URL
gh secret set JWT_SECRET_PROD

# 4. Push to deploy
git push origin main
# GitHub Actions → Railway auto-deploy
```

## Cost Tracking

```bash
# View token usage and costs
python main.py → option 3
```

## Logs

```
logs/
├── approvals.log    — your approval decisions
├── tool_calls.log   — every agent action
└── costs.json       — token usage per run
```
