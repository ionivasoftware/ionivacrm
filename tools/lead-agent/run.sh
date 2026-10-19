#!/usr/bin/env bash
set -euo pipefail

# ─────────────────────────────────────────────────────────────────────────────
# Liftdesk Lead Ajanı  (CRM uçları: /api/v1/lead-agent/*)
#
# 1) API'den sıradaki illeri alır (60 günlük rotasyon) ya da CITIES_OVERRIDE kullanır.
# 2) Her il için Claude Code'u WebSearch + WebFetch ile koşturur: asansör bakım/montaj firmaları
#    (ad, ünvan, telefon, web, e-posta, adres, ilçe, kaynak URL, güven).
# 3) Çıktıyı doğrular ve POST /candidates ile CRM'e yazar. Dedupe SUNUCUDA (müşteriler + eski
#    adaylar); yeni adaylar SuperAdmin onayına düşer. Bu ajan müşteri OLUŞTURMAZ.
#
# Env:
#   CRM_API_BASE     zorunlu  (ör. https://ion-crm-api-production.up.railway.app)
#   LEAD_AGENT_KEY   zorunlu  (LeadAgent__ApiKey — X-Agent-Key başlığı)
#   CLAUDE_MODEL     ops.     (varsayılan claude-opus-4-8 — npm CLI'nin tanıdığı model)
#   CITIES_OVERRIDE  ops.     ("İstanbul,Bursa" — boşsa API rotasyonu)
#   CITIES_PER_RUN   ops.     (varsayılan 2)
#   MAX_PER_CITY     ops.     (varsayılan 15)
# ─────────────────────────────────────────────────────────────────────────────

API="${CRM_API_BASE:?CRM_API_BASE gerekli}"
KEY="${LEAD_AGENT_KEY:?LEAD_AGENT_KEY gerekli}"
MODEL="${CLAUDE_MODEL:-claude-opus-4-8}"
PER_RUN="${CITIES_PER_RUN:-2}"
MAX="${MAX_PER_CITY:-15}"
HERE="$(cd "$(dirname "$0")" && pwd)"
PROMPT_TEMPLATE="$HERE/prompt.md"
AUTH="X-Agent-Key: $KEY"
RUN_ID="gh-$(date -u +%Y%m%dT%H%M%S)"

api() { # method path [json]
  local m="$1" p="$2" d="${3:-}"
  if [ -n "$d" ]; then
    curl -sS -X "$m" -H "$AUTH" -H "Content-Type: application/json" -d "$d" -w '\n%{http_code}' "$API$p"
  else
    curl -sS -X "$m" -H "$AUTH" -w '\n%{http_code}' "$API$p"
  fi
}

# ── 1. İller ──────────────────────────────────────────────────────────────────
if [ -n "${CITIES_OVERRIDE:-}" ]; then
  CITIES_JSON=$(printf '%s' "$CITIES_OVERRIDE" | jq -R -c 'split(",") | map(gsub("^\\s+|\\s+$"; "")) | map(select(length > 0))')
else
  OUT=$(api GET "/api/v1/lead-agent/next-cities?count=$PER_RUN")
  HTTP=$(printf '%s' "$OUT" | tail -n1); BODY=$(printf '%s' "$OUT" | sed '$d')
  if [ "$HTTP" != "200" ]; then
    echo "[lead] HATA: il listesi alınamadı (HTTP $HTTP). CRM_API_BASE / LEAD_AGENT_KEY kontrol (401=anahtar, 503=Railway'de LeadAgent__ApiKey yok)."
    echo "[lead]   yanıt: $(printf '%s' "$BODY" | head -c 300)"; exit 1
  fi
  CITIES_JSON=$(printf '%s' "$BODY" | jq -c '.data.cities')
fi
echo "[lead] koşu $RUN_ID — iller: $(printf '%s' "$CITIES_JSON" | jq -r 'join(", ")')"
api POST "/api/v1/lead-agent/runs/start" "$(jq -n -c --arg r "$RUN_ID" --argjson c "$CITIES_JSON" '{runId:$r, cities:$c}')" >/dev/null || true

# ── 2. İl başına araştırma ────────────────────────────────────────────────────
total_created=0; total_dup=0; total_exist=0; total_invalid=0
while IFS= read -r CITY; do
  [ -z "$CITY" ] && continue
  echo "[lead] ── $CITY araştırılıyor (en fazla $MAX aday)…"
  PROMPT="$(sed -e "s/{{CITY}}/$CITY/g" -e "s/{{MAX}}/$MAX/g" "$PROMPT_TEMPLATE")"
  LOG="/tmp/lead_$(printf '%s' "$CITY" | tr -c 'a-zA-Z0-9' '_').log"
  # `</dev/null` ŞART: claude stdin'i tüketip while döngüsünü kırar (sosyal ajanla aynı tuzak).
  RAW=$(claude -p "$PROMPT" --model "$MODEL" --output-format json \
        --allowedTools "WebSearch,WebFetch" </dev/null 2>"$LOG" || true)
  TEXT=$(printf '%s' "$RAW" | jq -r '.result // empty' 2>/dev/null || true)
  if grep -qiE "hit your (session|usage|limit)|usage limit|rate limit" "$LOG" 2>/dev/null; then
    echo "[lead] Claude kullanım limiti — koşu burada bitiyor, kalan iller sonraki haftaya."; break
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
    if not (x.get("phone") or x.get("website")): continue
    out.append({k: x.get(k) for k in ["companyName","legalTitle","phone","email","website","address","city","district","taxNumber","mersisNo","source","sourceUrl","snippet","confidence"]})
sys.stdout.write(json.dumps(out, ensure_ascii=False))' 2>/dev/null || true)
  COUNT=$(printf '%s' "${JSON:-[]}" | jq 'length' 2>/dev/null || echo 0)
  if [ "$COUNT" = "0" ]; then
    echo "[lead] $CITY: geçerli aday yok (çıktı JSON değil ya da telefon/web eksik)."
    echo "[lead] ── claude metni (son 15 satır) ──"; printf '%s\n' "${TEXT:-(boş)}" | tail -n 15 | cut -c1-300
    echo "[lead] ── stderr (son 8) ──"; tail -n 8 "$LOG" 2>/dev/null | cut -c1-300 || true
    continue
  fi
  BODY=$(jq -n -c --arg r "$RUN_ID" --arg c "$CITY" --argjson items "$JSON" '{runId:$r, city:$c, items:$items}')
  OUT=$(api POST "/api/v1/lead-agent/candidates" "$BODY")
  HTTP=$(printf '%s' "$OUT" | tail -n1); RESP=$(printf '%s' "$OUT" | sed '$d')
  if [ "$HTTP" != "200" ]; then echo "[lead] $CITY: CRM reddetti (HTTP $HTTP): $(printf '%s' "$RESP" | head -c 300)"; continue; fi
  c=$(printf '%s' "$RESP" | jq -r '.data.created'); d=$(printf '%s' "$RESP" | jq -r '.data.duplicates'); e=$(printf '%s' "$RESP" | jq -r '.data.existing'); i=$(printf '%s' "$RESP" | jq -r '.data.invalid')
  echo "[lead] $CITY: $COUNT aday → $c yeni (onaya düştü), $d mükerrer müşteri, $e önceden önerilmiş, $i geçersiz"
  printf '%s' "$RESP" | jq -r '.data.results[] | "[lead]    \(.status | ascii_upcase | .[0:9]) \(.companyName)\(if .reason then " — " + .reason else "" end)"' | head -40
  total_created=$((total_created + c)); total_dup=$((total_dup + d)); total_exist=$((total_exist + e)); total_invalid=$((total_invalid + i))
done <<< "$(printf '%s' "$CITIES_JSON" | jq -r '.[]')"

# ── 3. Bitir ──────────────────────────────────────────────────────────────────
api POST "/api/v1/lead-agent/runs/$RUN_ID/finish" "$(jq -n -c --arg n "yeni=$total_created mükerrer=$total_dup önceden=$total_exist geçersiz=$total_invalid" '{notes:$n}')" >/dev/null || true
echo "[lead] tamam: $total_created yeni aday onay bekliyor (CRM → Lead Adayları), $total_dup mükerrer, $total_exist önceden, $total_invalid geçersiz."
