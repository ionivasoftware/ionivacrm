# CRM — Firma Paketi Görüntüleme ve Değiştirme API'si

CRM operatörünün bir firmanın (tenant) abonelik **paketini** görmesi ve
yükseltip düşürmesi için iki uç. Paket kademesi (Standart / Pro / Prime) EMS'te
**özellik kapılarını** (feature gating) belirler: değişiklik anında geçerli olur.

> Fiyat yönetimi ayrı bir API'dir → [crm-pricing-api.md](crm-pricing-api.md).
> Lisans **süresi** uzatma ayrı bir uçtur → `POST /companies/{id}/extend-expiration`
> ([liftdesk-saas-integration-contract.md](liftdesk-saas-integration-contract.md) §4.3).

---

## Bağlantı

| | |
|---|---|
| Base URL (prod) | `https://<ems-api-domain>/api/v1/crm` |
| Auth | `Authorization: Bearer <LIFTDESKSAAS_API_KEY>` |
| İçerik | `application/json` (UTF-8) |

- Key EMS tarafında Railway ortam değişkeni **`LIFTDESKSAAS__APIKEY`** ile tanımlıdır.
  Key yoksa `503`, yanlış key `401` döner.
- **Yanıtlar DÜZDÜR** — `{success, data, message}` zarfı **YOKTUR**. (Bu uçlar
  `CrmSaasController` ailesindendir; `pricing` uçlarından farklıdır.)
- `companyId` = `crmCompanyId` (sayısal), müşteri feed'indeki `customers.id` ile aynı.
- Tarihler ISO-8601 UTC.

---

## 1) GET /companies/{companyId}/plan — güncel paket

Ekranı tek çağrıyla kurar: firmanın mevcut paketi + seçilebilecek paketler.
Ayrıca `pricing` ucuna gitmenize gerek yok.

```bash
curl -H "Authorization: Bearer $LIFTDESKSAAS_API_KEY" \
  "$API/api/v1/crm/companies/7/plan"
```

```json
{
  "companyId": 7,
  "current": {
    "planId": "3f8a…",
    "name": "EMS Pro",
    "tier": "Pro",
    "status": "Active",
    "billingPeriod": "Monthly",
    "startDate": "2026-01-01T00:00:00Z",
    "endDate": "2026-12-31T00:00:00Z",
    "autoRenew": true
  },
  "availablePlans": [
    { "planId": "1a…", "name": "EMS Standart", "tier": "Standart", "priceMonthly": 500, "priceYearly": 5000 },
    { "planId": "3f8a…", "name": "EMS Pro", "tier": "Pro", "priceMonthly": 900, "priceYearly": 9000 },
    { "planId": "9c…", "name": "EMS Prime", "tier": "Prime", "priceMonthly": 1500, "priceYearly": 15000 }
  ],
  "warning": "Bu firmanın iyzico'da otomatik yenilenen aboneliği var. …"
}
```

| Alan | Anlamı |
|---|---|
| `current` | Firmanın en güncel aboneliği. **`null` olabilir** — abonelik kaydı hiç olmayan eski tenant. Ekran yine açılmalı, "paket tanımsız" gösterilmeli. |
| `current.status` | `Trialing` \| `Active` \| `PendingPayment` \| `Cancelled` \| `Expired` |
| `current.autoRenew` | iyzico otomatik tahsilat açık mı (tek seferlik ödemede `false`). |
| `availablePlans` | Yalnız satıştaki (`isActive`) paketler, kademe sırasına göre. |
| `warning` | Operatöre gösterilecek uyarı, yoksa `null`. Bkz. **iyzico uyarısı**. |

---

## 2) PUT /companies/{companyId}/plan — paketi değiştir

```bash
curl -X PUT -H "Authorization: Bearer $LIFTDESKSAAS_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"tier":"Prime"}' \
  "$API/api/v1/crm/companies/7/plan"
```

### Gövde

| Alan | Zorunlu | Değerler |
|---|---|---|
| `tier` | `tier` veya `planId`'den **biri** | `Standart` \| `Pro` \| `Prime` (paket adı da kabul edilir: `"EMS Pro"`) |
| `planId` | ” | `availablePlans[].planId`. **İkisi de gönderilirse `planId` kazanır.** |
| `billingPeriod` | hayır | `Monthly` \| `Yearly`. Verilmezse mevcut dönem korunur. |

Yanıt **GET ile birebir aynı gövdedir** — ekranı ayrıca tazelemenize gerek yok.

`PUT` olduğu için aynı isteği tekrarlamak güvenlidir (retry'da çift etki yok).

---

## Davranış — okumadan entegre etmeyin

**1. Paket değişikliği SÜREYİ ve DURUMU değiştirmez.**
`endDate`, `startDate` ve `status` aynen kalır. Süresi dolmuş (`Expired`) bir firmayı
Prime yapmak onu **çalışır hale getirmez** — sistem yine görüntüleme modundadır.
Önce/ayrıca `extend-expiration` çağırın.

**2. Değişiklik anında geçerlidir.**
Paket kademesi performans için 60 sn cache'lenir; bu uç cache'i **düşürür**, yani
firma bir sonraki isteğinde yeni özellikleri görür. "Neden değişmedi" beklemesi yok.

**3. iyzico uyarısı — gelir kaybı riski.**
Firma iyzico'da otomatik yenilenen bir aboneliğe sahipse (`autoRenew: true` +
iyzico referansı), paket değişikliği **yalnız EMS tarafını** etkiler. iyzico **eski
tutarı çekmeye devam eder**. Bu durumda yanıtta `warning` dolu gelir — operatöre
mutlaka gösterin ve iyzico tarafındaki aboneliği elle güncelleyin.

**4. Paket düşürmek veri silmez, kullanıcı kilitlemez.**
Kademe yalnız özellik kapılarını kapatır. `maxUsers` / `maxElevators` alanları
şu an **hiçbir yerde zorlanmıyor** — 50 kullanıcılı firma Standart'a düşerse
kullanıcılar silinmez, kimse dışarı atılmaz. Bu alanları CRM'de yalnız bilgi
amaçlı gösterin, "limit aşıldı" mantığı kurmayın.

**5. Kademeler kümülatiftir.** Prime = Pro + Prime modülleri, Pro = Standart + Pro
modülleri. Düşürme, üst kademeye ait ekranları kapatır.

---

## Hata kodları

| Kod | Ne zaman |
|---|---|
| `400` | `tier`/`planId` ikisi de boş; geçersiz `tier`; geçersiz `planId` (guid değil); geçersiz `billingPeriod` |
| `401` | Key yanlış/eksik |
| `404` | `companyId` bulunamadı; `planId` böyle bir plan yok |
| `409` | Firmanın abonelik kaydı yok → önce `extend-expiration` ile süre tanımlayın |
| `503` | EMS'te `LIFTDESKSAAS__APIKEY` tanımlı değil |

Hata gövdesi `GlobalExceptionMiddleware` formatındadır; `message` alanını operatöre
gösterebilirsiniz.

---

## CRM tarafında önerilen akış

1. Firma detayında **GET .../plan** → mevcut paketi ve `availablePlans`'ı göster.
2. Operatör yeni paketi seçer → **PUT .../plan** `{"tier":"Prime"}`.
3. Yanıttaki `warning` doluysa modalda göster: *"iyzico aboneliğini elle güncelleyin."*
4. Paket değişikliği süreyi uzatmaz — gerekiyorsa aynı ekrandan `extend-expiration`
   çağrısını ayrıca sunun.
5. `current` `null` geldiyse "paket tanımsız" göster ve değiştirme butonunu, süre
   tanımlanana kadar pasif tut (PUT `409` dönecektir).
