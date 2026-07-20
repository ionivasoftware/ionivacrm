# Liftdesk (Yeni Versiyon) — CRM Hata Triyaj Entegrasyon Sözleşmesi

Bu doküman **Liftdesk tarafını kuran ekip/ajanlar içindir**. ION CRM'in SuperAdmin "Hata Onay
Ekranı" şu anda EMS'in error-triage API'sini tüketiyor; Liftdesk (yeni versiyon) **birebir aynı
REST sözleşmesini** sunduğunda CRM tarafında **hiçbir kod değişikliği gerekmez** — sadece iki env
değişkeni güncellenir (bkz. §10).

Boru hattı (EMS ile aynı):
`hata → AI triyaj (Triaged) → **CRM onayı (Approved/Rejected)** → AI fix (Fixing → Fixed/Failed, PR) → insan merge`

Hata yakalama, AI triyaj ve fix ajanları Liftdesk tarafındadır. CRM yalnız aşağıdaki iki endpoint'i
tüketir: kartları listeler ve onay/ret kararını yazar.

> Bu sözleşme CRM'in **fiilen implemente edilmiş** istemcisinden çıkarılmıştır
> (`src/IonCrm.Infrastructure/ExternalApis/LiftdeskClient.cs` + `ErrorTriageController.cs`).
> Buradaki her alan/davranış CRM kodunda karşılığı olan gerçek gereksinimdir.

---

## 1. Kimlik doğrulama

- **Auth:** her istekte statik Bearer key (M2M, sunucu-sunucu):
  ```
  Authorization: Bearer <CRM_API_KEY>
  ```
- Key, Liftdesk ile CRM arasında paylaşılan tek ortak anahtardır (EMS'teki Railway `CRM__APIKEY`
  muadili). CRM bu değeri kendi sunucusunda `Liftdesk__ApiKey` env'inde tutar; **tarayıcıya asla
  inmez** — tüm çağrılar CRM backend'inden gelir.
- **Key yanlış/eksik → 401**, **Liftdesk tarafında key tanımsız → 503** dönmelidir
  (CRM bu iki kodu özel Türkçe mesaja çevirir).
- Token exchange YOK (RezervAl'den farklı) — key doğrudan Bearer olarak kullanılır.

---

## 2. Yanıt zarfı — HER yanıt için zorunlu

Tüm yanıtlar (2xx **ve** 4xx/5xx dahil) şu JSON zarfıyla dönmelidir:

```json
{ "success": true, "data": <T>, "message": "…|null", "errors": null, "statusCode": 200 }
```

Sayfalı listelerde `data`:

```json
{ "items": [ … ], "totalCount": 42, "page": 1, "pageSize": 50,
  "totalPages": 1, "hasPreviousPage": false, "hasNextPage": false }
```

Kurallar:
- Alan adları **camelCase**. Tarihler **UTC ISO-8601** (`2026-07-19T14:12:00Z`). Id'ler **GUID** string.
- **Hata durumlarında da zarf dönün** ve `message`'ı doldurun — CRM bu mesajı operatöre aynen
  gösterir (Türkçe yazılması önerilir). Gövdesi dolu ama JSON-parse-edilemeyen yanıt (ör. bir
  proxy'nin HTML hata sayfası) CRM'de jenerik "Liftdesk yanıtı çözümlenemedi (HTTP …)" uyarısına
  düşer ve o durumda §6'daki 401/503 özel mesajları da ÜRETİLMEZ — ara katman/reverse-proxy'nin
  de JSON zarf döndüğünden emin olun.
- **GET 2xx her zaman dolu `data` nesnesi içermeli** — sonuç yoksa bile `data.items: []` dönün.
  Tamamen boş gövdeli ya da `data: null` olan bir 200, CRM'de uyarısız şekilde "boş kuyruk"
  olarak görünür ve gerçek boş listeden ayırt edilemez; bu yüzden boş gövdeli 200 yasak.
- `message` null olduğunda CRM `errors[0]`'a bakmaz (CRM→UI katmanında bakar; Liftdesk→CRM
  katmanında yalnız `message` okunur). **Operatörün görmesi gereken metni `message`'a koyun.**

---

## 3. CRM'in çağırdığı endpoint'ler

### 3.1 Triyaj kartlarını listele

```
GET /api/v1/crm/error-analyses?page={page}&pageSize={pageSize}&status={status}
```

- `status` opsiyonel; boşsa **tüm durumlar** döner. Geçerli değerler:
  `Triaged` | `Approved` | `Rejected` | `Fixing` | `Fixed` | `Failed`.
- `page` ≥ 1, `pageSize` CRM'den her zaman **50** gelir (kontratta 1-200 aralığını kabul edin).
- Query parametre sırası değişebilir (`page` ve `pageSize` `status`'tan önce gelebilir).
- `data` = yukarıdaki sayfalı zarf, `items` = `ErrorAnalysisDto[]` (bkz. §4).
- Sıralama önerisi: `createdAt DESC` (CRM kendi tarafında severity/occurrence/tarih ile yeniden
  sıralar, ama sayfa 1'de en güncel kartların olması beklenir).

### 3.2 Onayla / Reddet (birleşik durum geçişi)

```
PATCH /api/v1/crm/error-analyses/{id}/status
Content-Type: application/json
```

CRM **tam olarak** şu iki gövdeden birini gönderir (camelCase):

**Onay:**
```json
{ "status": "Approved", "approvedBy": "ofcakmakci@gmail.com" }
```
**Ret:**
```json
{ "status": "Rejected", "rejectReason": "Gürültü, gerçek bug değil" }
```

- `approvedBy` **serbest metin tanımlayıcıdır** — normalde CRM JWT'sinden türetilen SuperAdmin
  e-postası, ama e-posta claim'i boşsa literal **`"crm"`** gelir. **E-posta formatı valide
  etmeyin**, kullanıcı tablosuna FK bağlamayın; string olarak saklayın.
- `rejectReason` CRM'den **her zaman dolu gelir** (operatör boş bırakırsa CRM
  `"CRM üzerinden reddedildi."` gönderir). Kaydedin ve liste yanıtında geri döndürün.
- CRM yalnız `Approved` ve `Rejected` gönderir. `Fixing/Fixed/Failed` fix ajanına aittir —
  CRM'den gelirse **400** dönün.
- Yalnız **`Triaged`** durumundaki kart geçiş yapabilir; diğer durumlarda **409** (zarflı, mesajlı).
- Bilinmeyen id → **404**. `{id}` GUID formatındadır (CRM göndermeden önce Guid.Parse eder).
- Başarıda **200** + `data` = güncel `ErrorAnalysisDto`. **`data` DOLU olmak zorundadır:**
  204 No Content ya da `data: null` dönen bir 2xx, CRM tarafından **başarısızlık** sayılır ve
  operatöre "İşlem Liftdesk tarafından reddedildi." gösterilir — geçiş Liftdesk'te kaydedilmiş
  olsa bile. PATCH'ten daima güncellenmiş kartın tamamını döndürün.

### 3.3 Ham hata detayı (opsiyonel)

```
GET /api/v1/crm/client-errors/{clientErrorId}
```

EMS sözleşmesinde vardı; **CRM şu an bu endpoint'i ÇAĞIRMIYOR** (liste yanıtındaki gömülü
`clientError` yeterli). İleride tam-stack görünümü için isteğe bağlı; ilk sürümde atlanabilir.

---

## 4. Veri modeli

### ErrorAnalysisDto (triyaj kartı)

"CRM kullanımı" kolonu, alanın ekranda **nerede göründüğünü** söyler — ★ işaretli alanlar boş
kalırsa kart operatör için anlamsızlaşır.

| alan | tip | CRM kullanımı |
|---|---|---|
| `id` | guid | ★ kart kimliği; PATCH URL'inde kullanılır; UI'da ilk 8 hane gösterilir |
| `clientErrorId` | guid | fingerprint olarak saklanır (gösterilmez) |
| `explanation` | string? | şu an gösterilmiyor (ileride detay için) |
| `rootCause` | string? | ★ "Kök Neden" bölümü |
| `suggestedFix` | string? | ★ "Önerilen Çözüm" bölümü |
| `sourceFile` | string? | kod yolu satırı, ör. `src/…/Handler.cs:64` |
| `severity` | string | ★ `Low` \| `Medium` \| `High` \| `Critical` — rozet + sıralama (başka değer gri rozet olur) |
| `status` | string | ★ `Triaged` \| `Approved` \| `Rejected` \| `Fixing` \| `Fixed` \| `Failed` |
| `approvedBy` | string? | geçmiş sekmelerinde "İşleyen: …" |
| `approvedOn` | datetime? | işlem tarihi (3. öncelik, bkz. not) |
| `reviewedAt` | datetime? | işlem tarihi (2. öncelik) |
| `rejectReason` | string? | "Ret gerekçesi: …" (Reddedilen sekmesi) |
| `fixBranch` | string? | gösterilmiyor (opsiyonel) |
| `fixCommitSha` | string? | gösterilmiyor (opsiyonel) |
| `fixPrUrl` | string? | ★ (Fixed kartlarda) "PR'ı İncele" linki — mutlaka doldurun |
| `failReason` | string? | "Fix başarısız: …" satırı (Failed kartlarda) |
| `fixedAt` | datetime? | işlem tarihi (1. öncelik) |
| `occurrenceCount` | int | "N× tekrar" rozeti — `max(analysis, clientError.occurrenceCount)` alınır |
| `createdAt` | datetime | ★ kart tarihi **ve** operatörün tarih filtresi buna göre çalışır |
| `clientError` | ClientErrorDto | ★ gömülü hata özeti — listede MUTLAKA dolu gelmeli |

> İşlem tarihi: CRM `fixedAt ?? reviewedAt ?? approvedOn` sırasıyla ilk dolu olanı gösterir.

### ClientErrorDto (gömülü hata)

| alan | tip | CRM kullanımı |
|---|---|---|
| `id` | guid | — |
| `source` | string | ★ uygulama rozeti (ör. `Backend`, `Frontend`, `MobileStaff`…) — kısa tutun, UI'da aynen basılır |
| `errorType` | string | ★ kart başlığı (ör. `NullReferenceException`, `TypeError`) |
| `message` | string | ★ kısa mesaj (PII-scrub'lı) |
| `details` | string? | stack/detay (PII-scrub'lı) |
| `contextJson` | string? | gösterilmiyor (ileride detay için) |
| `occurrenceCount` | int | tekrar rozetine katkı (üstteki max) |
| `firstSeenAt` / `lastSeenAt` | datetime | şu an gösterilmiyor |
| `status` | string | `New` \| `Analyzed` \| `Ignored` — gösterilmiyor |

> Exception bloğu: CRM `details` doluysa onu, yoksa `message`'ı basar; `message` `details` içinde
> geçmiyorsa ikisini alt alta birleştirir. Yani stack'in ilk satırında mesaj varsa çift göstermez.

---

## 5. Durum makinesi — kim neyi değiştirir

```
              ┌─CRM──▶ Approved ──fix ajanı──▶ Fixing ──▶ Fixed  (fixPrUrl doldur)
Triaged ──────┤                                   └─────▶ Failed (failReason doldur)
              └─CRM──▶ Rejected
```

- CRM **yalnız `Triaged` kartı** `Approved`/`Rejected` yapar.
- Diğer tüm geçişler Liftdesk iç ajanlarının işidir; CRM bu durumları sadece **okur**.
- CRM ekranı 4 sekmedir ve Liftdesk durumlarını şöyle katlar (bilgi amaçlı):
  Bekleyen=`Triaged` · Onaylanan=`Approved`+`Fixing` · Reddedilen=`Rejected` · Tamamlanan=`Fixed`+`Failed`.

---

## 6. Hata kodları

| kod | anlam | zarf? | CRM davranışı |
|---|---|---|---|
| 200 | başarılı | ✔ | — |
| 400 | geçersiz `status`/gövde | ✔ `message` dolu | mesajı operatöre gösterir |
| 401 | key yanlış/eksik | ✔ önerilir | "Liftdesk API anahtarı geçersiz…" uyarısı |
| 404 | kart yok | ✔ `message` dolu | mesajı gösterir; sonraki poll listeyi tazeler |
| 409 | geçersiz geçiş (zaten Approved vb.) | ✔ `message` dolu | mesajı toast'ta gösterir |
| 503 | Liftdesk'te CRM key tanımsız | ✔ önerilir | "Liftdesk (EMS) tarafında CRM anahtarı tanımlı değil" uyarısı |

Zarf dönülemeyen durumlarda öncelik şöyledir: gövde **tamamen boşsa** CRM HTTP koduna göre
yukarıdaki jenerik mesajları üretir; gövde **dolu ama JSON değilse** operatör yalnız
"Liftdesk yanıtı çözümlenemedi (HTTP …)" görür (401/503 özel mesajları üretilmez). Her durumda
zarf+`message` dönmek en iyisidir.

---

## 7. CRM'in çağrı düzeni (trafik profili)

Liftdesk tarafı performans/rate-limit planı için bilsin:

- Ekran açıkken **her 60 sn'de bir** poll; ayrıca operatör "Yenile"ye basabilir.
- Her poll'da aktif sekmeye göre:
  - Bekleyen/Reddedilen: **1** GET (`status=Triaged` / `Rejected`)
  - Onaylanan: **2 ardışık** GET (`status=Approved`, sonra `status=Fixing`)
  - Tamamlanan: **2 ardışık** GET (`status=Fixed`, sonra `status=Failed`)
- Her zaman `page=1&pageSize=50`.
- CRM istek başına **60 sn timeout** uygular. **Devre kesici:** art arda **5 "geçici hata"** —
  ağ hatası **veya HTTP 5xx/408 yanıtı (zarflı olsa bile)** — kesiciyi **30 sn** açar; o pencerede
  Liftdesk kartları görünmez ve operatör jenerik "Liftdesk geçici olarak devre dışı" mesajını
  görür (Rezerval etkilenmez). Onaylanan/Tamamlanan sekmeleri poll başına 2 istek attığı için
  sayaç hızlı dolabilir. **Bu yüzden iş hataları için tanımlı 4xx kodlarını kullanın (bunlar
  kesiciyi tetiklemez); 5xx'i yalnız gerçek servis arızasında dönün.** Sürekli 503 dönen
  "key tanımsız" yapılandırma durumunda kesicinin açılıp kapanması beklenen davranıştır.
  Hedef yanıt süresi **< 2-3 sn**.
- Onay/ret anında ekstra 1 PATCH + ardından liste yenilenir.
- CRM iki ardışık status çağrısı arasında durum değişen kartı `(source,id)` ile tekilleştirir —
  Liftdesk tarafında ek önlem gerekmez.

---

## 8. curl örnekleri (kabul testi)

```bash
# Liste — onay bekleyen
curl -H "Authorization: Bearer $CRM_API_KEY" \
  "https://<liftdesk-api>/api/v1/crm/error-analyses?page=1&pageSize=50&status=Triaged"

# Onayla
curl -X PATCH -H "Authorization: Bearer $CRM_API_KEY" -H "Content-Type: application/json" \
  -d '{"status":"Approved","approvedBy":"ofcakmakci@gmail.com"}' \
  "https://<liftdesk-api>/api/v1/crm/error-analyses/<GUID>/status"

# Reddet
curl -X PATCH -H "Authorization: Bearer $CRM_API_KEY" -H "Content-Type: application/json" \
  -d '{"status":"Rejected","rejectReason":"Gürültü, gerçek bug değil"}' \
  "https://<liftdesk-api>/api/v1/crm/error-analyses/<GUID>/status"

# 409 senaryosu — aynı kartı ikinci kez onayla (zarflı 409 dönmeli)
```

**Örnek liste yanıtı:**
```json
{
  "success": true,
  "data": {
    "items": [{
      "id": "3f2b1c9e-8d4a-4f6b-9a2e-1c5d7e9f0a3b",
      "clientErrorId": "9a1c2d3e-4f5a-6b7c-8d9e-0f1a2b3c4d5e",
      "explanation": null,
      "rootCause": "GetById tenant filtresi uygulanmadan çağrılıyor…",
      "suggestedFix": "FirstOrDefaultAsync + query filter kullan…",
      "sourceFile": "src/Liftdesk.Application/Elevators/…/Handler.cs:64",
      "severity": "High",
      "status": "Triaged",
      "approvedBy": null, "approvedOn": null, "reviewedAt": null, "rejectReason": null,
      "fixBranch": null, "fixCommitSha": null, "fixPrUrl": null, "failReason": null, "fixedAt": null,
      "occurrenceCount": 12,
      "createdAt": "2026-07-19T14:00:00Z",
      "clientError": {
        "id": "9a1c2d3e-4f5a-6b7c-8d9e-0f1a2b3c4d5e",
        "source": "Backend",
        "errorType": "NullReferenceException",
        "message": "Object reference not set to an instance of an object.",
        "details": "System.NullReferenceException: Object reference not set…\n   at Liftdesk.Application…",
        "contextJson": "{\"path\":\"/api/v1/elevators/42\",\"method\":\"GET\",\"statusCode\":500}",
        "occurrenceCount": 12,
        "firstSeenAt": "2026-07-18T09:12:00Z",
        "lastSeenAt": "2026-07-19T13:55:00Z",
        "status": "Analyzed"
      }
    }],
    "totalCount": 1, "page": 1, "pageSize": 50, "totalPages": 1,
    "hasPreviousPage": false, "hasNextPage": false
  },
  "message": null, "errors": null, "statusCode": 200
}
```

---

## 9. Liftdesk tarafının sağlaması gerekenler — özet kontrol listesi

- [ ] `GET /api/v1/crm/error-analyses` — zarflı, sayfalı, `status` filtreli (6 durum + boş=hepsi)
- [ ] `PATCH /api/v1/crm/error-analyses/{id}/status` — yalnız Triaged→Approved/Rejected; 400/404/409 zarflı
- [ ] Statik Bearer key doğrulama (env: `CRM__APIKEY` muadili) — 401 / 503 semantiği
- [ ] camelCase JSON + UTC ISO-8601 tarih + GUID id
- [ ] Hata yanıtlarında da zarf + operatöre gösterilebilir `message` (proxy katmanı dahil)
- [ ] GET 2xx daima dolu `data` (`items: []` dahil) — boş gövdeli 200 yok
- [ ] PATCH 2xx daima güncellenmiş kartın tamamı `data`'da — 204/`data:null` yok
- [ ] `approvedBy` serbest string kabul (e-posta formatı zorunlu değil; `"crm"` gelebilir)
- [ ] `clientError` liste yanıtında gömülü ve dolu (source, errorType, message, details)
- [ ] `rejectReason` kalıcı; `approvedBy`/`reviewedAt` kaydı
- [ ] Fix ajanı `Fixing/Fixed/Failed` geçişlerinde `fixPrUrl` / `failReason` / `fixedAt` doldurur
- [ ] `message`/`details` PII-scrub'lı
- [ ] Dev + prod base URL'lerinin CRM ekibine bildirilmesi

**CRM'in yapmayacakları** (Liftdesk bunlara endpoint açmak zorunda değil): hata yakalama/ingest,
triyaj yazma (`POST error-analyses`), `Fixing/Fixed/Failed` geçişleri, `client-errors` listesi.

---

## 10. CRM tarafında yapılacak tek şey

Liftdesk yukarıdaki sözleşmeyi sunduğunda CRM'de kod değişikliği yok; Railway prod'da:

```
Liftdesk__BaseUrl = https://<liftdesk-prod-api>     (şu an: https://ems-api-development.up.railway.app)
Liftdesk__ApiKey  = <paylaşılan CRM API key>
```

**URL birleştirme kuralı:** CRM path'i BaseUrl'e **aynen ekler** —
`{BaseUrl}/api/v1/crm/error-analyses…` (BaseUrl sonundaki `/` temizlenir). Endpoint'ler
**tam olarak** bu path'te yaşamalıdır; farklı bir prefix (ör. `/crm/v1/…`) çalışmaz. BaseUrl
path içerebilir (`https://host/liftdesk` → `https://host/liftdesk/api/v1/crm/…`); sürüm/path
müzakeresi yoktur.

Kartlar CRM Hata Onay Ekranı'nda **Liftdesk** rozetiyle, Rezerval kartlarıyla yan yana görünür.

> Not: EMS (eski) ve Liftdesk (yeni) bir süre **aynı anda** ayrı kaynaklar olarak yaşayacaksa,
> CRM'de üçüncü bir kaynak yuvası (ayrı rozet + config) eklemek gerekir — küçük bir iş, istenirse
> yapılır. Tek başına geçişte (URL değişimi) hiçbir şey gerekmez.
