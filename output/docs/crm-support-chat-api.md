# CRM Destek Asistanı Sohbet Logları API'si

Uygulama içi **Destek Asistanı** (Claude Haiku + yardım dokümanları) ile kullanıcılar
arasındaki sohbetlerin salt-okunur kaydı. Amaç: destek ekibinin (CRM) müşterilerin neyi
sorduğunu, asistanın ne yanıtladığını görmesi — eksik/yanlış yanıtları ve sık sorulanları
tespit etmek.

```
Liftdesk portal → Destek Asistanı widget'ı (sağ alt köşe)
   → POST /api/v1/support/chat (JWT)  → Claude yanıtı
   → her yanıttan sonra son soru + yanıt → SupportChatLog (en iyi çaba)
   → 10 gün saklanır → SupportChatLogCleanupJob kalıcı siler
   → CRM:  GET /api/v1/crm/support-chat-logs (ApiKey) → listeler
```

Bu sohbet loglarının [ticket sistemiyle](crm-ticket-api.md) ilgisi YOKTUR: ticket'lar
kullanıcının bilinçli açtığı geri bildirim kayıtlarıdır; bunlar ise asistanla yapılan
sohbetin pasif logudur (durum makinesi, onay/ret, ajan yok — yalnız okuma).

---

## Auth

Tek kanal — **CRM (makine)**: `api/v1/crm/support-chat-logs`

- `[CrmApiKey]`, header `Authorization: Bearer <CRM_API_KEY>`
- Key kaynağı: config `LiftdeskSaas:ApiKey` / Railway `LIFTDESKSAAS__APIKEY` — hata
  telemetrisi ve ticket hattıyla **AYNI** paylaşımlı key; yeni secret **GEREKMEZ**.
- Key yapılandırılmamışsa **503**, yanlış/eksikse **401**.
- Cross-tenant çalışır (IgnoreQueryFilters) — tüm tenant'ların logları tek listede döner.

Yanıt standart `ApiResponse<T>` zarfı: `{ success, data, message, errors, statusCode }`.

**Tenant kullanıcısına GÖSTERİLMEZ** — sohbet loglarını okuyan hiçbir tenant (JWT) ucu
yoktur; yalnız CRM görür.

---

## Saklama (retention) — 10 gün

- Her satır `SupportChatLog.RetentionDays` = **10 gün** tutulur.
- `SupportChatLogCleanupJob` (Hangfire, **her gün 04:30 UTC**) `CreatedAt` 10 günden eski
  satırları **kalıcı siler** (soft-delete değil — geri gelmez).
- Sonuç: liste en fazla son 10 günü kapsar. CRM'de daha uzun arşiv gerekiyorsa loglar
  düzenli çekilip CRM tarafında saklanmalıdır (bu API kalıcı arşiv değildir).

---

## Kayıt modeli

Sohbet **stateless**'tir: istemci her istekte tüm geçmişi gönderir, ama loglanan yalnız o
isteğin **son kullanıcı sorusu + asistanın yanıtı**dır (bir "tur" = bir satır). Böylece
geçmiş tekrar tekrar loglanmaz.

- Bir kullanıcının tam konuşmasını yeniden kurmak için: aynı `projectId` + `userId`
  satırlarını `createdAt` artan sırada diz.
- Loglama **en iyi çaba** ile yapılır — DB hatası olsa bile kullanıcının sohbet yanıtı
  döner, log atlanır (yanıt asla bozulmaz).
- Proje bağlamı olmayan kullanıcı (ör. SuperAdmin) **loglanmaz** (Project FK zorunlu).

---

## GET /api/v1/crm/support-chat-logs

Sayfalı, cross-tenant sohbet logu listesi — **en yeni önce** (`createdAt` desc).

| Query param | Tip | Açıklama |
|---|---|---|
| `projectId` | Guid? | Belirli bir tenant ile sınırla |
| `search` | string? | `question` + `answer` + `userEmail` içinde geçen (küçük-harf ILIKE) |
| `startDate` | DateTime? | `createdAt >= startDate` (ISO 8601, UTC) |
| `endDate` | DateTime? | `createdAt <= endDate` (ISO 8601, UTC) |
| `page` | int | Varsayılan `1` |
| `pageSize` | int | Varsayılan `20` |

`data`: `PaginatedResult<SupportChatLogDto>` (`items`, `totalCount`, `page`, `pageSize`,
`totalPages`, `hasPreviousPage`, `hasNextPage`).

```bash
# Son sohbetler (tüm tenant'lar)
curl -H "Authorization: Bearer $CRM_API_KEY" \
  "$API/api/v1/crm/support-chat-logs?page=1&pageSize=20"

# Belirli tenant + kelime araması
curl -H "Authorization: Bearer $CRM_API_KEY" \
  "$API/api/v1/crm/support-chat-logs?projectId=$PID&search=bakım"

# Tarih aralığı (30 Haziran tüm günü dahil etmek için ertesi gün 00:00 kullan)
curl -H "Authorization: Bearer $CRM_API_KEY" \
  "$API/api/v1/crm/support-chat-logs?startDate=2026-06-29T00:00:00Z&endDate=2026-06-30T00:00:00Z"
```

Örnek yanıt:

```json
{
  "success": true,
  "data": {
    "items": [
      {
        "id": "7f2c…",
        "projectId": "a91b…",
        "projectName": "Yıldız Asansör",
        "userId": "3c40…",
        "userName": "Ayşe Yılmaz",
        "userEmail": "ayse@yildizasansor.com",
        "question": "QR etiketi nasıl yazdırırım?",
        "answer": "Asansörler ekranında ilgili asansörün kartından…",
        "createdAt": "2026-06-30T08:14:22Z"
      }
    ],
    "totalCount": 137,
    "page": 1,
    "pageSize": 20,
    "totalPages": 7,
    "hasPreviousPage": false,
    "hasNextPage": true
  },
  "message": null,
  "errors": null,
  "statusCode": 200
}
```

> `endDate` **tam eşitlik** karşılaştırır (`<=`): çıplak tarih `2026-06-30` = o günün
> 00:00'ıdır, günü tam kapsamaz. Bütün günü almak için `endDate`'e ertesi gün 00:00 (ör.
> `2026-07-01T00:00:00Z`) verin.

---

## DTO

**SupportChatLogDto**

| Alan | Tip | Açıklama |
|---|---|---|
| `id` | Guid | Log satırı |
| `projectId` | Guid | Sohbetin ait olduğu tenant |
| `projectName` | string | Firma adı (Project.Name) |
| `userId` | Guid? | Soruyu soran kullanıcı; kullanıcı silinmişse null |
| `userName` | string? | Kullanıcı ad-soyad (silinmişse null) |
| `userEmail` | string? | Yazan kullanıcının e-postası (snapshot — kullanıcı silinse de kalır) |
| `question` | string | Kullanıcının son mesajı |
| `answer` | string | Asistanın yanıtı |
| `createdAt` | DateTime | UTC |

---

## Hata kodları

| Kod | Ne zaman |
|---|---|
| 400 | `startDate`/`endDate` ISO 8601 olarak parse edilemedi |
| 401 | CRM key yanlış/eksik |
| 503 | `LIFTDESKSAAS__APIKEY` yapılandırılmamış |

Not: liste ucu her zaman 200 döner (sonuç yoksa boş `items`); 404 yoktur.

---

## CRM tarafında yapılacaklar

CRM ayrı projedir; yalnız bu sözleşmeyi tüketir.

### a) Sohbet logları ekranı
- `GET /api/v1/crm/support-chat-logs?projectId=&search=&startDate=&endDate=&page=&pageSize=`
  ile listele; filtreler: firma (projectId), kelime araması, tarih aralığı.
- Her satır: firma (`projectName`), kullanıcı (`userName` / `userEmail`), tarih (`createdAt`),
  **Soru** (`question`) ve **Yanıt** (`answer`). Yanıt düz metindir (markdown değil).
- Sayfalama: `totalCount / page / pageSize` (veya `hasNextPage`).
- Sıralama sabit: en yeni önce.

### b) Konuşma görünümü (opsiyonel)
- Aynı `projectId` + `userId`'nin satırlarını `createdAt` artan sırada dizerek bir
  kullanıcının tüm oturumunu soru→yanıt→soru→yanıt akışı olarak gösterebilirsiniz
  (oturum kimliği yoktur; ayrım kullanıcı + zaman yakınlığıyla yapılır).

### c) 10 gün uyarısı
- Ekranda "loglar 10 gün saklanır" notu göster; daha eski sohbet CRM'de görünmez.
- Kalıcı analiz gerekiyorsa periyodik çekip CRM veritabanında biriktir.

### d) Kullanım fikirleri
- `search` ile sık geçen konuları tara (ör. "fatura", "QR", "bakım") — yardım dokümanı
  eksiklerini ve asistanın yanlış yanıtladığı başlıkları yakala.
- Yanlış/eksik yanıt gördüğünüzde ilgili yardım dokümanını (`docs/help/`) güncelleyin;
  asistan yeni deploy'da güncel dokümanla yanıtlar.
