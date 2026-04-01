# Sıradaki Yapılacaklar

<!-- Agent çalışırken buraya ekle. Bitince todo.md'ye taşı. -->
<!-- Format: - [ ] Açıklama -->

- [ ] Müşteri Detayı — EMS Kullanıcı Listesi: Müşteri EMS müşterisiyse (LegacyId sayısal veya "SAASA-{id}" formatında), detay ekranına "Kullanıcılar" sekmesi ekle. Backend: `GET /api/v1/crm/companies/{companyId}/users` endpoint'ini `ISaasAClient`'a ekle (Authorization: Bearer <ems-api-key>). Frontend: kullanıcıları tablo olarak listele (ad, soyad, email, rol, loginName, şifre). Şifre alanı varsayılan gizli (masked), göz ikonuna basınca göster/gizle toggle'ı.

- [ ] RezervAl (SaasB) Entegrasyonu — Firma Sync + Create/Edit: Base URL: `https://rezback.rezerval.com`. Auth: `Authorization: Bearer {rezerval-api-key}` (project tablosundaki `RezervAlApiKey`). (1) Senkronizasyon: `GET /v1/Crm/CompanyList` endpoint'inden tüm firmaları düzenli olarak çekip CRM müşterilerine upsert et. Durum hesaplama EMS ile aynı kurallara göre (`CreatedOn` + `ExperationDate` 40 gün eşiği). Create/edit işlemi sonrası da tetiklensin. (2) Firma Oluşturma: Proje RezervAl ise müşteri oluşturma/düzenleme ekranında "RezervAl'a Gönder" seçeneği/butonu ekle. `POST /v1/Crm/Company` multipart/form-data ile çağır; response'daki `companyId`'yi müşterinin `LegacyId`'si olarak kaydet. (3) Firma Düzenleme: Mevcut RezervAl müşterisini düzenlerken `PUT /v1/Crm/Company` çağır (Id alanı zorunlu). Tüm alanlar: Name, Title, Phone, Email, TaxUnit, TaxNumber, TCNo, IsPersonCompany, Address, Language (default 1), CountryPhoneCode (default 90), ExperationDate, AdminNameSurname, AdminLoginName, AdminPassword, AdminEmail, AdminPhone, Logo (opsiyonel dosya). ISaasBClient arayüzüne bu metodları ekle, SaasBClient'ta implement et.

