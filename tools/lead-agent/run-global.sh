#!/usr/bin/env bash
set -euo pipefail

# ─────────────────────────────────────────────────────────────────────────────
# LiftDesk Global Lead Ajanı  (CRM uçları: /api/v1/lead-agent/global/*)
#
# TR ajanının (run.sh) TR-dışı kardeşi. Ülke + bölge bazlı arar (varsayılan GB), bulduklarını
# POST /lead-agent/global/candidates ile GlobalLeads tablosuna ADAY yazar. Dedupe sunucuda;
# SuperAdmin "Global Lead'ler" ekranında onaylayınca İngilizce tanıtım + takip dizisi başlar.
#
# Env:
#   CRM_API_BASE      zorunlu
#   LEAD_AGENT_KEY    zorunlu  (LeadAgent__ApiKey ile aynı — X-Agent-Key)
#   COUNTRY           ops.     (ISO2, varsayılan GB)
#   CLAUDE_MODEL      ops.     (varsayılan claude-opus-4-8)
#   REGIONS_OVERRIDE  ops.     ("London,Leeds and West Yorkshire" — boşsa API rotasyonu)
#   REGIONS_PER_RUN   ops.     (varsayılan 2)
#   MAX_PER_REGION    ops.     (varsayılan 15)
# ─────────────────────────────────────────────────────────────────────────────

API="${CRM_API_BASE:?CRM_API_BASE gerekli}"
KEY="${LEAD_AGENT_KEY:?LEAD_AGENT_KEY gerekli}"
COUNTRY="${COUNTRY:-GB}"
MODEL="${CLAUDE_MODEL:-claude-opus-4-8}"
PER_RUN="${REGIONS_PER_RUN:-2}"
MAX="${MAX_PER_REGION:-15}"
HERE="$(cd "$(dirname "$0")" && pwd)"
PROMPT_TEMPLATE="$HERE/prompt.global.md"
AUTH="X-Agent-Key: $KEY"
RUN_ID="gh-${COUNTRY,,}-$(date -u +%Y%m%dT%H%M%S)"
case "$COUNTRY" in
  GB) COUNTRY_NAME="the United Kingdom" ;;
  IE) COUNTRY_NAME="Ireland" ;;
  *)  COUNTRY_NAME="$COUNTRY" ;;
esac

api() { # method path [json]
  local m="$1" p="$2" d="${3:-}"
  if [ -n "$d" ]; then
    curl -sS -X "$m" -H "$AUTH" -H "Content-Type: application/json" -d "$d" -w '\n%{http_code}' "$API$p"
  else
    curl -sS -X "$m" -H "$AUTH" -w '\n%{http_code}' "$API$p"
  fi
}

# ── 1. Bölgeler ───────────────────────────────────────────────────────────────
if [ -n "${REGIONS_OVERRIDE:-}" ]; then
  REGIONS_JSON=$(printf '%s' "$REGIONS_OVERRIDE" | jq -R -c 'split(",") | map(gsub("^\\s+|\\s+$"; "")) | map(select(length > 0))')
else
  OUT=$(api GET "/api/v1/lead-agent/global/next-regions?country=$COUNTRY&count=$PER_RUN")
  HTTP=$(printf '%s' "$OUT" | tail -n1); BODY=$(printf '%s' "$OUT" | sed '$d')
  if [ "$HTTP" != "200" ]; then
    echo "[global-lead] HATA: bölge listesi alınamadı (HTTP $HTTP). CRM_API_BASE / LEAD_AGENT_KEY kontrol (401=anahtar, 503=Railway'de LeadAgent__ApiKey yok)."
    echo "[global-lead]   yanıt: $(printf '%s' "$BODY" | head -c 300)"; exit 1
  fi
  REGIONS_JSON=$(printf '%s' "$BODY" | jq -c '.data.regions')
fi
echo "[global-lead] koşu $RUN_ID — $COUNTRY bölgeler: $(printf '%s' "$REGIONS_JSON" | jq -r 'join(", ")')"
api POST "/api/v1/lead-agent/global/runs/start" "$(jq -n -c --arg r "$RUN_ID" --arg c "$COUNTRY" --argjson g "$REGIONS_JSON" '{runId:$r, country:$c, regions:$g}')" >/dev/null || true

# ── 2. Bölge başına araştırma ────────────────────────────────────────────────
total_created=0; total_exist=0; total_invalid=0
while IFS= read -r REGION; do
  [ -z "$REGION" ] && continue
  echo "[global-lead] ── $REGION araştırılıyor (en fazla $MAX aday)…"
  PROMPT="$(sed -e "s/{{REGION}}/$REGION/g" -e "s/{{COUNTRY_NAME}}/$COUNTRY_NAME/g" -e "s/{{MAX}}/$MAX/g" "$PROMPT_TEMPLATE")"
  LOG="/tmp/global_lead_$(printf '%s' "$REGION" | tr -c 'a-zA-Z0-9' '_').log"
  # `</dev/null` ŞART: claude stdin'i tüketip while döngüsünü kırar.
  RAW=$(claude -p "$PROMPT" --model "$MODEL" --output-format json \
        --allowedTools "WebSearch,WebFetch" </dev/null 2>"$LOG" || true)
  TEXT=$(printf '%s' "$RAW" | jq -r '.result // empty' 2>/dev/null || true)
  if grep -qiE "hit your (session|usage|limit)|usage limit|rate limit" "$LOG" 2>/dev/null; then
    echo "[global-lead] Claude kullanım limiti — koşu burada bitiyor, kalan bölgeler sonraki haftaya."; break
  fi
  JSON=$(printf '%s' "$TEXT" | python3 -c 'import sys,re,json
t=sys.stdin.read(); m=re.search(r"\[.*\]", t, re.S)
if not m: sys.exit(0)
try:
    arr=json.loads(m.group(0))
except Exception: sys.exit(0)
out=[]
for x in arr if isinstance(arr,list) else []:
    if not isinstance(x,dict): continue
    name=(x.get("companyName") or "").strip()
    if not name: continue
    if not (x.get("email") or x.get("website")): continue
    out.append({k: x.get(k) for k in ["companyName","contactName","phone","email","website","address","city","region","source","sourceUrl","snippet","confidence"]})
sys.stdout.write(json.dumps(out, ensure_ascii=False))' 2>/dev/null || true)
  COUNT=$(printf '%s' "${JSON:-[]}" | jq 'length' 2>/dev/null || echo 0)
  if [ "$COUNT" = "0" ]; then
    echo "[global-lead] $REGION: geçerli aday yok (çıktı JSON değil ya da e-posta/web eksik)."
    echo "[global-lead] ── claude metni (son 15 satır) ──"; printf '%s\n' "${TEXT:-(boş)}" | tail -n 15 | cut -c1-300
    echo "[global-lead] ── stderr (son 8) ──"; tail -n 8 "$LOG" 2>/dev/null | cut -c1-300 || true
    continue
  fi
  BODY=$(jq -n -c --arg r "$RUN_ID" --arg c "$COUNTRY" --arg g "$REGION" --argjson items "$JSON" '{runId:$r, country:$c, region:$g, items:$items}')
  OUT=$(api POST "/api/v1/lead-agent/global/candidates" "$BODY")
  HTTP=$(printf '%s' "$OUT" | tail -n1); RESP=$(printf '%s' "$OUT" | sed '$d')
  if [ "$HTTP" != "200" ]; then echo "[global-lead] $REGION: CRM reddetti (HTTP $HTTP): $(printf '%s' "$RESP" | head -c 300)"; continue; fi
  c=$(printf '%s' "$RESP" | jq -r '.data.created'); e=$(printf '%s' "$RESP" | jq -r '.data.existing'); i=$(printf '%s' "$RESP" | jq -r '.data.invalid')
  echo "[global-lead] $REGION: $COUNT aday → $c yeni (onaya düştü), $e önceden önerilmiş, $i geçersiz"
  printf '%s' "$RESP" | jq -r '.data.results[] | "[global-lead]    \(.status | ascii_upcase | .[0:9]) \(.companyName)\(if .reason then " — " + .reason else "" end)"' | head -40
  total_created=$((total_created + c)); total_exist=$((total_exist + e)); total_invalid=$((total_invalid + i))
done <<< "$(printf '%s' "$REGIONS_JSON" | jq -r '.[]')"

# ── 3. Bitir ──────────────────────────────────────────────────────────────────
api POST "/api/v1/lead-agent/global/runs/$RUN_ID/finish" "$(jq -n -c --arg n "yeni=$total_created önceden=$total_exist geçersiz=$total_invalid" '{notes:$n}')" >/dev/null || true
echo "[global-lead] tamam: $total_created yeni global lead onay bekliyor (CRM → Global Lead'ler), $total_exist önceden, $total_invalid geçersiz."
