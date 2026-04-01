# Çalışma Kuralları

## Her Değişiklik Sonrası
1. `dotnet build IonCrm.sln --no-restore` — backend değişikliği
2. `npm run build` — frontend değişikliği (`output/frontend/` dizininde)
3. `git add -A && git commit -m "..." && git push origin main`

## Kod Kuralları
- Mevcut kodu silme — üzerine ekle
- Yeni DB kolonu → `Program.cs` startup SQL bloğuna `ADD COLUMN IF NOT EXISTS` ekle
- Tip değişikliği → `DO $$ BEGIN IF EXISTS (...data_type check...) THEN ALTER COLUMN ... END IF; END$$;`
- Background job DB sorgusu → `.IgnoreQueryFilters()` zorunlu (tenant filter HTTP context olmadan bloklar)
- External API tarihleri → `DateTime.SpecifyKind(dt, DateTimeKind.Utc)` (Npgsql Unspecified reddeder)
- EF `SaveChangesAsync` hatası → inner exception logla: `ex.InnerException?.Message`
- Yeni entity alanı → hem DTO sınıfını hem mapping extension'ı güncelle

## Commit Mesajı Formatı
```
feat: kısa açıklama
fix: kısa açıklama
ci: kısa açıklama
```

## Test
- Tüm testler `output/tests/IonCrm.Tests/` altında
- Background job testlerinde `ApplicationDbContext` InMemory ile mock'lanır
- `dotnet test tests/IonCrm.Tests/IonCrm.Tests.csproj --no-build`
