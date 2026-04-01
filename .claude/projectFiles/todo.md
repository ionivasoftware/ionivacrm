# Yapılacaklar

<!-- Buraya yapılacak maddeleri ekle. Claude her oturumda bu dosyayı okur ve sırayla uygular. -->
<!-- Format: - [ ] Açıklama -->

- [x] [BACKEND] Müşteri Detayı — EMS Kullanıcı Listesi: `ISaasAClient`'a `GetCompanyUsersAsync(string apiKey, int companyId, CancellationToken)` metodu ekle. EMS endpoint: `GET /api/v1/crm/companies/{companyId}/users` (Authorization: Bearer <ems-api-key>). Response model: `EmsCompanyUser(string UserId, string Name, string Surname, string Email, string Role, string LoginName, string Password)`. Yeni CRM endpoint: `GET /api/v1/customers/{id}/ems-users` — müşteri EMS müşterisiyse (LegacyId sayısal veya "SAASA-{n}" formatında) companyId parse et, EMS'ten kullanıcıları çek ve döndür. EMS müşterisi değilse 400 döndür.

- [x] [FRONTEND] Müşteri Detayı — EMS Kullanıcı Listesi: Müşteri EMS müşterisiyse detay ekranına "Kullanıcılar" sekmesi ekle. `customers.ts`'e `useCustomerEmsUsers(customerId)` hook'u ekle. Kullanıcıları tablo olarak listele: Ad Soyad, E-posta, Rol, Kullanıcı Adı, Şifre. Şifre sütunu varsayılan gizli (••••••••), göz ikonuna basınca göster/gizle toggle'ı.

- [ ] [BACKEND] RezervAl Firma Sync: `ISaasBClient`'a `GetRezervalCompaniesAsync` metodunu ekle. Base URL: `https://rezback.rezerval.com`. Endpoint: `GET /v1/Crm/CompanyList` (Authorization: Bearer {RezervAlApiKey}). Response: `RezervalCompany(int Id, string Name, string Title, string Phone, string Email, string? Logo, DateTime ExperationDate, DateTime CreatedOn, bool IsDeleted, bool IsActiveOnline)`. `SaasSyncJob`'da `SyncRezervalCompaniesAsync` metodunu yaz: tüm firmaları çekip CRM müşterilerine upsert et. Durum hesaplama EMS ile aynı kural (CreatedOn + ExperationDate 40 gün eşiği, `ComputeStatusFromExpiration` kullan). LegacyId formatı: `"REZV-{id}"`.

- [ ] [BACKEND] RezervAl Firma Create/Edit: `ISaasBClient`'a `CreateRezervalCompanyAsync` ve `UpdateRezervalCompanyAsync` metodları ekle. POST/PUT `https://rezback.rezerval.com/v1/Crm/Company` multipart/form-data. Yeni CRM endpoint'leri: `POST /api/v1/customers/{id}/push-to-rezerval` (müşteriyi RezervAl'a gönder/güncelle). Create sonrası response'daki `companyId`'yi müşterinin LegacyId'si olarak kaydet (`"REZV-{companyId}"`). Alanlar: Name, Title, Phone, Email, TaxUnit, TaxNumber, TCNo, IsPersonCompany, Address, Language (default 1), CountryPhoneCode (default 90), ExperationDate, AdminNameSurname, AdminLoginName, AdminPassword, AdminEmail, AdminPhone, Logo (opsiyonel).

- [ ] [FRONTEND] RezervAl Firma Create/Edit: Proje RezervAl ise müşteri detay sayfasına "RezervAl'a Gönder" butonu ekle. Müşteri henüz RezervAl'a gönderilmemişse (LegacyId "REZV-" ile başlamıyorsa) create, başlıyorsa update çağrısı yapsın. `customers.ts`'e `usePushToRezerval(customerId)` hook'u ekle.

