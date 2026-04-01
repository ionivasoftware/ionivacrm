# Mimari

## Backend — Clean Architecture Katmanları
```
IonCrm.Domain          → Entity'ler, Enum'lar, Interface'ler (IRepository vb.)
IonCrm.Application     → CQRS (MediatR), DTO'lar, Validator'lar, Interface'ler
IonCrm.Infrastructure  → EF Core, Repository'ler, ExternalApis, BackgroundServices
IonCrm.API             → Controller'lar, Middleware, Program.cs
IonCrm.Tests           → xUnit + Moq + FluentAssertions
```

## Backend Kuralları
- **Global query filter:** `ApplicationDbContext` tüm sorgulara tenant + soft-delete filtresi uygular
- **Background job DB sorgusu:** `.IgnoreQueryFilters()` zorunlu — HTTP context yok, tenant filter tüm satırları bloklar
- **DateTime:** `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)` Program.cs'te aktif. External API'lerden gelen tarihler için `DateTime.SpecifyKind(dt, DateTimeKind.Utc)` kullan
- **DB Migration:** EF migration dosyası yok. Schema değişiklikleri Program.cs'teki `app.Lifetime.ApplicationStarted` bloğundaki idempotent raw SQL ile yapılır
  - Yeni kolon: `ALTER TABLE "X" ADD COLUMN IF NOT EXISTS "Y" type;`
  - Tip değişikliği: `DO $$ BEGIN IF EXISTS (...) THEN ALTER COLUMN ... TYPE ... USING ...; END IF; END$$;`
- **SaveChangesAsync hatası:** inner exception'ı mutlaka logla (`ex.InnerException?.Message`)
- **DTO mapping:** Yeni entity alanı eklendiğinde hem DTO sınıfını hem mapping extension'ı güncelle

## Frontend Kuralları
- **API client:** `output/frontend/src/api/client.ts` — axios tabanlı, JWT interceptor
- **State:** Zustand (`authStore`) + React Query (server state)
- **Tipler:** `output/frontend/src/types/index.ts` — tüm tipler buraya
- **API hook'ları:** `output/frontend/src/api/` altında feature bazlı dosyalar (customers.ts, sync.ts vb.)
