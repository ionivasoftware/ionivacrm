# Liftdesk (Yeni Versiyon) — ION CRM SaaS Entegrasyon Sözleşmesi

Bu doküman **Liftdesk tarafını kuran ekip/ajanlar içindir**. ION CRM, EMS (eski versiyon) ile
kurulu bir SaaS entegrasyonuna sahiptir: müşteri senkronu, ödeme→fatura senkronu, süre uzatma,
SMS yükleme, kullanıcı listesi ve kullanım özeti. **Liftdesk aynı REST yüzeyini sunacak** ve CRM'e
EMS'in yanına ikinci bir SaaS kaynağı olarak bağlanacak.

> Bu sözleşme CRM'in **fiilen çalışan** EMS istemcisinden çıkarılmıştır
> (`SaasAClient.cs`, `SaasSyncJob.cs`, `SyncEmsPaymentsCommandHandler.cs`). Her alan/davranış
> kodda karşılığı olan gerçek gereksinimdir — tahmin yoktur.
>
> Not: Bu doküman **SaaS senkron entegrasyonudur**. Hata-triyaj onay ekranı entegrasyonu AYRI bir
> sözleşmedir: `docs/liftdesk-error-triage-contract.md`.

---

## 1. Genel mimari

- CRM multi-tenant'tır: her SaaS bir **Project** kaydına bağlanır (EMS → "EMS" projesi,
  Liftdesk → yeni "Liftdesk" projesi). Müşteriler, faturalar ve sync logları proje bazlıdır.
- Veri akışı üç kanaldır:
  1. **CRM → Liftdesk (pull, ana kanal):** 15 dk'da bir arka plan sync'i müşterileri ve son
     ödemeleri çeker. Ek olarak operatör aksiyonları (süre uzat, SMS yükle, kullanıcı/özet
     görüntüle) anlık çağrılardır.
  2. **Liftdesk → CRM (push, opsiyonel):** anlık müşteri güncellemesi webhook'u (§6.1).
  3. **CRM → Liftdesk (push, rezerve):** CRM olay callback'i (§6.2) — bugün pasif.
- CRM tarafındaki durum (Lead/Demo/Active/Passive/Churned) **Liftdesk'ten gelen
  `expirationDate`'ten türetilir** (§5). Liftdesk'in ayrıca "durum" göndermesi gerekmez;
  doğru `expirationDate` göndermesi kritiktir.

---

## 2. Kimlik doğrulama

- CRM'in yaptığı tüm çağrılarda statik Bearer key:
  ```
  Authorization: Bearer <LIFTDESK_CRM_API_KEY>
  ```
  Key, Liftdesk ile CRM arasında paylaşılan M2M anahtarıdır; CRM tarafında global config'te
  ve/veya proje kaydında tutulur, tarayıcıya asla inmez.
- Geçersiz/eksik key → **401** (gövdesinde kısa açıklayıcı metin olsun; CRM hata gövdelerini
  operatör mesajlarına taşır).
- Token exchange yoktur.

---

## 3. JSON kuralları

- Alan adları **camelCase**. (CRM parse'ı büyük/küçük harfe duyarsızdır ama camelCase standarttır.)
- Tarihler **ISO-8601 UTC** (`2026-07-19T14:12:00Z` veya offset'siz `2026-07-19T14:12:00` —
  CRM offset'siz değerleri UTC varsayar). **Epoch kullanmayın.**
- **DİKKAT — zarf yapısı endpoint-özeldir.** Bu API'de RezervAl/error-triage'daki gibi genel bir
  `{success, data, message, ...}` zarfı YOKTUR. Her endpoint'in kendi düz yanıt şeması vardır
  (aşağıda tek tek verildi).
- Hatalarda (4xx/5xx) gövdeye **kısa, anlamlı bir metin/JSON** koyun — CRM `HTTP {kod} {reason}:
  {gövde}` formatında yüzeye taşır.

---

## 4. Liftdesk'in sunması gereken endpoint'ler (CRM → Liftdesk)

### 4.1 Müşteri feed'i — ana sync kanalı ★

```
GET /api/v1/crm/customers?page={page}&pageSize={pageSize}
```

- CRM **15 dk'da bir tam tarama** yapar: `page=1`'den başlar, `pageSize=500` ile
  `page >= totalPages` veya boş sayfa gelene kadar ilerler. **Delta/updatedSince parametresi
  yoktur** — her turda tüm müşteriler döner (CRM, süresi geçenlerin durumunu veri değişmeden de
  yeniden hesaplamak için bilinçli olarak full sync yapar). Endpoint tekrarlı tam taramayı
  kaldıracak kadar hızlı olmalı; sayfalama stabil olmalı (sabit sıralama, ör. `id ASC`).

**Yanıt:**
```json
{
  "data": [
    {
      "id": "3",
      "name": "Asansör A.Ş.",
      "email": "info@asansor.com",
      "phone": "+90 555 111 22 33",
      "address": "…",
      "taxNumber": "1234567890",
      "segment": "sme",
      "createdAt": "2025-11-01T09:00:00Z",
      "updatedAt": "2026-07-18T12:30:00Z",
      "expirationDate": "2026-12-31T00:00:00Z"
    }
  ],
  "total": 142,
  "page": 1,
  "pageSize": 500,
  "totalPages": 1
}
```

| alan | tip | not |
|---|---|---|
| `id` | string | ★ Liftdesk firma kimliği. **Sayısal string** olmalı (`"3"`) — ödemelerdeki `companyId` (int) ile aynı kimlik uzayı; CRM müşteri↔ödeme eşleşmesi buna dayanır |
| `name` | string | ★ firma adı |
| `email`, `phone`, `address`, `taxNumber` | string? | iletişim/vergi bilgileri |
| `segment` | string? | serbest metin, CRM aynen saklar (ör. `enterprise`/`sme`/`individual`) |
| `createdAt` | datetime | ★ CRM'e kayıt tarihi olarak geçer ve **statü hesabına girer** (§5 40-gün kuralı) |
| `updatedAt` | datetime | CRM şu an saklamıyor; yine de gönderin (ileride delta için) |
| `expirationDate` | datetime? | ★★ lisans bitişi — CRM statüsü tamamen buna göre türetilir; null = Lead |
| `total`/`page`/`pageSize`/`totalPages` | int | ★ sayfalama zorunlu; `totalPages` doğru olmalı (CRM buna göre durur) |

- **Silme bayrağı yok:** Liftdesk'te silinen firma feed'den çıkar ama CRM'de kayıt kalır
  (güncellenmeyi bırakır; churn `expirationDate`'ten türer). CRM'de soft-delete edilmiş müşteri
  sync ile asla geri gelmez.

### 4.2 Son ödemeler — otomatik fatura taslağı ★

```
GET /api/v1/crm/payments/recent
```

- **Query parametresi YOKTUR.** Zaman penceresi **Liftdesk tarafında sabittir**: son **20
  dakikada** oluşmuş ve **tamamlanmış** (`completionPayment=true`) ödemeleri döndürün,
  `createdOn DESC` sıralı. (CRM 15 dk'da bir çağırır; 20 dk pencere + CRM tarafında `id` ile
  tekilleştirme sayesinde kaçırma/mükerrer olmaz. Pencereyi 20 dk'dan kısaltmayın.)

**Yanıt:**
```json
{
  "asOf": "2026-07-19T14:12:00Z",
  "windowMinutes": 20,
  "data": [
    {
      "id": 1041,
      "companyId": 3,
      "userId": 17,
      "paymentType": "CreditCard",
      "price": 1200.00,
      "subTotal": 1000.00,
      "vatPrice": 200.00,
      "installmentCount": 1,
      "conversationId": "abc-123",
      "completionPayment": true,
      "completionProcess": "success",
      "productId": 5,
      "productName": "1 Yıllık Üyelik",
      "createdOn": "2026-07-19T14:05:00Z"
    }
  ]
}
```

| alan | tip | not |
|---|---|---|
| `id` | int | ★ ödeme kimliği — CRM `Invoice.EmsPaymentId` ile **tekilleştirir** (aynı id ikinci kez fatura yaratmaz) |
| `companyId` | int | ★ 4.1'deki müşteri `id`'siyle aynı kimlik (int hâli). Müşteri CRM'de yoksa ödeme atlanır + Failed log |
| `price` / `subTotal` / `vatPrice` | decimal | brüt / net / KDV. CRM KDV oranını `round(vatPrice/subTotal*100)` ile türetir (subTotal≤0 ise %20 varsayar) |
| `paymentType` | string | fatura başlığında görünür: "EMS Ödeme - {paymentType} ({tarih})" |
| `productId` / `productName` | int? / string? | Paraşüt ürün eşlemesi `productId` üzerinden; yoksa açıklama `productName`'e düşer |
| `completionPayment` | bool | yalnız `true` olanları göndermeniz beklenir |
| `createdOn` | datetime | ★ fatura kesim/vade tarihi olur |
| `userId`, `installmentCount`, `conversationId`, `completionProcess` | — | log/iz için taşınır |

### 4.3 Süre uzatma (yazma) ★

```
POST /api/v1/crm/companies/{companyId}/extend-expiration
Content-Type: application/json

{ "durationType": "Months", "amount": 1 }
```

- `companyId` = int (4.1'deki `id`). `durationType` ∈ **`"Days" | "Months" | "Years"`**
  (değerler PascalCase, anahtarlar camelCase), `amount` = pozitif int.
- Liftdesk firmanın lisans bitişini uzatır ve **yeni bitiş tarihini** döner:

```json
{
  "companyId": 3,
  "expirationDate": "2027-12-31T00:00:00Z",
  "extended": { "durationType": "Months", "amount": 1 }
}
```

- **Boş gövdeli 2xx yasak** — CRM boş gövdeyi hata sayar. CRM bu yanıttaki `expirationDate`'i
  lokale yazar ve 1 Ay/1 Yıl uzatmalarda Paraşüt fatura taslağı oluşturur.

### 4.4 SMS kredisi yükleme (yazma)

```
POST /api/v1/crm/companies/{companyId}/add-sms

{ "count": 500 }
```

**Yanıt** (boş gövde yasak):
```json
{ "companyId": 3, "smsCount": 1250, "added": 500 }
```
`smsCount` = yükleme sonrası **toplam** bakiye.

### 4.5 Firma kullanıcıları

```
GET /api/v1/crm/companies/{companyId}/users
```

**Yanıt:**
```json
{
  "companyId": 3,
  "data": [
    { "userId": "17", "name": "Ali", "surname": "Yılmaz", "email": "ali@firma.com",
      "role": "Admin", "loginName": "ali", "password": "…" }
  ]
}
```

> ⚠️ **Güvenlik notu:** EMS bu endpoint'te `password` alanını düz metin döndürüyor ve CRM ekranı
> bunu SuperAdmin'e gösteriyor. Liftdesk'te parolalar hash'liyse (olmalı) bu alanı boş string
> döndürün ya da "ilk giriş şifresi" gibi ayrı bir alanla çözün — **alan şemada var olmalı**
> (CRM modeli bekliyor) ama gerçek parola olması gerekmiyor.

### 4.6 Firma kullanım özeti

```
GET /api/v1/crm/companies/{companyId}/summary
```

**Yanıt** (boş gövde yasak):
```json
{
  "companyId": 3,
  "totals": { "customerCount": 120, "elevatorCount": 340, "userCount": 8 },
  "monthly": [
    { "year": 2026, "month": 7, "maintenanceCount": 45, "faultCount": 12,
      "partChangeOfferCount": 3, "revisionOfferCount": 1, "assemblyOfferCount": 0 }
  ]
}
```

Alan adları EMS ile birebir aynı kalmalı (asansör alan modeli: bakım/arıza/teklif sayıları).
`monthly` son aylar listesi (EMS ~son 12 ay döndürüyor; aynısını önerin).

---

## 5. CRM statü türetmesi — `expirationDate` neden kritik

CRM her sync turunda müşteri durumunu şöyle hesaplar (bilgi amaçlı — Liftdesk'in tek görevi
doğru `expirationDate` + `createdAt` göndermek):

- `expirationDate` yok → **Lead**
- `kısaDeneme` = `createdAt + 40 gün > expirationDate` (oluşturmadan itibaren ≤40 gün lisans)
- süresi geçmemiş* + kısaDeneme → **Demo**; süresi geçmiş + kısaDeneme → **Passive**
- süresi geçmemiş + normal → **Active**; süresi geçmiş + normal → **Churned**

\* "geçmiş" = bugün (UTC, gün bazında) ≥ `expirationDate` günü — bitiş gününde müşteri geçmiş sayılır.

---

## 6. Opsiyonel kanallar

### 6.1 Liftdesk → CRM anlık müşteri webhook'u (push)

15 dk'lık sync yeterliyse bu kanalı hiç kurmayabilirsiniz. Anlık yansıma istenirse:

```
POST {CRM_BASE}/api/v1/sync/saas-a
X-Api-Key: <WEBHOOK_API_KEY>          ← CRM config'indeki WebhookApiKey ile birebir aynı
X-Project-Id: <CRM_PROJECT_GUID>      ← CRM'in Liftdesk projesi (CRM ekibi verir)
Content-Type: application/json
```

Gövde **düz yapıdadır** — müşteri alanları `eventType` ile aynı seviyededir, `data` altına
**yuvalanmaz**:

```json
{
  "eventType": "customer.updated",
  "entityType": "customer",
  "entityId": "3",
  "id": "3", "name": "Asansör A.Ş.", "email": "…", "phone": "…", "address": "…",
  "taxNumber": "…", "status": "active", "segment": "sme",
  "createdAt": "2025-11-01T09:00:00Z", "updatedAt": "2026-07-19T14:00:00Z"
}
```

- Yalnız `entityType="customer"` işlenir (diğerleri 200 dönüp no-op olur — göndermeyin).
- `status` metni: `active|lead|demo|trial|inactive|passive|churned` (webhook yolunda statü bu
  metinden eşlenir; sync yolundan farklı olarak `expirationDate` kullanılmaz).
- Yanıt: 200 `{"success":true,...}` zarfı; 401 kötü key; 400 proje çözülemedi/işleme hatası.
- HMAC yok; güvenlik X-Api-Key eşitliğidir. Endpoint'i yalnız sunucudan çağırın.
- ⚠️ Bu route (`/sync/saas-a`) bugün EMS içindir; Liftdesk ayrı kaynak olacağı için CRM tarafında
  Liftdesk'e özel route/config açılacak (bkz. §9) — webhook'u kurmadan önce CRM ekibiyle route'u
  netleştirin.

### 6.2 CRM → Liftdesk olay callback'i (rezerve — bugün PASİF)

CRM kod tabanında `POST {LIFTDESK_BASE}/api/v1/crm-callbacks` hedefine
`{eventType, entityType, entityId, projectId, data, occurredAt}` gönderen bir kanal tanımlıdır
(`eventType`: `subscription_extended | status_changed | customer_updated`) ama **bugün hiçbir
yerden tetiklenmiyor**. İlk sürümde implemente etmeniz gerekmez; path'i ileride kullanılmak üzere
rezerve edin.

---

## 7. Dayanıklılık ve trafik profili

- **Cadence:** otomatik sync 15 dk'da bir (startup +30 sn; PostgreSQL advisory lock ile tek
  instance); SuperAdmin ekrandan "Sync Başlat" ile de tetikleyebilir.
- **Müşteri feed'i yükü:** tur başına `ceil(N/500)` istek (N = toplam firma).
- **Retry:** CRM istemcisi ağ hatası/timeout'ta 3 kez exponential backoff (2s/4s/8s) dener; sync
  job katmanı da aşamayı 3 kez tekrarlar → başarısız bir çağrı **16 denemeye** kadar çıkabilir.
  Endpoint'ler **idempotent GET/deterministik POST** olmalı: aynı `extend-expiration` isteğinin
  retry'ı çifte uzatma yaratmamalı (öneri: kısa süreli idempotency — aynı gövde+firma birkaç sn
  içinde tekrar gelirse aynı yanıtı dönün).
- **Circuit breaker:** art arda 5 taşıma hatası **veya 5xx/408 yanıtı** devreyi 30 sn açar.
  İş hataları için 4xx kullanın; 5xx'i gerçek arızaya saklayın.
- **Timeout:** istek başına toplam 120 sn tavan; hedef yanıt < 2-3 sn (müşteri feed sayfası dahil).

---

## 8. Kabul testleri (curl)

```bash
B="https://<liftdesk-api>"; K="Authorization: Bearer $CRM_API_KEY"

curl -H "$K" "$B/api/v1/crm/customers?page=1&pageSize=500"
curl -H "$K" "$B/api/v1/crm/payments/recent"
curl -X POST -H "$K" -H "Content-Type: application/json" \
  -d '{"durationType":"Months","amount":1}' "$B/api/v1/crm/companies/3/extend-expiration"
curl -X POST -H "$K" -H "Content-Type: application/json" \
  -d '{"count":100}' "$B/api/v1/crm/companies/3/add-sms"
curl -H "$K" "$B/api/v1/crm/companies/3/users"
curl -H "$K" "$B/api/v1/crm/companies/3/summary"
# Negatifler: yanlış key → 401; bilinmeyen companyId → 404 + açıklayıcı gövde
```

**Kontrol listesi:**
- [ ] 6 endpoint yukarıdaki şemalarla birebir (alan adları camelCase, tarih ISO/UTC)
- [ ] `customers.id` sayısal string ve `payments.companyId` ile aynı kimlik uzayı
- [ ] `expirationDate` doğru ve güncel (statü tamamen buna bağlı), `createdAt` gerçek kayıt tarihi
- [ ] Sayfalama stabil, `totalPages` doğru, 500'lük sayfalar hızlı
- [ ] `payments/recent`: sabit 20 dk pencere, yalnız tamamlanmış ödemeler, `createdOn DESC`
- [ ] Yazma endpoint'lerinde boş gövdeli 2xx yok; retry'a dayanıklı (çifte uzatma/yükleme yok)
- [ ] 401/404/4xx gövdelerinde kısa açıklayıcı metin; 5xx yalnız gerçek arızada
- [ ] Bearer key üretildi ve CRM ekibine iletildi; dev+prod base URL'ler bildirildi

---

## 9. CRM tarafında yapılacaklar (bilgi — Liftdesk ekibinin işi değil)

Error-triage'ın aksine bu entegrasyon CRM'de **drop-in değildir**; Liftdesk API hazır olunca CRM'de
şunlar yapılacak (ayrı iş kalemi):

1. Yeni config bölümü (isim çakışması: mevcut `Liftdesk` bölümü error-triage'a ait —
   ör. `LiftdeskSaas:BaseUrl/ApiKey/WebhookApiKey/ProjectId` açılacak).
2. `Project` tablosuna `LiftdeskBaseUrl`/`LiftdeskApiKey` kolonları + "Liftdesk" proje kaydı +
   ProjectsAdminPage'deki `name === 'ems'` kapısının genişletilmesi.
3. `SyncSource` enum'una yeni üye + sync log ekranında etiket.
4. Sync job'a Liftdesk aşamaları (müşteri feed + ödemeler) — **LegacyId çakışma kararı:**
   EMS müşterileri düz sayısal LegacyId kullanıyor (`"3"`); Liftdesk kimlikleri de sayısal
   olacağından CRM tarafında `LIFT-{id}` gibi önekli format zorunlu (upsert LegacyId üzerinden,
   tenant filtresiz yapılıyor — öneksiz kimlikler EMS kayıtlarını ezer).
5. Süre uzat / SMS yükle / Kullanıcılar / Kullanım Özeti butonlarının Liftdesk müşterileri için
   açılması (bugün yalnız EMS LegacyId formatlarında görünüyor).
6. (Webhook istenirse) `/api/v1/sync/liftdesk` route'u + WebhookApiKey.

**Liftdesk ekibinden CRM ekibine teslim edilecekler:** dev + prod base URL, Bearer API key,
(webhook kurulacaksa) webhook gönderim IP'si/servisi bilgisi.

---

## 10. Açık kararlar (Liftdesk ekibi görüş bildirsin)

1. **Delta parametresi:** EMS'te yok (her tur full tarama). Firma sayısı büyükse
   `?updatedAfter=` desteği ekleyebilirsiniz — CRM bugün kullanmaz ama ileride geçilebilir.
2. **Silme bilgisi:** EMS feed'inde silinen firma sessizce kaybolur. `isDeleted` bayrağı
   eklemek isterseniz CRM tarafı ileride tüketebilir (şimdilik yok sayılır).
3. **`users.password`:** gerçek parola dönmeyin (bkz. §4.5) — boş string önerilir.
4. **Ödeme penceresi:** 20 dk sunucu tarafında sabit. Farklı bir değer isterseniz CRM ekibiyle
   birlikte kararlaştırın (CRM 15 dk'da bir çağırıyor; pencere < 15 dk olamaz).
