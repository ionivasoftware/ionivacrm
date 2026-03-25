#!/usr/bin/env bash
# ============================================================
# ION CRM — Sprint 0  Jira Ticket Creator  (curl version)
# ============================================================
# PREREQUISITES before running:
#
#  1. Regenerate API token at:
#     https://id.atlassian.com/manage-profile/security/api-tokens
#
#  2. Create the PROD project in Jira UI:
#     https://ofcakmakci.atlassian.net
#     → Projects → Create project
#     → Template: Scrum software development
#     → Key: PROD  |  Name: ION CRM
#
#  3. Set env var and run:
#     export JIRA_API_TOKEN='<your-new-token>'
#     bash jira-curl-commands.sh
# ============================================================

set -e

EMAIL="ofcakmakci@gmail.com"
BASE="https://ofcakmakci.atlassian.net"
PROJECT="PROD"
AUTH=$(echo -n "${EMAIL}:${JIRA_API_TOKEN}" | base64 -w 0)

echo "============================================================"
echo "  ION CRM — Sprint 0 Jira Ticket Creator"
echo "============================================================"

# ── AUTH CHECK ───────────────────────────────────────────────
echo ""
echo "🔑 Checking authentication..."
ME_RESP=$(curl -s "${BASE}/rest/api/3/myself" \
  -H "Authorization: Basic ${AUTH}" \
  -H "Accept: application/json")
STATUS=$(echo "$ME_RESP" | python3 -c "import sys,json; d=json.load(sys.stdin); print('ok' if 'accountId' in d else 'fail')" 2>/dev/null || echo "fail")

if [ "$STATUS" != "ok" ]; then
  echo "❌ Auth failed."
  echo "   → Regenerate your API token at:"
  echo "     https://id.atlassian.com/manage-profile/security/api-tokens"
  echo "   → export JIRA_API_TOKEN='<new-token>'"
  exit 1
fi

ACCOUNT_ID=$(echo "$ME_RESP" | python3 -c "import sys,json; print(json.load(sys.stdin)['accountId'])")
DISPLAY_NAME=$(echo "$ME_RESP" | python3 -c "import sys,json; print(json.load(sys.stdin).get('displayName',''))")
echo "✅ Authenticated as: $DISPLAY_NAME (accountId: $ACCOUNT_ID)"

# ── VERIFY PROJECT ───────────────────────────────────────────
echo ""
echo "🏗  Checking project PROD..."
PROJ_STATUS=$(curl -s -o /dev/null -w "%{http_code}" \
  "${BASE}/rest/api/3/project/${PROJECT}" \
  -H "Authorization: Basic ${AUTH}" \
  -H "Accept: application/json")

if [ "$PROJ_STATUS" == "404" ]; then
  echo "❌ Project PROD not found."
  echo "   → Create it manually:"
  echo "     1. Open: ${BASE}"
  echo "     2. Projects → Create project"
  echo "     3. Scrum software development template"
  echo "     4. Key: PROD  |  Name: ION CRM"
  echo "   Then re-run this script."
  exit 1
fi
echo "✅ Project PROD found (HTTP $PROJ_STATUS)"

# ── GET ISSUE TYPE IDs ───────────────────────────────────────
echo ""
echo "📋 Fetching issue type IDs..."
PROJ_DATA=$(curl -s "${BASE}/rest/api/3/project/${PROJECT}" \
  -H "Authorization: Basic ${AUTH}" -H "Accept: application/json")

EPIC_TYPE_ID=$(echo "$PROJ_DATA" | python3 -c "
import json,sys; d=json.load(sys.stdin)
for t in d.get('issueTypes',[]):
  if t['name']=='Epic': print(t['id']); break
" 2>/dev/null)
STORY_TYPE_ID=$(echo "$PROJ_DATA" | python3 -c "
import json,sys; d=json.load(sys.stdin)
for t in d.get('issueTypes',[]):
  if t['name']=='Story': print(t['id']); break
" 2>/dev/null)

[ -z "$EPIC_TYPE_ID"  ] && EPIC_TYPE_ID="Epic"   && echo "  ⚠ Using fallback Epic type name"
[ -z "$STORY_TYPE_ID" ] && STORY_TYPE_ID="Story"  && echo "  ⚠ Using fallback Story type name"
echo "  Epic type id  : $EPIC_TYPE_ID"
echo "  Story type id : $STORY_TYPE_ID"

# ── GET / CREATE SPRINT ──────────────────────────────────────
echo ""
echo "🗓  Looking for Sprint 0 board & sprint..."
BOARD_ID=$(curl -s "${BASE}/rest/agile/1.0/board?projectKeyOrId=${PROJECT}&type=scrum" \
  -H "Authorization: Basic ${AUTH}" -H "Accept: application/json" | \
  python3 -c "import json,sys; v=json.load(sys.stdin).get('values',[]); print(v[0]['id'] if v else '')" 2>/dev/null)

SPRINT_ID=""
if [ -n "$BOARD_ID" ]; then
  echo "  ✅ Board id: $BOARD_ID"
  # Check for existing Sprint 0
  SPRINT_ID=$(curl -s "${BASE}/rest/agile/1.0/board/${BOARD_ID}/sprint?state=active,future" \
    -H "Authorization: Basic ${AUTH}" -H "Accept: application/json" | \
    python3 -c "
import json,sys
for s in json.load(sys.stdin).get('values',[]):
  if 'Sprint 0' in s.get('name',''):
    print(s['id']); break
" 2>/dev/null)

  if [ -z "$SPRINT_ID" ]; then
    echo "  Creating Sprint 0..."
    SPRINT_ID=$(curl -s -X POST "${BASE}/rest/agile/1.0/sprint" \
      -H "Authorization: Basic ${AUTH}" -H "Content-Type: application/json" \
      -d "{\"name\":\"Sprint 0 — Analysis & Architecture\",\"originBoardId\":${BOARD_ID},\"goal\":\"Analyze old DB, design new schema, define API contracts, scaffold project\"}" | \
      python3 -c "import json,sys; print(json.load(sys.stdin).get('id',''))" 2>/dev/null)
    [ -n "$SPRINT_ID" ] && echo "  ✅ Sprint created: id $SPRINT_ID"
  else
    echo "  ✅ Sprint 0 found: id $SPRINT_ID"
  fi
else
  echo "  ⚠ No Scrum board found — sprint field will be omitted"
fi

# ── SPRINT FIELD HELPER ──────────────────────────────────────
sprint_field() {
  [ -n "$SPRINT_ID" ] && echo ", \"customfield_10020\": $SPRINT_ID" || echo ""
}

# ── CREATE EPIC ──────────────────────────────────────────────
echo ""
echo "📌 Creating Epic..."

EPIC_RESP=$(curl -s -X POST "${BASE}/rest/api/3/issue" \
  -H "Authorization: Basic ${AUTH}" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json" \
  -d "{
    \"fields\": {
      \"project\":           {\"key\": \"${PROJECT}\"},
      \"summary\":           \"Sprint 0 — Analysis & Architecture\",
      \"issuetype\":         {\"name\": \"Epic\"},
      \"labels\":            [\"architecture\", \"sprint-0\"],
      \"customfield_10014\": \"Sprint 0 — Analysis & Architecture\",
      \"customfield_10016\": 29
      $(sprint_field)
    }
  }")

EPIC_KEY=$(echo "$EPIC_RESP" | python3 -c "import json,sys; print(json.load(sys.stdin).get('key','ERROR'))")
echo "  ✅ Epic: $EPIC_KEY  →  ${BASE}/browse/${EPIC_KEY}"
sleep 0.5

# ── STORY CREATOR FUNCTION ───────────────────────────────────
create_story() {
  local STORY_ID="$1" TITLE="$2" POINTS="$3" LABELS="$4" DESC="$5"
  local RESP KEY
  RESP=$(curl -s -X POST "${BASE}/rest/api/3/issue" \
    -H "Authorization: Basic ${AUTH}" \
    -H "Content-Type: application/json" \
    -H "Accept: application/json" \
    -d "{
      \"fields\": {
        \"project\":           {\"key\": \"${PROJECT}\"},
        \"summary\":           ${TITLE},
        \"issuetype\":         {\"name\": \"Story\"},
        \"labels\":            ${LABELS},
        \"parent\":            {\"key\": \"${EPIC_KEY}\"},
        \"customfield_10014\": \"${EPIC_KEY}\",
        \"customfield_10016\": ${POINTS}
        $(sprint_field),
        \"description\": {
          \"type\": \"doc\", \"version\": 1,
          \"content\": [{\"type\": \"paragraph\", \"content\": [{\"type\": \"text\", \"text\": ${DESC}}]}]
        }
      }
    }")
  KEY=$(echo "$RESP" | python3 -c "import json,sys; print(json.load(sys.stdin).get('key','ERROR'))" 2>/dev/null || echo "ERROR")
  echo "  ✅ $KEY  ($STORY_ID)  →  ${BASE}/browse/${KEY}"
  sleep 0.4
}

# ── CREATE 6 STORIES ─────────────────────────────────────────
echo ""
echo "📝 Creating 6 Stories..."

create_story "ION-S0-01" \
  '"[ARCHITECT] Analyze MSSQL .bak file and extract legacy schema"' \
  5 '["backend","migration","analysis"]' \
  '"Restore 4.4MB MSSQL backup. Extract DDL. Document tables/columns/FKs/indexes. Map legacy customers + contact history to new schema. Flag PII fields. Map MSSQL types to PostgreSQL. Outputs: legacy-schema.md, migration-mapping.md"'

create_story "ION-S0-02" \
  '"[ARCHITECT] Design PostgreSQL target schema"' \
  8 '["backend","database","architecture"]' \
  '"Design full PostgreSQL schema: Projects, Users, UserProjectRoles, Customers, ContactHistory, Notes, CustomerTasks, Opportunities, SyncLogs, RefreshTokens, AuditLogs. Every table: Id(UUID), CreatedAt, UpdatedAt, IsDeleted, ProjectId. All enums, indexes, FKs defined. Outputs: db-schema.md, entity-stubs/"'

create_story "ION-S0-03" \
  '"[ARCHITECT] Define API contracts and OpenAPI spec"' \
  5 '["backend","api","architecture"]' \
  '"Define REST endpoints: Auth (login/logout/refresh/me), SuperAdmin, Customers CRUD, ContactHistory, Notes, Tasks, Opportunities, Sync (POST /sync/saas-a, /sync/saas-b + outbound callbacks), Dashboard. Request/response schemas, roles, error codes, ApiResponse<T> wrapper. Output: api-contracts.md"'

create_story "ION-S0-04" \
  '"[ARCHITECT] Define solution folder structure and Clean Architecture scaffold"' \
  3 '["backend","devops","architecture"]' \
  '"Document .NET Clean Architecture layers: Domain, Application, Infrastructure, Api, Tests. React 18+TS+shadcn/ui frontend structure. GitHub Actions CI/CD workflows. Docker Compose (postgres, mssql, api, frontend). All env vars in .env.example. Outputs: project-structure.md, .env.example"'

create_story "ION-S0-05" \
  '"[ARCHITECT] Define Sync Service architecture (SaaS A & B to ION CRM)"' \
  5 '["backend","sync","architecture"]' \
  '"Design bi-directional sync: SaaS A/B push every 15min via POST /sync/saas-a and /sync/saas-b (upsert by ExternalId, idempotency). ION CRM instant outbound callbacks (3 retries, exponential backoff). SyncLog entity. IHostedService background poller. Output: sync-architecture.md"'

create_story "ION-S0-06" \
  '"[ARCHITECT] Document ADRs, tech decisions, and dev environment setup"' \
  3 '["architecture","documentation","devops"]' \
  '"Write 7 ADRs: Clean Architecture, MediatR/CQRS, Neon PostgreSQL, Railway deploy, multi-tenancy, sync strategy, JWT+Refresh tokens. Dev setup guide: prerequisites, clone, env vars, dotnet run, npm dev, EF migrations. Outputs: docs/adr/*.md, dev-setup.md"'

# ── DONE ─────────────────────────────────────────────────────
echo ""
echo "============================================================"
echo "  🎉 Sprint 0 tickets created successfully!"
echo "  🔗 Board  : ${BASE}/jira/software/projects/${PROJECT}/boards"
echo "  🔗 Backlog: ${BASE}/jira/software/projects/${PROJECT}/backlog"
echo "============================================================"
