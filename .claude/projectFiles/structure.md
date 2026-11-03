# Proje Yapısı

> Bu dosya önemli değişikliklerde güncellenir. Projeyi yeniden analiz etmene gerek kalmaz.

## Dizin Yapısı
```
output/
├── src/
│   ├── IonCrm.API/
│   │   ├── Controllers/         → HTTP endpoint'leri
│   │   ├── Middleware/          → GlobalException, Tenant, HangfireAuth
│   │   ├── Common/              → ApiResponse<T>, UtcDateTimeConverter
│   │   └── Program.cs           → DI, middleware pipeline, startup SQL
│   ├── IonCrm.Application/
│   │   ├── Auth/                → Login, Register, Refresh, UpdateProfile, AssignRole
│   │   ├── Customers/           → CRUD + ExtendEmsExpiration + ConvertLead
│   │   ├── ContactHistory/      → CRUD + GetAll
│   │   ├── CustomerTasks/       → CRUD + UpdateStatus
│   │   ├── Opportunities/       → CRUD + UpdateStage
│   │   ├── Features/
│   │   │   ├── Dashboard/       → GetDashboardStats, GetReports
│   │   │   ├── Parasut/         → Connect, Invoices, ParasutTokenHelper
│   │   │   ├── Projects/        → CRUD
│   │   │   └── Sync/            → ProcessWebhook, GetSyncLogs, NotifySaas
│   │   └── Common/
│   │       ├── DTOs/            → CustomerDto, UserDto, SyncLogDto, vb.
│   │       ├── Interfaces/      → ISaasAClient, ISaasBClient, IParasutClient, vb.
│   │       └── Models/ExternalApis/ → SaasAModels, SaasBModels, ParasutModels
│   ├── IonCrm.Infrastructure/
│   │   ├── Persistence/
│   │   │   └── ApplicationDbContext.cs  → global query filter (tenant + soft-delete)
│   │   ├── Repositories/        → Customer, User, SyncLog, Opportunity, vb.
│   │   ├── ExternalApis/        → SaasAClient, SaasBClient, ParasutClient
│   │   ├── BackgroundServices/  → SaasSyncJob, SyncBackgroundService
│   │   └── DependencyInjection.cs
│   └── IonCrm.Domain/
│       ├── Entities/            → Customer, User, Project, SyncLog, Opportunity, vb.
│       ├── Enums/               → CustomerStatus, CustomerLabel, SyncSource, vb.
│       └── Interfaces/          → IRepository<T>, ICustomerRepository, vb.
├── tests/
│   └── IonCrm.Tests/
│       └── Sync/SaasSyncJobTests.cs   → 438 test (xUnit + Moq + InMemory EF)
└── frontend/
    └── src/
        ├── api/
        │   ├── client.ts        → axios + JWT interceptor
        │   ├── auth.ts          → login, logout, refresh, me
        │   ├── customers.ts     → CRUD + hooks (useCustomers, useExtendEmsExpiration, vb.)
        │   ├── admin.ts         → users + projects yönetimi
        │   ├── dashboard.ts     → stats + reports
        │   ├── parasut.ts       → bağlantı + faturalar
        │   └── sync.ts          → logs + trigger
        ├── pages/
        │   ├── LoginPage.tsx
        │   ├── DashboardPage.tsx
        │   ├── ContactHistoriesPage.tsx
        │   ├── PipelinePage.tsx
        │   ├── TasksPage.tsx
        │   ├── SyncLogsPage.tsx
        │   ├── SettingsPage.tsx
        │   ├── ReportsPage.tsx
        │   ├── InvoicesPage.tsx
        │   ├── customers/
        │   │   ├── CustomersPage.tsx
        │   │   └── CustomerDetailPage.tsx   → "Süre Uzat" butonu (legacyId kontrolü)
        │   └── admin/
        │       ├── UsersAdminPage.tsx
        │       └── ProjectsAdminPage.tsx
        ├── stores/
        │   ├── authStore.ts     → currentUser, currentProjectId, token
        │   └── themeStore.ts
        └── types/index.ts       → tüm TypeScript tipleri
```

## Önemli Dosyalar

| Dosya | Açıklama |
|---|---|
| `output/src/IonCrm.API/Program.cs` | Startup, DI, middleware, idempotent SQL migration |
| `output/src/IonCrm.Infrastructure/BackgroundServices/SaasSyncJob.cs` | EMS + RezervAl sync job |
| `output/src/IonCrm.Infrastructure/Persistence/ApplicationDbContext.cs` | Global query filter |
| `output/src/IonCrm.Application/Common/DTOs/CustomerDto.cs` | Customer DTO (LegacyId dahil) |
| `output/src/IonCrm.Application/Customers/Mappings/CustomerMappings.cs` | Entity → DTO mapping |
| `output/src/IonCrm.Application/Features/Parasut/ParasutTokenHelper.cs` | Token yenileme (3 aşama) |
| `output/frontend/src/types/index.ts` | Tüm frontend tipleri |
| `output/frontend/src/api/customers.ts` | Customer API hook'ları |

## DB Schema Notları
- `Customers.Segment` → `text` (başlangıçta `integer` idi, startup SQL ile dönüştürüldü)
- `Customers.ExpirationDate` → `timestamp with time zone` (nullable)
- `Customers.LegacyId` → `text` (EMS=numeric, eski EMS=`SAASA-{n}`, RezervAl=`SAASB-{n}`)
- `Customers.ParasutContactId` → `text` (nullable)
- `Projects.EmsApiKey` → `text` (nullable, per-project EMS API key override)
- `Projects.RezervAlApiKey` → `text` (nullable)
- Tüm entity'ler `BaseEntity`'den türer: `Id`, `CreatedAt`, `UpdatedAt`, `IsDeleted`
- `SocialPostImages` → sosyal medya özel görselleri (bytea, PostId indeksli; tenant kapsamı yok; satır hard-delete)
- `Users` + `MailAddress, MailPasswordEncrypted, MailSenderName, MailBccSelf` (Gmail gönderimi)
- `LeadCandidates` (`Fingerprint` tekil; global) · `LeadAgentRuns` (`RunId` tekil)
- `Customers` + `Country` (ISO2, Liftdesk feed) · `MarketingOptOut` · `CustomerLifecycleMails` (`(CustomerId, Step)` indeks — demo yaşam döngüsü gönderim kaydı)
- `GlobalLeads` (TR dışı lead'ler; `Fingerprint` ülke-önekli tekil, `UnsubscribeToken` tekil, `(Status, OutreachStatus, NextSendAt)` indeks; global) · `GlobalLeadEmails` (`LeadId`, `(SenderUserId, SentAt)` — günlük limit)
- `Appointments` (tenant; `(ProjectId, ScheduledAt)` indeks) · `CallPlanRuns` (`(ProjectId, PlanDate)` tekil) · `ContactHistories` + `Purpose, Reached, SatisfactionScore, NextContactAt, AppointmentId`
- `SocialProfiles` (`(Provider, Language)` tekil, `FieldsJson` jsonb, görsel id'leri, AppliedAt)
- `SocialAccounts` (şifreli token, `(Provider, ExternalId)` tekil) · `SocialPublishPosts` (`PostId` tekil, damga) · `SocialPublishJobs` (`(PostId, AccountId)` tekil — mükerrer yayın engeli) · `SocialPostImages.Kind/Language` (Card = publisher'ın sakladığı şablon kart)

## API Endpoint'leri

### Auth — `/api/v1/auth`
| Method | Path | Açıklama |
|---|---|---|
| POST | `/login` | JWT token al |
| POST | `/logout` | Token iptal |
| POST | `/refresh` | Token yenile |
| GET | `/me` | Mevcut kullanıcı |
| PUT | `/profile` | Profil güncelle |

### Customers — `/api/v1/customers`
| Method | Path | Açıklama |
|---|---|---|
| GET | `/` | Listele (search, status, label, segment filtresi) |
| POST | `/` | Oluştur |
| GET | `/{id}` | Detay |
| PUT | `/{id}` | Güncelle |
| DELETE | `/{id}` | Soft-delete |
| GET | `/{id}/details` | Detay + son görüşmeler + görevler |
| POST | `/{id}/convert` | Lead → Müşteri dönüşümü |
| POST | `/{id}/extend-expiration` | EMS süre uzat + Paraşüt fatura |
| GET | `/{id}/contact-histories` | Görüşmeler |
| POST | `/{id}/contact-histories` | Görüşme ekle |
| GET | `/{id}/tasks` | Görevler |
| POST | `/{id}/tasks` | Görev ekle |
| PUT | `/{id}/tasks/{taskId}` | Görev güncelle |
| PATCH | `/{id}/tasks/{taskId}/status` | Görev status değiştir |
| GET | `/{id}/opportunities` | Fırsatlar |
| POST | `/{id}/opportunities` | Fırsat ekle |
| PUT | `/{id}/opportunities/{oppId}` | Fırsat güncelle |

### Sync — `/api/v1/sync`
| Method | Path | Açıklama |
|---|---|---|
| POST | `/saas-a` | EMS webhook (X-Api-Key) |
| POST | `/saas-b` | RezervAl webhook (X-Api-Key) |
| GET | `/logs` | Sync log listesi |
| POST | `/trigger` | Manuel sync tetikle (SuperAdmin) |

### Diğer
| Method | Path | Açıklama |
|---|---|---|
| GET | `/api/v1/dashboard` | İstatistikler |
| GET | `/api/v1/reports` | Raporlar |
| GET | `/api/v1/contact-histories` | Global görüşme listesi |
| GET | `/api/v1/tasks` | Global görev listesi |
| GET | `/api/v1/pipeline` | Fırsat listesi |
| PATCH | `/api/v1/pipeline/{id}/stage` | Kanban stage değiştir |
| GET | `/api/v1/users` | Kullanıcı listesi |
| POST | `/api/v1/users` | Kullanıcı oluştur |
| GET | `/api/v1/projects` | Proje listesi |
| GET/POST/PUT | `/api/v1/parasut/...` | Paraşüt entegrasyonu |
| GET | `/health` | Health check |

### Sosyal Medya — `/api/v1/social-posts` (SuperAdmin; Liftdesk `crm/social-posts` proxy)
| Method | Path | Açıklama |
|---|---|---|
| GET | `/` | Liste (`status`, `from`, `to`, `page`, `pageSize`) |
| GET | `/pending-count` | Menü rozeti — bekleyen taslak sayısı |
| GET | `/{id}` | Detay (tr → en → en-GB varyantları) |
| GET | `/{id}/card?lang=` | Şablon kart PNG (Liftdesk üretir) |
| PUT | `/{id}` | Düzenle (topic, customImageUrl, variants) — yalnız Pending/Rejected |
| POST | `/{id}/image` | Kendi görselini yükle (multipart `file`, ≤5 MB) → CRM saklar, Liftdesk'e URL |
| GET | `/images/{imageId}` | **Anonim** görsel servisi (publisher/platform çeker) |
| POST | `/{id}/approve` | Pending/Rejected → Approved |
| POST | `/{id}/reject` | Pending/Approved → Rejected (`{reason?}`) |
| DELETE | `/{id}` | Soft-delete, slot boşalır (Published silinemez) |

### Sosyal Hesaplar — `/api/v1/social-accounts` (SuperAdmin; token döndürmez)
| Method | Path | Açıklama |
|---|---|---|
| GET | `/` | Bağlı hesaplar |
| GET | `/config` | Yapılandırma (Meta/X hazır mı, redirect URI'lar, yayın saati) |
| POST | `/meta/connect` | Meta OAuth adresi (state sunucuda) |
| GET | `/meta/callback` | **Anonim** OAuth dönüşü → sayfalar state altında → ekrana yönlendirme |
| GET | `/meta/pending/{state}` | Seçilebilir sayfalar (+ bağlı Instagram) |
| POST | `/meta/complete` | Seçilen sayfa/IG'yi kaydet (`{state, pageId, language, connectFacebook, connectInstagram}`) |
| POST | `/x/connect` | X PKCE adresi |
| GET | `/x/callback` | **Anonim** X dönüşü → hesap kaydı → ekrana yönlendirme |
| PUT | `/{id}` | Etiket / dil eşlemesi / etkin |
| DELETE | `/{id}` | Bağlantıyı kaldır (token silinir) |
| POST | `/{id}/verify` | Token'ı platformda dene |
| POST | `/{id}/test` | GERÇEK test gönderisi (`{text, imageUrl?}`; IG için imageUrl zorunlu) |

### Sosyal Publisher — `/api/v1/social-publisher` (SuperAdmin)
| Method | Path | Açıklama |
|---|---|---|
| GET | `/status` | Sayımlar, son döngü, uyarılar |
| GET | `/jobs?postId=&status=&limit=` | Yayın günlüğü (platform post id) |
| GET | `/posts?limit=` | Gönderi bazlı damga durumu |
| POST | `/run` | Bir döngüyü hemen koştur |
| POST | `/jobs/{id}/retry` | Failed/Blocked/Skipped → Pending |
| POST | `/jobs/{id}/skip` | Atla (damgayı engellemesin) |

### Sosyal Profiller — `/api/v1/social-profiles` (SuperAdmin)
| Method | Path | Açıklama |
|---|---|---|
| GET | `/?language=tr` | Facebook + Instagram profil alanları (kayıt yoksa varsayılan) |
| PUT | `/{provider}?language=` | Alanları kaydet (`{fields}`; anahtarlar sabit, sınırlar doğrulanır) |
| POST | `/{provider}/reset` | Kaydı ve görselleri sil → varsayılan |
| POST | `/{provider}/images/{profile\|cover}` | Görsel yükle (multipart `file`, ≤5 MB) |
| DELETE | `/{provider}/images/{slot}` | Görseli kaldır |
| POST | `/facebook/apply` | Hakkında/açıklama/web/telefon/e-posta + profil fotoğrafını sayfaya bas |
| POST | `/{provider}/mark-applied` | Elle uyguladım işareti (Instagram) |

### Görüşme Takvimi — `/api/v1/appointments` (oturum; tenant filtreli)
| Method | Path | Açıklama |
|---|---|---|
| GET | `/?from=&to=&projectId=&customerId=&assignedUserId=&status=&kind=` | Yerel gün aralığı (≤62 gün) |
| GET | `/customer/{customerId}` | Müşterinin planlı + son randevuları |
| POST | `/` | Elle randevu (`customerId, kind, scheduledAt, durationMinutes?, title?, note?`) |
| PUT | `/{id}` | Düzenle; `status: "Planned"` ile atlanan/ulaşılamayanı geri al |
| POST | `/{id}/complete` | Görüşme kaydı + kapat (`type, reached, subject?, content?, outcome?, satisfactionScore?, nextContactAt?, rescheduleAt?`) |
| POST | `/{id}/skip` | Atla (`{reason?}`) |
| DELETE | `/{id}` | Soft delete |
| GET | `/plan/status?projectId=` | Kotalar, son koşu, çalıştırma yetkisi |
| POST | `/plan/run?projectId=&force=` | CSO planını şimdi koştur (yalnız SuperAdmin) |
| POST | `/plan/rebuild?projectId=` | Ajanın planlı randevularını silip yeniden planla (yalnız SuperAdmin) |

### Lead Ajanı — `/api/v1/lead-agent` (anonim + `X-Agent-Key` = LeadAgent:ApiKey)
| Method | Path | Açıklama |
|---|---|---|
| GET | `/next-cities?count=` | Sıradaki iller (60 günlük rotasyon) |
| POST | `/runs/start` | `{runId, cities[]}` |
| POST | `/candidates` | `{runId, city, items[]}` → created / duplicate / exists / invalid |
| POST | `/runs/{runId}/finish` | `{notes?}` |

### Global Lead Ajanı — `/api/v1/lead-agent/global` (anonim + `X-Agent-Key`)
| Method | Path | Açıklama |
|---|---|---|
| GET | `/next-regions?country=GB&count=` | Sıradaki bölgeler (90 günlük rotasyon, `GlobalLeadRegions`) |
| POST | `/runs/start` | `{runId, country, regions[]}` (LeadAgentRuns.Cities = "GB:…") |
| POST | `/candidates` | `{runId, country, region, items[]}` → created / exists / invalid (GlobalLeads) |
| POST | `/runs/{runId}/finish` | `{notes?}` |

### Global Lead'ler + Outreach — `/api/v1/global-leads` (SuperAdmin)
| Method | Path | Açıklama |
|---|---|---|
| GET | `/?status=&outreach=&country=&search=&page=&pageSize=` | Liste |
| GET | `/meta` | Sayımlar (durum/dizi/ülke), dizi tanımı + şablon konuları + ekler, limitler, gönderici (`shared` = ortak kutu, `source`, `inboxCheck`), son döngü |
| GET | `/pending-count` · `/runs?limit=` | Menü rozeti · global ajan koşuları |
| GET | `/{id}` | Lead + gönderici + e-posta geçmişi |
| PUT | `/{id}` | `{companyName?, contactName?, email?, phone?, website?, city?, notes?}` |
| POST | `/{id}/approve` · `/approve-many` | `{contactName?, email?, startOutreach=true}` → onay + dizi başlat (ortak kutudan oturum kullanıcısının adıyla; ortak kutu yoksa kullanıcının Gmail'i) |
| POST | `/{id}/reject` · `/reject-many` · `/{id}/reopen` | Ret / bekleyene al |
| POST | `/{id}/outreach/start` | `{restart?}` dizi (yeniden) başlat |
| POST | `/{id}/outreach/pause` · `/resume` · `/send-now` · `/check-inbox` | Duraklat / sürdür / sıradaki adımı hemen gönder / IMAP yanıt-bounce kontrolü |
| POST | `/{id}/outreach/stop` | `{reason: replied | unsubscribed | bounced | completed}` |
| POST | `/outreach/run` | Zamanlayıcı döngüsünü şimdi çalıştır |

### Outreach (public) — `/api/v1/outreach`
| Method | Path | Açıklama |
|---|---|---|
| GET/POST | `/unsubscribe/{token}` | Abonelikten çık (HTML sayfa; `List-Unsubscribe` başlığı da buraya) |
| GET/POST | `/unsubscribe/customer/{customerId}/{token}` | Demo yaşam döngüsü e-postaları için müşteri opt-out (HMAC token) |

### Destek Erişimi İzni — `/api/v1/customers/{id}/ems-users/{userId}/access-requests` (oturum) · `/consent` = `/api/v1/support-access` (public, köksüz kısa yol; mail bağlantısı `SupportAccess__PublicBaseUrl` kökünden)
| Method | Path | Açıklama |
|---|---|---|
| GET | `/customers/{id}/ems-users/{userId}/access-requests` | Yürürlükteki onay, bekleyen istek, son 5 istek, gönderici kutusu, müşteri e-postası, `mailLanguage` (tr/en) + `mailLanguageSource` (firma dili / ülke / telefon / varsayılan) |
| POST | `/customers/{id}/ems-users/{userId}/access-requests` | `{to, reason, validHours?, emsUserName?, language?}` → izin e-postası (ortak kutu, destek ekibi adına; operatör adı/adresi mailde yok, Reply-To yok). Dil: `language` → firmanın Liftdesk dili → ülke → telefon öneki → tr; isteğe yazılır |
| POST | `/customers/{id}/ems-users/{userId}/impersonation` | `{reason, accessRequestId?}` — izin verildiyse onaylı + süresi içinde olmalı |
| GET | `/consent/{token}` (= `/api/v1/support-access/{token}`) | Public inceleme sayfası (isteğin dili; eski kayıtta müşteriden türetilir) |
| POST | `/consent/{token}/approve` · `/decline` | Müşterinin yanıtı → Approved (+validHours) / Declined; form action kökü yapılandırılan public kök ya da `X-Forwarded-Proto` + Host (Request.Scheme değil) |

### Demo E-postaları — `/api/v1/lifecycle-mails` (SuperAdmin)
| Method | Path | Açıklama |
|---|---|---|
| GET | `/status` · `/candidates` | Ayar/SMTP/diller/sayımlar/son döngü · sıradakiler (adım, pencere, atlanma sebebi) |
| GET | `/?step=&page=&pageSize=` · `/preview?customerId=&step=` | Gönderim geçmişi · şablon render'ı |
| POST | `/customers/{id}/send` `{step, force?}` · `/run` | Şimdi gönder (aynı adım ikinci kez gitmez) · döngüyü çalıştır |
| POST | `/test` `{to, step, language?}` | Şablonu örnek verilerle verilen adrese gönder ("[TEST]" konu, kayıt tutulmaz) — SMTP/şablon kontrolü |

### Lead Adayları — `/api/v1/lead-candidates` (SuperAdmin)
| Method | Path | Açıklama |
|---|---|---|
| GET | `/?status=&search=&page=&pageSize=` | Liste |
| GET | `/meta` | Sayımlar, projeler, varsayılan proje, ajan anahtarı var mı |
| GET | `/pending-count` | Menü rozeti |
| GET | `/runs?limit=` | Ajan koşuları |
| POST | `/{id}/approve` | Lead müşteri oluştur (`projectId?, companyName?, contactName?, phone?, email?, address?, taxNumber?, label?`) |
| POST | `/{id}/reject` · `/reject-many` | Reddet (parmak izi kalır) |
| POST | `/{id}/duplicate` | `{customerId}` elle mükerrer |
| POST | `/{id}/reopen` | Bekleyene al |

### Site Blog Kuyruğu — `/api/v1/site-blog` (SuperAdmin; Liftdesk `/api/v1/crm/site-blog` proxy)
| Method | Path | Açıklama |
|---|---|---|
| GET | `/locales` | Yerel ayar kayıt defteri (sekme sırası) |
| GET | `/?status=&locale=&page=&pageSize=` | Konular (varyantlar gövdesiz) |
| GET | `/pending-count` · `/{postId}` | Rozet · detay (Markdown dâhil) |
| PUT | `/variants/{id}` | `{title?, description?, slug?, bodyMarkdown?, relatedPageSlug?, publishDate?}` → 400 errors[] (message=SITE_BLOG_VALIDATION) |
| POST | `/variants/{id}/approve` · `/reject` · `/retire` | Durum geçişleri |
| POST | `/{postId}/approve-all` · DELETE `/{postId}` | Toplu onay · soft delete (Published/Retired varsa 409) |
| POST | `/publish` · GET `/publish-status` | Yayın tetiği · durum |
| GET/POST | `/topic-requests` · DELETE `/topic-requests/{id}` | Konu kuyruğu |

### Pazarlama — `/api/v1/marketing` (oturum)
| Method | Path | Açıklama |
|---|---|---|
| GET/PUT/DELETE | `/mail-settings` | Kullanıcının Gmail adresi + uygulama şifresi (şifreli) + gönderen adı + BCC (tanıtım maili yalnız bu hesaptan) |
| POST | `/mail-settings/test` | Kendine test maili |
| GET | `/materials` | Şablonlar (konu seçenekleri) + ekler |
| POST | `/customers/{id}/intro-email` | `{templateKey?, subjectIndex?, note?, to?}` → kullanıcının Gmail'inden gönder (kendine BCC) + E-posta kaydı; ortak kutu kullanılmaz |

### Rakip Firmalar — `/api/v1/competitors` (oturum; tenant filtresi)
| Method | Path | Açıklama |
|---|---|---|
| GET | `/?projectId=&search=` | Proje rakipleri, ada göre; `customerCount` (kaç müşteri bu firmayla çalışıyor) |
| POST | `/` | `{projectId, name, website?, notes?}` → 201; aynı ad (harf duyarsız) varsa mevcut kayıt 200 |
| PUT | `/{id}` | Ad/web/not (ad çakışması 409) |
| DELETE | `/{id}` | Soft delete; müşteri + görüşme kayıtlarındaki `competitorId` boşa düşer |

Görüşme kaydı alanları (`POST/PUT /customers/{id}/contact-histories`, `POST /appointments/{id}/complete`): `usesCompetitor?: bool`, `competitorId?: guid`. `usesCompetitor=true` olan kayıt `Customers.UsesCompetitor/CompetitorId`'ye yansır (`CompetitorMirror`); DTO'larda `competitorName`.

