# CRM Fiyat Yönetimi API'si

EMS abonelik planı ve SMS paketi fiyatları veritabanında tutulur ve bu API
üzerinden CRM'den yönetilir. Fiyat değişiklikleri **anında** geçerli olur:
web'deki abonelik ekranı fiyatları bu tablolardan okur ve tahsilat tutarı her
zaman sunucu tarafında DB'den hesaplanır.

## Bağlantı

| | |
|---|---|
| Base URL (prod) | `https://<ems-api-domain>/api/v1/crm/pricing` |
| Auth | `Authorization: Bearer <LIFTDESKSAAS_API_KEY>` |
| İçerik | `application/json` (UTF-8) |

- API key EMS tarafında Railway ortam değişkeni **`LIFTDESKSAAS__APIKEY`** ile tanımlanır.
  Key tanımlı değilse tüm uçlar `503` döner; yanlış key `401` döner.
- Tüm yanıtlar `ApiResponse` zarfındadır: `{ "success": bool, "data": ..., "message": string|null, "statusCode": int, "errors": [] }`.

## Alan semantiği (önemli)

- **Fiyatlar KDV HARİÇ net TL'dir.** %20 KDV tahsilat sırasında sunucu tarafından eklenir.
- **`priceYearly` = yıllık TOPLAM tutar** (aylık eşdeğeri değil). Web'deki "aylık
  eşdeğer" gösterimi `priceYearly / 12` ile hesaplanır.
- **`maxUsers` / `maxElevators`: `0` = sınırsız.**
- **`tier` (Standart/Pro/Prime) ve iyzico referans kodları CRM'den değiştirilemez** —
  paket özellik eşlemesi (feature gating) ve iyzico webhook eşleşmesi bunlara
  bağlıdır; yalnız EMS tarafında yönetilir.
- Plan **oluşturma/silme yok**: sistem 3 sabit kademe (Standart/Pro/Prime) üzerine
  kuruludur. Bir planı satıştan kaldırmak için `isActive=false` yapın.
- Fiyat değişikliği **mevcut aktif abonelikleri etkilemez**; yeni satın alma,
  yenileme ve paket yükseltmelerinde yeni fiyat geçerlidir.
- SMS paketinde `DELETE` = pasifleştirme (soft delete). Geçmiş satın almalar bozulmaz.

## Abonelik Planları

### GET /plans — tüm planlar (pasifler dahil)

```bash
curl -H "Authorization: Bearer $LIFTDESKSAAS_API_KEY" \
  https://<ems-api-domain>/api/v1/crm/pricing/plans
```

```json
{
  "success": true,
  "data": [
    {
      "id": "3f1c9a4e-...",
      "name": "EMS Pro",
      "tier": "Pro",
      "description": "Saha operasyonunu büyüten firmalar için",
      "priceMonthly": 599.00,
      "priceYearly": 5988.00,
      "maxUsers": 15,
      "maxElevators": 250,
      "isActive": true,
      "iyzicoProductReferenceCode": null,
      "iyzicoPlanReferenceCodeMonthly": null,
      "iyzicoPlanReferenceCodeYearly": null,
      "createdAt": "2026-07-07T18:52:00Z"
    }
  ]
}
```

### PUT /plans/{id} — plan güncelle

Gövdedeki TÜM alanlar gönderilmelidir (partial update değildir).

```bash
curl -X PUT -H "Authorization: Bearer $LIFTDESKSAAS_API_KEY" -H "Content-Type: application/json" \
  https://<ems-api-domain>/api/v1/crm/pricing/plans/3f1c9a4e-... \
  -d '{
    "name": "EMS Pro",
    "description": "Saha operasyonunu büyüten firmalar için",
    "priceMonthly": 699,
    "priceYearly": 6990,
    "maxUsers": 15,
    "maxElevators": 250,
    "isActive": true
  }'
```

Doğrulama kuralları: `name` zorunlu (≤200), `priceMonthly`/`priceYearly` > 0,
`maxUsers`/`maxElevators` ≥ 0. Hatalı gövde `400` + `errors[]` döner; bilinmeyen
id `404` döner.

## SMS Paketleri

### GET /sms-packages — tüm paketler (pasifler dahil)

```json
{
  "success": true,
  "data": [
    { "id": "…", "name": "1000 SMS Paketi", "smsCount": 1000, "price": 179.00, "isActive": true, "createdAt": "…" }
  ]
}
```

### POST /sms-packages — yeni paket

```bash
curl -X POST -H "Authorization: Bearer $LIFTDESKSAAS_API_KEY" -H "Content-Type: application/json" \
  https://<ems-api-domain>/api/v1/crm/pricing/sms-packages \
  -d '{ "name": "10000 SMS Paketi", "smsCount": 10000, "price": 1299 }'
```

`201` + oluşturulan paket döner. `smsCount` yüklenecek toplam kredidir.

### PUT /sms-packages/{id} — paket güncelle

```bash
curl -X PUT -H "Authorization: Bearer $LIFTDESKSAAS_API_KEY" -H "Content-Type: application/json" \
  https://<ems-api-domain>/api/v1/crm/pricing/sms-packages/<id> \
  -d '{ "name": "1000 SMS Paketi", "smsCount": 1000, "price": 199, "isActive": true }'
```

`isActive` gönderilmezse `true` varsayılır.

### DELETE /sms-packages/{id} — paketi satıştan kaldır

```bash
curl -X DELETE -H "Authorization: Bearer $LIFTDESKSAAS_API_KEY" \
  https://<ems-api-domain>/api/v1/crm/pricing/sms-packages/<id>
```

Soft delete: paket `isActive=false` olur, satın alma geçmişi korunur. Tekrar
satışa açmak için `PUT` ile `isActive=true` gönderin.

## Hata kodları

| Kod | Anlamı |
|---|---|
| 400 | Doğrulama hatası — detay `errors[]` içinde |
| 401 | API key eksik/yanlış |
| 404 | Plan/paket bulunamadı |
| 503 | EMS tarafında `LIFTDESKSAAS__APIKEY` tanımlı değil |

## Notlar

- Uçlar idempotent PUT/DELETE semantiği kullanır; retry güvenlidir (POST hariç —
  POST'u yinelemeden önce GET ile listeyi kontrol edin).
- Rate limit uygulanmaz; yine de makine-makine senkronunda dakikada birkaç
  istekten fazlasına gerek yoktur.
- Fiyat geçmişi (audit) tutulmaz; değişiklik log'ları EMS sunucu loglarına yazılır.
