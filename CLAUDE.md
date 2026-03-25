# ION CRM — Agent Team Rules

## Proje
Multi-tenant SaaS CRM — .NET Core 8 + React + PostgreSQL (Neon)

## Repo
https://github.com/ionivasoftware/ionivacrm
Working directory: /root/my-product-team/output

## Tech Stack
- Backend: ASP.NET Core 8, Clean Architecture, EF Core, MediatR, FluentValidation, JWT
- Frontend: React 18, TypeScript, shadcn/ui, Tailwind, Zustand, React Query
- DB: Neon PostgreSQL
- Deploy: Railway (dev + prod)

## Ortamlar
- Dev API: https://ion-crm-api-development.up.railway.app
- Dev Frontend: https://ion-crm-frontend-development.up.railway.app
- Dev DB: Host=ep-royal-grass-a9u9toyt-pooler.gwc.azure.neon.tech;Database=neondb;Username=neondb_owner
- Prod API: https://ion-crm-api-production.up.railway.app
- Prod Frontend: https://ion-crm-frontend-production.up.railway.app
- Prod DB: Host=ep-purple-sound-a9vyag84-pooler.gwc.azure.neon.tech;Database=ioncrm;Username=neondb_owner

## ✅ Sprint 1 — TAMAMLANDI
- .NET Core 8 Clean Architecture solution
- JWT Authentication (login, logout, refresh, me)
- Customers CRUD (temel)
- ContactHistories (temel)
- CustomerTasks (temel)
- Sync endpoints (SaaS A, SaaS B)
- React frontend (dark mode, sidebar, login)
- Railway deploy (dev + prod)
- CI/CD pipeline (GitHub Actions)
- Neon DB migration (639 müşteri, 892 görüşme aktarıldı)

## 🔄 Sprint 2 — AKTİF
Görevler: /root/my-product-team/input/sprint2-tasks.md

### YAPILMAYACAKLAR (Sprint 1'de tamamlandı):
- JWT auth sistemi → YAPILDI
- Temel Customer CRUD → YAPILDI
- Railway deploy → YAPILDI
- CI/CD → YAPILDI
- Neon DB bağlantısı → YAPILDI

### YAPILACAKLAR (Sprint 2):
1. Müşteri listesi verisi gelmiyor → API bağlantısı düzelt
2. Müşteri ekle formu boş → form düzelt
3. Müşteri düzenleme/silme/görüşme sayfaları ekle
4. Label sistemi (YuksekPotansiyel/Potansiyel/Notr/Vasat/Kotu)
5. Status sistemi (Musteri/Potansiyel/Demo)
6. Potansiyel → Müşteri birleştirme
7. Tüm görüşmeler sayfası (filtreli)
8. Pipeline sistemi
9. Dashboard widget + grafikler

## Kurallar
- Mevcut kodu silme, üzerine ekle
- Her değişikten sonra dotnet build çalıştır
- Frontend değişikliklerinde npm run build çalıştır
- Bitince git add -A && git commit && git push origin main
- Sprint 1'deki şeyleri tekrar yapma!
