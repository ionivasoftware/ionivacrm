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
