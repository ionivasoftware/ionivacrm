# Liftdesk (ION CRM SaaS) — Firma Checklist Yönetimi Sözleşmesi

Bu doküman **CRM tarafını kuran ekip içindir**. ION CRM SaaS entegrasyonuna
(`docs/liftdesk-saas-integration-contract.md`) **ek** bir yüzeydir: CRM operatörü bir firmanın
**bakım** ve **arıza** checklist'ini görüntüler, düzenler ve **tek buton** ile varsayılana döndürür.

Aynı `api/v1/crm` yüzeyi, aynı auth, aynı düz (zarfsız) yanıt kuralları — yalnızca yeni endpoint'ler.

> Checklist = servis/bakım formunda teknisyenin işaretlediği kontrol maddeleri. Başlık (grup) →
> altında maddeler şeklinde iki seviyelidir. "Varsayılan" = Liftdesk'in DEMO tenant'ındaki curated
> checklist; yeni firma kaydında bu şablon kopyalanır. Bu endpoint'ler o kopyayı yönetir.

---

## 1. Kimlik doğrulama ve genel kurallar

- **Auth:** SaaS sözleşmesiyle **aynı** statik Bearer key:
  `Authorization: Bearer <LIFTDESK_CRM_API_KEY>` (Liftdesk'te config `LiftdeskSaas:ApiKey` /
  Railway env `LIFTDESKSAAS__APIKEY`). Geçersiz/eksik key → **401**.
- **Firma kimliği:** yol parametresi `{companyId}` = **CrmCompanyId (int)** — SaaS feed'indeki
  `customers[].id` ve `payments[].companyId` ile aynı kimlik uzayı. Guid ProjectId KULLANILMAZ.
- **Yanıt şeması DÜZDÜR** — `{success,data,message}` zarfı **YOKTUR** (SaaS yüzeyiyle tutarlı).
  Her endpoint kendi düz gövdesini döner. Hatalarda 4xx + kısa açıklayıcı metin.
- Alan adları **camelCase**, tarih yok (checklist'te tarih alanı yoktur).
- **Makine-makine kanalıdır:** CRM **backend**'inden çağırın; key'i tarayıcıya indirmeyin.

---

## 2. Endpoint'ler

Firmanın **aktif form şablonu** (`ChecklistFormId`, varsayılan 4) üzerinden çalışır. Görüntüle/düzenle/
reset üçü de aynı forma yazar/okur — tutarlıdır.

### 2.1 Bakım checklist'ini görüntüle
```
GET /api/v1/crm/companies/{companyId}/maintenance-checklist
```

### 2.2 Arıza checklist'ini görüntüle
```
GET /api/v1/crm/companies/{companyId}/fault-checklist
```

**Yanıt (ikisi de aynı şema):**
```json
{
  "companyId": 7,
  "kind": "maintenance",
  "formId": 4,
  "headers": [
    {
      "id": "3f2b8c10-…",
      "title": "Kuyu Kontrolü",
      "sortOrder": 1,
      "isActive": true,
      "items": [
        { "id": "9a1c…", "text": "Kuyu dibi temizliği", "sortOrder": 1, "isActive": true },
        { "id": "b2d4…", "text": "Tampon kontrolü",      "sortOrder": 2, "isActive": true }
      ]
    }
  ]
}
```
- `headers` **SortOrder**'a göre sıralı; her başlığın `items`'ı da SortOrder'a göre.
- Hem **aktif hem pasif** (silinmemiş) satırlar döner (`isActive` ile ayırt edilir) — editörde
  hepsini gösterip aç/kapa yapabilmeniz için.
- Firmanın hiç checklist'i yoksa `headers: []` döner (çok eski/seed'lenmemiş tenant). Bu durumda
  **reset** (2.5) ile varsayılanı bas.

### 2.3 Bakım checklist'ini düzenle (tam-belge değişim)
```
PUT /api/v1/crm/companies/{companyId}/maintenance-checklist
Content-Type: application/json
```

### 2.4 Arıza checklist'ini düzenle (tam-belge değişim)
```
PUT /api/v1/crm/companies/{companyId}/fault-checklist
```

**İstek gövdesi (ikisi de aynı):**
```json
{
  "headers": [
    {
      "title": "Kuyu Kontrolü",
      "isActive": true,
      "items": [
        { "text": "Kuyu dibi temizliği", "isActive": true },
        { "text": "Tampon kontrolü",      "isActive": false }
      ]
    }
  ]
}
```
- **Tam-belge değişim:** gönderdiğiniz set checklist'in **YENİ tam hâlidir**. Sunucu firmanın mevcut
  checklist'ini **tamamen değiştirir** (eski başlık/maddeler silinir, gönderilenler yazılır). Tek
  "Kaydet" butonu için tasarlandı — kısmi (satır-satır) güncelleme yoktur.
- `sortOrder` **göndermeyin** — sunucu başlıkları ve maddeleri **dizideki sıraya göre** numaralandırır.
- `isActive` opsiyonel (varsayılan `true`). `title` ve madde `text` **zorunlu** (boş olamaz) → boşsa **400**.
- `headers: []` (boş dizi) göndermek checklist'i **temizler** (bilinçli). `headers` alanının kendisi
  eksikse **400**.
- Yanıt: kaydedilmiş checklist (2.2'deki şema, yeni `id`'lerle).

### 2.5 Varsayılana döndür — TEK BUTON
```
POST /api/v1/crm/companies/{companyId}/checklists/reset
Content-Type: application/json

{ "kind": "both" }
```
- `kind` ∈ **`"maintenance" | "fault" | "both"`**. Boş/eksik gövde → **`both`** varsayılır.
- Firmanın seçilen checklist'ini **DEMO şablonuna** (genel varsayılan) döndürür. **Firmanın mevcut
  özelleştirmesi SİLİNİR** — bu bir sıfırla/yeniden-uygula işlemidir.
- Yanıt: reset edilen checklist(ler).

**Yanıt:**
```json
{
  "companyId": 7,
  "maintenance": { "companyId": 7, "kind": "maintenance", "formId": 4, "headers": [ … ] },
  "fault":       { "companyId": 7, "kind": "fault",       "formId": 4, "headers": [ … ] }
}
```
- `kind: "maintenance"` ise `fault` **null** (ve tersi). `both` ise ikisi de dolu.

---

## 3. Veri modeli (özet)

| nesne | alan | tip | not |
|---|---|---|---|
| header | `id` | guid | yalnız yanıtta; PUT'ta göndermeyin |
| header | `title` | string | ★ zorunlu (PUT) |
| header | `sortOrder` | int | yalnız yanıt; PUT'ta diziden türetilir |
| header | `isActive` | bool | varsayılan true |
| header | `items` | item[] | başlık altındaki maddeler |
| item | `id` | guid | yalnız yanıt |
| item | `text` | string | ★ zorunlu (PUT) |
| item | `sortOrder` | int | yalnız yanıt |
| item | `isActive` | bool | varsayılan true |

---

## 4. Hata kodları

| kod | anlam | CRM ne yapmalı |
|---|---|---|
| 200 | başarılı | — |
| 400 | geçersiz gövde (boş `title`/`text`, eksik `headers`, geçersiz `kind`) | gövdeyi düzelt |
| 401 | key yanlış/eksik | `Authorization` header'ını kontrol et |
| 404 | firma (companyId) bulunamadı | firma kimliğini/senkronu kontrol et |

---

## 5. Tasarım kararları (bilgi)

- **Firma kimliği CrmCompanyId (int):** SaaS yüzeyinin diğer 6 endpoint'iyle tutarlı. CRM zaten bu
  kimliği kullanıyor.
- **Edit = tam-belge değişim (PUT):** tek "Kaydet" için en yalın model. Satır-satır CRUD YOK.
- **Reset yıkıcıdır:** varsayılanı basmak firmanın özelleştirmesini kalıcı olarak siler (yeniden-tohum).
  Bu, "varsayılan değerleri tek butonla tanımla" gereğinin doğrudan karşılığıdır.
- **Form kapsamı:** işlemler firmanın `ChecklistFormId`'sine (varsayılan 4) yazar; görüntü de aynı formu
  okur → üçü tutarlı. (Özel form numaralı nadir tenant'larda da self-consistent.)
- **Kültür:** yeni satırlar firmanın `CompanySettings.Culture`'ıyla (yoksa TR=1) yazılır.
- **Eşzamanlılık:** reset/edit, mobil lazy-seed ile **aynı advisory lock** altında serileşir; yarış
  durumunda çift kayıt/çakışma olmaz.

---

## 6. curl örnekleri

```bash
B="https://ems-api-development.up.railway.app"; K="Authorization: Bearer $CRM_API_KEY"

# Görüntüle
curl -H "$K" "$B/api/v1/crm/companies/7/maintenance-checklist"
curl -H "$K" "$B/api/v1/crm/companies/7/fault-checklist"

# Düzenle (tam-belge)
curl -X PUT -H "$K" -H "Content-Type: application/json" \
  -d '{"headers":[{"title":"Kuyu","items":[{"text":"Kuyu dibi temizliği"}]}]}' \
  "$B/api/v1/crm/companies/7/maintenance-checklist"

# Varsayılana döndür (tek buton)
curl -X POST -H "$K" -H "Content-Type: application/json" \
  -d '{"kind":"both"}' "$B/api/v1/crm/companies/7/checklists/reset"

# Negatifler: yanlış key → 401; bilinmeyen companyId → 404; boş title → 400
```

**Kontrol listesi:**
- [ ] 5 endpoint yukarıdaki şemalarla birebir (camelCase, düz gövde, zarf yok)
- [ ] `companyId` = CrmCompanyId (SaaS feed'iyle aynı kimlik)
- [ ] PUT tam-belge: `sortOrder` göndermeden, sıra diziden türer; boş `title`/`text` → 400
- [ ] Reset yıkıcı (özelleştirmeyi siler) — UI'da onay iste
- [ ] 401/404/400 gövdelerinde kısa açıklayıcı metin
