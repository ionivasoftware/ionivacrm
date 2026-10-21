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

## Görüşme Takvimi + CSO Planlayıcı (Pipeline yerine) — 2026-10-19
- `/pipeline` artık haftalık randevu takvimi (Pzt–Cum, hafta sonu açılır). Randevu türleri: yeni arama, takip araması, memnuniyet araması, toplantı, eğitim, demo, diğer. Eski fırsat panosu `/pipeline/firsatlar`.
- **CSO planlayıcı** (`CallPlanner`, saf kurallar; `CallPlanService` veri; `CallPlanBackgroundService` günlük 05:00 TR sonrası, 20 dk kontrol, proje×gün idempotent): her iş günü 5 yeni / 5 takip / 2 memnuniyet (env `CallPlan__NewCallsPerDay|FollowUpsPerDay|SatisfactionPerDay|HorizonBusinessDays(5)|RunHourLocal(5)|Enabled`). Yeni arama: Lead, etiket ağırlığı, son görüşme (7 gün bekleme), yeni kayıt bonusu. Takip: Active/Demo/Passive (+90 gün içinde churn), kullanım raporu (14+ gün giriş yok, bakım girişi yok, deneme planı), süre dolumu ≤30/≤60 gün, söz verilen takip (+100, kota üstü, kendi gününde); sağlıklı hesapta yalnız "uzun süredir görüşülmedi" takip gerekçesi değildir. Memnuniyet: sağlıklı Active (giriş ≤14 gün veya bakım >0), 60 gün temas yok, son memnuniyet araması ≥180 gün (19.10.2026: kullanıcı istedi, sık aranmasın). Firma ufukta en fazla bir kez; mevcut planlı randevular kotaya sayılır. Gerekçe (`Reason`) kartta görünür.
- Randevudan görüşme kaydı: kanal, görüşüldü/ulaşılamadı, memnuniyet puanı (1–5), tekrar arama günü → 14:00 takip randevusu; ulaşılamadıysa yeniden planla. ContactHistories yeni kolonlar: `Purpose`, `Reached`, `SatisfactionScore`, `NextContactAt`, `AppointmentId` (Aktivite Ekle diyaloğunda da var).
- Müşteri detayı: "Randevu" düğmesi, aktivite sekmesinde planlı randevular + kayıt girme. Planı elle çalıştırma ve yeniden oluşturma: yalnız SuperAdmin (düğmeler diğerlerine görünmez).

## Lead Ajanı (web'den aday bulma + SuperAdmin onayı) — 2026-10-19
- GitHub Actions `lead-agent.yml` (Salı 06:00 UTC + elle): `tools/lead-agent/run.sh` → API'den sıradaki iller (60 günlük rotasyon, `LeadAgentCities` büyük pazarlar önce) → il başına Claude Code (`WebSearch,WebFetch`) → JSON → `POST /api/v1/lead-agent/candidates`. Kapsam yalnız TR, yalnız firmalar, telefon ya da web zorunlu.
- Dedupe SUNUCUDA (`LeadNormalizer`/`LeadDedupe`, testli): isim Türkçe harf katlama + Ltd/Şti/A.Ş./San./Tic. ayıklama; telefon ulusal 10 hane; web/e-posta alan adı (gmail vb. hariç); vergi no. TÜM müşteriler + TÜM eski adaylar: telefon/alan/vergi/isim → `Duplicate`; eski aday → `exists` (kayıt açılmaz); bulanık isim (ayırt edici kelimeler, "asansör/servis" gibi genel kelimeler sayılmaz) ≥0.92 ya da ≥0.86 + aynı il → Duplicate, 0.80–0.92 → Pending + "olası eşleşme" notu. Parmak izi tekil; reddedilen bir daha önerilmez.
- Ekran `/admin/lead-candidates` (SuperAdmin, menü rozeti): Bekleyen/Onaylanan/Reddedilen/Mükerrer, arama, toplu ret, detayda alanları düzenleyip **Onayla → Lead oluştur** (CreateCustomerCommand, etiket varsayılan Potansiyel, kaynak notu zaman çizelgesine), reddet (sebep), mevcut müşteriyle eşleştir, bekleyene al; ajan koşuları paneli.
- Env: `LeadAgent__ApiKey` (ajan `X-Agent-Key`), ops. `LeadAgent__ProjectId`. GitHub secrets: `CLAUDE_CODE_OAUTH_TOKEN`, `CRM_LEAD_AGENT_KEY`.

## Tanıtım Maili (kullanıcının Gmail'inden) — 2026-10-19
- Materyaller `output/docs/marketing/` (intro-email*.html + *.pdf) csproj Content ile imaja `marketing/` olarak girer; şablon konu seçenekleri dosya başındaki `<!-- 1: ... -->` yorumlarından, yer tutucular `[Name]`, `[Ad Soyad]`, `{{CompanyName}}`, `{{ContactName}}`, `{{SenderName}}`, `[Note]` (yoksa not selamlamanın altına girer). Türkçe harfler kaçırılmaz (minimal HTML escape).
- Her kullanıcı Ayarlar → "E-posta Gönderimi (Gmail)": adres + 16 haneli Uygulama Şifresi (AES-GCM şifreli, geri dönmez), gönderen adı, kendine BCC; test maili. smtp.gmail.com:587 STARTTLS. Workspace alan adı için onay kutusu.
- Müşteri detayı "Tanıtım Maili" düğmesi: alıcı (kart e-postası, elle değiştirilebilir), şablon/konu seçimi, kişisel not, ek listesi → gönderim, zaman çizelgesine E-posta kaydı (Purpose=NewCall). Randevu kapatma diyaloğunda "görüşme sonrası tanıtım maili gönder" kutusu.

## Global Lead'ler + İngilizce E-posta Outreach (TR dışı, şimdilik UK) — 2026-10-21
- TR ajanının kardeşi: GitHub Actions `global-lead-agent.yml` (Perşembe 06:00 UTC + elle; input `country` ISO2, `regions`) → `tools/lead-agent/run-global.sh` + `prompt.global.md` → `GET /api/v1/lead-agent/global/next-regions` (`GlobalLeadRegions`, GB 26 bölge, 90 gün rotasyon) → bölge başına Claude Code → `POST /api/v1/lead-agent/global/candidates`. Aynı `LeadAgent__ApiKey` (`LeadAgentAuth` ortak). Kaynaklar LEIA/Yell/Checkatrade/Google/firma siteleri; OEM'ler atlanır; e-posta ya da web zorunlu; noreply/webmaster elenir.
- Adaylar **ayrı** `GlobalLeads` tablosuna düşer (Müşteriler TR'ye özel kalır). Dedupe `GlobalLeadNormalizer` (UK telefon, plc/llp/… ayıklama, ülke-önekli parmak izi) + `LeadDedupe` (tüm eski global lead'ler). Koşular `LeadAgentRuns.Cities = "GB:London, …"` (TR ekranı gizler).
- Ekran `/admin/global-leads` (SuperAdmin, Earth ikonu, rozet): Bekleyen / Onaylı-Outreach / Reddedilen / Mükerrer, ülke + dizi durumu filtresi, arama, toplu onay+mail / toplu ret. Detayda alan düzenleme (yetkili, e-posta…), **Onayla → maili başlat** (varsayılan; kutu kaldırılırsa outreach'siz onay), dizi paneli: başlat/yeniden başlat, duraklat, sürdür, sıradaki adımı şimdi gönder, gelen kutusunu kontrol et, yanıtladı, durdur, listeden çıkar; gönderim geçmişi. "Dizi & limitler" paneli (pencere, limit, şablon/konu/ek, son döngü), "Sıradakileri şimdi gönder".
- Dizi `output/docs/marketing/global/`: `sequence.json` (Europe/London, hafta içi 09–17; intro gün 0 + PDF ekleri, followup-1 +4, followup-2 +9 gün) + `intro.en-GB.html` (İngilizce şablon, veri aktarımı ifadesi yok) + `followup-1/2.en-GB.html`. Takipler intro'nun thread'inde (In-Reply-To, konu "Re: …"). Her mailde `{{UnsubscribeUrl}}` + `List-Unsubscribe` başlığı → public `GET|POST /api/v1/outreach/unsubscribe/{token}` (dizi Unsubscribed).
- `OutreachService` (`GmailSmtp` ortak SMTP; IMAP imap.gmail.com:993 aynı uygulama şifresiyle takip öncesi yanıt/bounce arar → Replied/Bounced durdurur) + `OutreachBackgroundService` (10 dk): pencere dışı → ertele, gönderici başına günlük limit (`Outreach__DailyCapPerSender` 40), gönderimler arası `Outreach__MinGapSeconds` (60), `Outreach__MaxPerCycle` (25), `Outreach__Enabled`, `Outreach__IntervalMinutes`, ops. `Outreach__Folder`. Gönderici = onaylayan kullanıcı (`SenderUserId`); Gmail ayarı yoksa onay reddedilir. SMTP auth hatası → Failed; diğer hata → 2 saat sonra tekrar.
- Bilinen eksikler (kullanıcıya iletildi): İngilizce katalog/fiyat PDF yok (global klasöre PDF konunca otomatik eklenir); kişisel Gmail 500/gün + itibar → ayrı Workspace gönderici (SPF/DKIM/DMARC) önerisi; PECR/GDPR (B2B kurumsal OK, sole trader rıza, AB ülkeleri farklı); IMAP bounce tespiti best-effort.
