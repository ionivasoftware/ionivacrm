# Liftdesk — Checklist & Pricing uçlarında 401 (CRM bug raporu)

**Belirti (CRM operatörü):** Müşteri detayında **Checklists** sekmesi ve **Fiyat Yönetimi** ekranı
şu hatayı veriyor:

```
Liftdesk API anahtarı geçersiz veya eksik (401).
```

Aynı anahtarla **müşteri senkronu çalışıyor** — yani anahtar doğru, dönmedi. Sorun EMS tarafında
iki controller'ın **yanlış config anahtarına** doğrulanması.

---

## Kök neden

`28dac29d` — *"fix(crm): CRM API ile Liftdesk SaaS API'yi tekrar AYRI anahtara ayır"* (22 Tem 15:03)
commit'i iki M2M anahtarını ayırdı:

| Attribute | Config anahtarı | Env | Kullanan yüzeyler |
|---|---|---|---|
| `[CrmApiKey]` (çıplak — **varsayılan `Crm:ApiKey`**) | `Crm:ApiKey` | `CRM__APIKEY` | error-triage, ticket, support-chat, triage/fixer GitHub ajanları |
| `[CrmApiKey("LiftdeskSaas:ApiKey")]` | `LiftdeskSaas:ApiKey` | `LIFTDESKSAAS__APIKEY` | SaaS senkron kanalı (`CrmSaasController`) |

Commit `CrmSaasController`'ı SaaS anahtarına taşıdı, ama **aynı kanalın diğer iki controller'ını
atladı**: `CrmChecklistController` ve `CrmPricingController` çıplak `[CrmApiKey]`'de kaldı → artık
`CRM__APIKEY` bekliyorlar.

CRM tarafında bu iki yüzey **per-proje** kimlik bilgileriyle çağrılıyor
(`project.LiftdeskBaseUrl` + `project.LiftdeskApiKey` — senkronun kullandığı **aynı** alanlar,
yani `LIFTDESKSAAS__APIKEY`). Beklenen ≠ gönderilen → **401**.

Doğrulayan noktalar:
- `CrmApiKeyAttribute` ctor varsayılanı gerçekte `"Crm:ApiKey"` (sınıfın XML yorumu "LiftdeskSaas"
  diyor — yorum eskimiş, kod öyle değil).
- Her iki controller'ın kendi başlık yorumu zaten *"config LiftdeskSaas:ApiKey / env
  LIFTDESKSAAS__APIKEY"* diyor → **niyet** SaaS anahtarıydı; bölünme güncellemeyi atlamış.
- Bölünme öncesi checklist reset çağrısı auth'u **geçiyordu** (o dönemde ayrı bir 500 alınmıştı) —
  yani anahtar değişmedi, endpoint'in beklediği anahtar değişti.

---

## Düzeltme (2 satır)

`src/Ems.API/Controllers/` altında:

**1) `CrmChecklistController.cs`**
```diff
- [CrmApiKey]
+ [CrmApiKey("LiftdeskSaas:ApiKey")]
```

**2) `CrmPricingController.cs`**
```diff
- [CrmApiKey]
+ [CrmApiKey("LiftdeskSaas:ApiKey")]
```

Gerekçe: ikisi de per-proje Liftdesk SaaS yüzeyidir (CRM'de `project.LiftdeskApiKey`'den çözülür,
tıpkı `CrmSaasController` gibi) → aynı `LIFTDESKSAAS__APIKEY`'e doğrulanmalıdır.

### Dokunulmayacaklar
- `CrmErrorController`, `CrmTicketsController`, `CrmSupportChatController` → çıplak `[CrmApiKey]`
  (`Crm:ApiKey`) **doğru**, aynen kalsın. CRM bunlara global `Liftdesk:ApiKey` (= `CRM__APIKEY`)
  gönderiyor; triage/fixer GitHub Actions ajanları da `CRM_API_KEY` secret'ını kullanıyor.
- Env değişkenleri değişmiyor — **rotate gerekmez**.
- (Opsiyonel kozmetik) `CrmApiKeyAttribute` XML yorumundaki "varsayılan LiftdeskSaas:ApiKey" ifadesi
  eskimiş; gerçek varsayılan `Crm:ApiKey`.

---

## Doğrulama

1. `dotnet test` — `CrmSaasEndpointTests` deseni `LiftdeskSaas__ApiKey` set ediyor; checklist/pricing
   endpoint testleri varsa aynı şekilde yeşil kalmalı.
2. Push → Railway `ems-api-development` auto-deploy.
3. CRM'de doğrula:
   - `LIFT-` LegacyId'li bir müşteri → **Checklists** sekmesi → bakım/arıza listesi 200 dönmeli;
     "Varsayılana Döndür" çalışmalı.
   - **Fiyat Yönetimi** ekranı → planlar + SMS paketleri yüklenmeli.
4. Negatif kontrol: yanlış key ile `curl` → yine 401 (mesaj gövdesi korunur).

```bash
B="https://ems-api-development.up.railway.app"
curl -s -o /dev/null -w "%{http_code}\n" \
  -H "Authorization: Bearer $LIFTDESKSAAS_APIKEY" \
  "$B/api/v1/crm/companies/7/maintenance-checklist"   # düzeltmeden sonra: 200 (veya firma yoksa 404)
```
