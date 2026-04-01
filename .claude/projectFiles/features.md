# Tamamlanan Özellikler

## Auth & Kullanıcı
- JWT login / logout / refresh / me
- SuperAdmin + proje bazlı roller (UserProjectRoles)
- Profil güncelleme, bildirimler

## Müşteri Yönetimi
- CRUD (oluştur, listele, güncelle, soft-delete)
- Status: `Lead(1)` / `Active(2)` / `Demo(3)` / `Churned(4)` / `Passive(5)`
- Label: YuksekPotansiyel / Potansiyel / Notr / Vasat / Kotu
- Segment: serbest string (EMS'ten gelir — DB sütunu text tipinde)
- Lead → Müşteri dönüşümü: `POST /api/v1/customers/{id}/convert`
- EMS süre uzatma: `POST /api/v1/customers/{id}/extend-expiration`
  - Button sadece numeric veya `SAASA-` prefixli `legacyId`'de görünür
  - 1 ay / 1 yıl ise Paraşüt taslak fatura otomatik oluşturulur
- `ExpirationDate = null` olan müşteriler → status `Lead` (startup'ta UPDATE çalışır)

## Görüşme & Görev
- ContactHistories: müşteri bazlı + global filtreli liste (`GET /api/v1/contact-histories`)
- CustomerTasks: müşteri bazlı + global liste (`GET /api/v1/tasks`)
- Pipeline stage değişiminde otomatik ContactHistory logu

## Pipeline & Fırsatlar
- Kanban board — drag & drop stage değişimi (`PATCH /api/v1/pipeline/{id}/stage`)
- Opportunity CRUD

## Sync Sistemi
- **SaasSyncJob** — 3 aşama: EMS CRM müşterileri (paginated+delta), EMS sub/orders, RezervAl
- Inbound webhook: `POST /api/v1/sync/saas-a` ve `saas-b` (X-Api-Key header)
- Sync logs: `GET /api/v1/sync/logs`
- Manuel trigger: `POST /api/v1/sync/trigger` (SuperAdmin)
- LegacyId formatları: EMS=`"3"` (numeric), eski EMS=`"SAASA-3"`, RezervAl=`"SAASB-{id}"`, manuel=`"PC-{guid}"`
- `ResolveProjectAsync`: config `SaasA:ProjectId` → DB direct lookup → EmsApiKey'li proje → ilk proje

## Paraşüt Entegrasyonu
- OAuth bağlantısı, token yenileme (ParasutTokenHelper — 3 aşamalı: valid → refresh → password grant)
- Müşteri Paraşüt contact eşleştirme (`ParasutContactId` alanı)
- Fatura geçmişi görüntüleme (müşteri detay sayfası)
- EMS süre uzatmada taslak satış faturası (0 TL, %20 KDV, açıklama: "1 Aylık/Yıllık EMS Lisans")

## Dashboard & Raporlar
- Widget'lar: toplam müşteri, aktif, demo, görevler, pipeline değeri
- Grafikler: müşteri artışı, pipeline dağılımı

## Admin
- Kullanıcı yönetimi (CRUD + proje rol atama)
- Proje yönetimi
