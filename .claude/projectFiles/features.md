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

## Sosyal Medya (Liftdesk hesapları — FB/IG/X) — 2026-10-11
- Ajan (GH Actions, Liftdesk deposu) Pzt haber / Çar bilgi / Cum ürün + bayram/anma taslakları üretir (`Pending`); **onay CRM'de**, yayınlama ayrı iş (publisher).
- Ekran `/admin/social-posts` (SuperAdmin): durum sekmeleri Bekleyen · Onaylı · Reddedilen · Yayınlanan, yayın günü süzgeci, menü rozeti = bekleyen sayısı.
- Detay: tr / en / en-GB sekmeli; kart önizlemesi (Liftdesk şablon PNG, blob olarak), CardTitle ≤80, Body, BodyShort ≤280 (canlı sayaç, aşımda kayıt engellenir), Hashtags; News'te kaynak linkleri ("onaylamadan önce aç-kontrol et").
- Aksiyonlar: Onayla (kirli metin varsa önce kaydeder) · Reddet (sebep opsiyonel) · Sil ("yeniden üretilsin" — slot boşalır, ajan yeni taslak yazar) · Onaylıda Geri Al (= reject). Approved/Published düzenlenemez (409).
- Görsel ezme: dosya CRM'de `SocialPostImages` (bytea, ≤5 MB, tür ilk baytlardan doğrulanır), Liftdesk'e yalnız URL yazılır. `GET /api/v1/social-posts/images/{id}` **anonim** (publisher/platform çeker; GUID tek anahtar, immutable cache). Eski görsel bizimse değiştirme/temizleme/silmede blob kaldırılır. Kök URL: `SocialPosts:PublicApiBaseUrl` yoksa X-Forwarded-Proto + Host.
- Proxy: `SocialPostsController` → `LiftdeskSocialPostsClient` (statik `Liftdesk:ApiKey`, `LiftdeskEnvelope`); Liftdesk 404/409/400 operatör Türkçesine ve aynı HTTP koduna çevrilir.

## Sosyal Medya Publisher (FB/IG/X yayınlama) — 2026-10-11
- Onaylanan EMS gönderilerini bağlı hesaplara yayınlar; EMS yalnız kuyruk + içerik + damga, **token'lar yalnız CRM'de (AES-GCM, `Social:TokenKey` ya da JWT secret)**.
- Hesap bağlama `/admin/social-posts/publisher`: Meta tek OAuth (Facebook Page + bağlı Instagram Business, uzun ömürlü sayfa token'ı), X OAuth 2.0 PKCE (`offline.access`, rotasyonlu refresh). Geri dönüş uçları anonim, güvenlik sunucu tarafı tek kullanımlık `state` (IMemoryCache 20 dk). Dil → hesap eşlemesi hesap başına (tr / en / en-GB; eşleme yoksa yayınlanmaz, hata değil).
- Zamanlayıcı `SocialPublisherService` (30 dk, 5 dk startup) → `SocialPublishRunner`: EMS `status=Approved` listesi, yayın günü + `PublishHourLocal` (10:00 Europe/Istanbul) kuralı, `MaxLateDays` (2) sonrası bayat → yayınlanmaz, uyarı. Job tablosu `(PostId, AccountId)` tekil → platform asla iki kez almaz; kısmi başarısızlıkta yalnız başarısız platform backoff'la (15 dk → 24 sa, 8 deneme) yeniden denenir; kalıcı hata (token 190/401, izin) → Blocked + hesap Revoked/Error + e-posta; yarıda kesilen iş (InProgress > 2 sa) körlemesine yeniden denenmez, insana düşer.
- Görsel: `customImageUrl` yoksa EMS şablon kartı indirilir ve `SocialPostImages(Kind=Card, Language)` olarak kendi anonim görsel ucumuzdan sunulur (IG görseli public URL'den çeker); X'e bayt yüklenir. Metin EMS'ten geldiği gibi: FB/IG = body + boş satır + hashtags; X = bodyShort aynen.
- Damga: tüm işler Succeeded/Skipped ve ≥1 Succeeded → `POST {EMS}/crm/social-posts/{id}/published` (idempotent; Liftdesk tarafına eklendi). Pano: durum/uyarılar, yayın günlüğü (platform post id), "Şimdi çalıştır", bloke işte Yeniden dene / Atla; onay ekranı detayında gönderi başına yayın durumu.
- Env: `Social__Meta__AppId/AppSecret`, `Social__X__ClientId/ClientSecret`, `Social__TokenKey`, `SocialPosts__PublicApiBaseUrl` (zorunlu — IG görsel kökü ve OAuth redirect), `SocialPublisher__PublishHourLocal|MaxLateDays|IntervalMinutes|Enabled|FrontendBaseUrl`.

## Sosyal Profiller (Facebook sayfası + Instagram profil metinleri) — 2026-10-13
- `/admin/social-posts/profiles`: provider × dil (tr) başına düzenlenebilir profil alanları; varsayılanlar kodda (`SocialProfileDefaults`), kayıt yoksa ekranda varsayılan görünür, "Varsayılana dön" kaydı siler.
- Facebook: hakkında (≤255), açıklama, web, telefon, e-posta **Pages API ile sayfaya basılır** (`POST /{page-id}`, `pages_manage_metadata`); profil fotoğrafı `POST /{page-id}/picture` (multipart source). Sayfa adı/kullanıcı adı/kategori/CTA/kapak API ile değişmez → "elle" etiketi, kopyala düğmeleri. Kapak API ile ayarlanamıyor.
- Instagram: profil yazma API'si YOK → kopyala + "Uyguladım" işareti. Bio ≤150, ad ≤64 doğrulanır.
- Görseller `SocialPostImages` (Kind=ProfilePicture/Cover, PostId=profil id), anonim görsel ucundan servis; operatör indirip platforma yükler.
