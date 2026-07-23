# CRM Ticket API'si

Kullanıcı geri bildirim/öneri boru hattı (hata triyaj hattının kardeşi:
[error-telemetry-api.md](error-telemetry-api.md)):

```
Liftdesk "Geri Bildirim" ekranı / CRM destek formu
   → POST /api/v1/tickets (JWT) | POST /api/v1/crm/tickets (ApiKey) → SupportTicket (Status=New)
   → [ticket-triage]  POST .../analysis → Triaged (AgentComment + AgentSuggestedAction)
   → CRM superadmin   PATCH .../status  → Approved | Rejected
   → [ticket-fixer]   PATCH .../status  → InProgress → Done (PR açılır) | Failed
   → insan: PR review → merge (merge her zaman MANUEL)
```

İki auth kanalı:
- **Tenant uçları** — `api/v1/tickets` : JWT `[Authorize]`; RequirePermission YOK (tüm tenant
  personeli kullanır). Müşteri-portal token'ları kullanamaz (403).
- **CRM + ajanlar (makine)** — `api/v1/crm/tickets` : `[CrmApiKey]`, header
  `Authorization: Bearer <CRM_API_KEY>` (config `LiftdeskSaas:ApiKey` / Railway
  `LIFTDESKSAAS__APIKEY` — hata telemetrisiyle **AYNI** key; yeni secret YOK).
  Key yapılandırılmamışsa 503, yanlışsa 401. CRM uçları cross-tenant çalışır (IgnoreQueryFilters).

Tüm yanıtlar standart `ApiResponse<T>` zarfı: `{ success, data, message, errors, statusCode }`.

---

## Durum makinesi (SupportTicket.Status)

```
New ──(analysis)──▶ Triaged ──▶ Approved ──▶ InProgress ──▶ Done
                       │            ▲             │
                       └▶ Rejected  │             └▶ Failed ──(re-approve)──▶ Approved
        (New'den doğrudan Approved/Rejected de OLUR — superadmin analizi beklemeyebilir)
        (Approved'dan doğrudan Done/Failed de OLUR)
```
- `analysis` yalnız Status `New | Triaged` iken yazılabilir (upsert/tazeleme); diğer durumlar → 409.
- `Approved` ← `Triaged | New | Failed` (Approved olurken FailReason temizlenir)
- `Rejected` ← `Triaged | New`
- `InProgress` ← `Approved | Failed`
- `Done`/`Failed` ← `InProgress | Approved` (Done/Failed → CompletedAt=utcnow)
- Geçersiz status → 400; geçersiz geçiş → 409.

---

## Tenant uçları (özet)

- `POST /api/v1/tickets` — Liftdesk "Geri Bildirim" ekranı; body
  `{ projectId, type, platform, subject, description }` → 201 + `TicketDto`
  (Source=Tenant, Status=New, CreatedByName=kullanıcı ad-soyad snapshot).
- `GET /api/v1/tickets?projectId=&status=&search=` — o projenin ticket'ları, CreatedAt desc.
- `GET /api/v1/tickets/{id}` — tek ticket (query filter içinde; yoksa 404).

---

## CRM uçları

### GET /api/v1/crm/tickets?status=&type=&platform=&projectId=&page=1&pageSize=20
Filtreli ticket listesi (cross-tenant), CreatedAt desc. `data`:
`PaginatedResult<CrmTicketDto>` (`items`, `totalCount`, `page`, `pageSize`).
Ajanlar: triage `status=New`, fixer `status=Approved`.

```bash
curl -H "Authorization: Bearer $CRM_API_KEY" \
  "$API/api/v1/crm/tickets?status=New&pageSize=10"
```

### GET /api/v1/crm/tickets/{id}
Tek ticket'ın tam detayı. `data`: `CrmTicketDto`. Yoksa 404.

```bash
curl -H "Authorization: Bearer $CRM_API_KEY" "$API/api/v1/crm/tickets/{id}"
```

### POST /api/v1/crm/tickets
Destek ekibi ticket'ı açar (telefonda/e-postayla gelen talep). Source=Crm, Status=New —
aynı şekilde ajan analiz eder. `projectId` opsiyonel (null → global ticket). `201` + `CrmTicketDto`.

```bash
curl -X POST -H "Authorization: Bearer $CRM_API_KEY" -H "Content-Type: application/json" \
  -d '{
    "projectId": null,
    "type": "Suggestion",
    "platform": "Web",
    "subject": "İş emri listesine excel dışa aktarım",
    "description": "Müşteri telefonla istedi: iş emirlerini excel olarak indirmek istiyor.",
    "createdByName": "Destek: Ayşe"
  }' \
  "$API/api/v1/crm/tickets"
```

### POST /api/v1/crm/tickets/{id}/analysis
Ticket-triage ajanı AI analizini yazar → `Triaged`, AgentAnalyzedAt=utcnow. Yalnız Status
`New | Triaged` iken (upsert/tazeleme); başka durumda 409. `agentComment` zorunlu.

```bash
curl -X POST -H "Authorization: Bearer $CRM_API_KEY" -H "Content-Type: application/json" \
  -d '{
    "agentComment": "İş emri listesi frontend/src/pages/workorders altında; export butonu yok. Efor: M. Risk düşük.",
    "agentSuggestedAction": "WorkOrdersPage tablosuna CSV dışa aktarım butonu ekle; mevcut liste verisini kullan."
  }' \
  "$API/api/v1/crm/tickets/{id}/analysis"
```

### PATCH /api/v1/crm/tickets/{id}/status
Birleşik durum geçişi. `status` zorunlu; kalan alanlar geçişe göre. Geçersiz status → 400,
geçersiz geçiş → 409.

| Aktör | Body | Geçiş |
|---|---|---|
| CRM onay | `{"status":"Approved","decidedBy":"ofc","decisionNote":"..."}` | Triaged\|New\|Failed → Approved |
| CRM ret | `{"status":"Rejected","decidedBy":"ofc","decisionNote":"..."}` | Triaged\|New → Rejected |
| Ajan başlar | `{"status":"InProgress","fixBranch":"ticket/ab12-slug"}` | Approved\|Failed → InProgress |
| Ajan başarı | `{"status":"Done","fixPrUrl":"...","resolutionNote":"..."}` | InProgress\|Approved → Done |
| Ajan hata | `{"status":"Failed","failReason":"..."}` | InProgress\|Approved → Failed |

```bash
curl -X PATCH -H "Authorization: Bearer $CRM_API_KEY" -H "Content-Type: application/json" \
  -d '{"status":"Approved","decidedBy":"ofc","decisionNote":"Uygun; küçük iş, uygulansın."}' \
  "$API/api/v1/crm/tickets/{id}/status"
```

> Opsiyonel body alanları `string?`; gönderilmezlerse 400 olmaz
> ([error-telemetry-api.md](error-telemetry-api.md) required-tuzağı notu).

---

## DTO'lar

**CrmTicketDto** (CRM görünümü — tüm alanlar): `id, projectId, projectName, createdByUserId,
createdByName, source, type, platform, subject, description, status, agentComment,
agentSuggestedAction, agentAnalyzedAt, decisionNote, decidedBy, decidedAt, resolutionNote,
fixBranch, fixPrUrl, failReason, completedAt, createdAt`.
(`projectName`: Project adı; ProjectId null ise null.)

**TicketDto** (tenant görünümü — SIZDIRMA YOK): `id, type, platform, subject, description, status,
decisionNote, resolutionNote, createdByName, createdAt`.
Tenant'a GÖSTERİLMEYENLER: `agentComment`, `agentSuggestedAction`, `fixBranch`, `fixPrUrl`,
`failReason`, `decidedBy` (AI analizi ve iç ajan alanları CRM-only; `decisionNote` +
`resolutionNote` resmi yanıt olarak gösterilir).

---

## Hata kodları

| Kod | Ne zaman |
|---|---|
| 400 | Geçersiz enum (status/type/platform parse edilemedi); zorunlu alan eksik (subject/description/agentComment) veya uzunluk aşımı |
| 401 | CRM key yanlış/eksik |
| 404 | Ticket bulunamadı |
| 409 | Geçersiz durum geçişi (ör. Done ticket'a analysis yazmak, Rejected → InProgress) |
| 503 | `LIFTDESKSAAS__APIKEY` yapılandırılmamış |

---

## Ajanlar & çalışma

Ajan script'leri + GitHub Actions workflow'ları: [tools/README.md](../tools/README.md).
- **ticket-agent/run.sh** (`.github/workflows/ticket-triage.yml`, 3 saatte bir): New ticket'ları
  analiz → AI yorumunu yazar. Repoya yazmaz.
- **ticket-agent/apply.sh** (`.github/workflows/ticket-fixer.yml`, günde 1): Approved ticket'ları
  uygular, build/tsc doğrular, **PR açar** → Done. Merge insanda.

Gerekli secrets AYNI (hata hattıyla ortak): `CLAUDE_CODE_OAUTH_TOKEN`, `EMS_BASE_API`,
`CRM_API_KEY`. Yeni secret GEREKMEZ.

---

## CRM tarafında yapılacaklar

CRM ayrı projedir; yalnız bu sözleşmeyi tüketir. Ekran ekran:

### a) Ticket listesi
- `GET /api/v1/crm/tickets?status=&type=&platform=&projectId=&page=&pageSize=` ile listele;
  filtreler: durum, tür, platform, proje.
- Durum rozetleri (TR): New=**Yeni**, Triaged=**AI İnceledi**, Approved=**Onaylandı**,
  Rejected=**Reddedildi**, InProgress=**Uygulanıyor**, Done=**Tamamlandı**, Failed=**Başarısız**.
- Sayfalama: `totalCount / page / pageSize` alanlarından.

### b) Detay ekranı
- `GET /api/v1/crm/tickets/{id}` — ticket içeriği (subject, description, tür, platform),
  kaynak/tenant bilgisi (source, projectName, createdByName, createdAt).
- AI yorumu **vurgulu** göster: `agentComment` (teknik değerlendirme) + `agentSuggestedAction`
  (önerilen aksiyon) + `agentAnalyzedAt`.

### c) Onay/Ret aksiyonları (SUPERADMIN)
- Superadmin-gating CRM tarafında yapılır (API key rol ayrımı bilmez).
- Onay: `PATCH .../status` body `{"status":"Approved","decidedBy":"<CRM kullanıcı adı>","decisionNote":"..."}`.
- Ret: `{"status":"Rejected","decidedBy":"<CRM kullanıcı adı>","decisionNote":"..."}`.
- `decisionNote` tenant'a resmi yanıt olarak gösterilir — kullanıcıya hitaben yaz.

### d) Destek ekibi ticket açma formu
- `POST /api/v1/crm/tickets` — alanlar: tür, platform, konu, açıklama, `createdByName`
  (destek personeli adı, ör. "Destek: Ayşe"); `projectId` opsiyonel (belirli tenant'a bağlamak için).

### e) Sonuç gösterimi + retry
- Done: `fixPrUrl`'i link olarak göster (PR review + merge insanda) + `resolutionNote`.
- Failed: `failReason`'ı göster; **yeniden Onay (re-approve)** butonu — aynı Approved PATCH'i
  (Failed → Approved geçişi serbesttir, FailReason temizlenir) → fixer bir sonraki turda tekrar dener.

### f) Periyodik yenileme
- Liste/detay ekranında periyodik yenileme (ör. 60 sn polling) önerilir — ajanlar cron'la çalıştığı
  için durumlar zamanla değişir (New → Triaged → ... → Done).
