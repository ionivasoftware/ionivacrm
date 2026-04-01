# Deploy & CI/CD

## CI/CD Akışı
```
git push origin main
  → GitHub Actions (.github/workflows/deploy.yml):
      1. dotnet test --configuration Release   (438 test)
      2. npm run build                          (frontend)
      3. security-scan                          (hardcoded secret kontrolü)
      4. Railway API → serviceInstanceDeploy    (dev, otomatik)
      5. Railway API → serviceInstanceDeploy    (prod, manuel onay gerekli)
```

## Railway Kuralları
- **Auto Deploy: KAPALI** — deploy sadece GitHub Actions tetikler
- Health check endpoint: `GET /health` → `{"status":"healthy","timestamp":"..."}`
- Migration `app.Lifetime.ApplicationStarted` event'inde arka planda çalışır (health check bloklanmaz)
- Deployment "Queued" kalırsa: tüm aktif deployment'ları iptal et, yeni push yap

## Ortam Değişkenleri (Railway Variables)
| Değişken | Açıklama |
|---|---|
| `ConnectionStrings__DefaultConnection` | Neon DB bağlantı string'i |
| `JwtSettings__Secret` | JWT signing key (min 32 karakter) |
| `SaasA__BaseUrl` | EMS API base URL |
| `SaasA__ApiKey` | EMS global API key |
| `SaasA__ProjectId` | (opsiyonel) Sync hedef proje GUID — yoksa DB fallback |
| `SaasB__BaseUrl` | RezervAl API base URL |
| `SaasB__ApiKey` | RezervAl global API key |

## Servis ID'leri (GitHub Actions workflow)
- Backend Service ID: `987799b6-18b9-4223-81c6-505ffc6717ba`
- Frontend Service ID: `8e6ec2df-e4a1-4fe1-afd9-31c22c5b2e59`
- Dev Environment ID: `c319cc09-f1a9-4d11-8119-a23815b9883a`
