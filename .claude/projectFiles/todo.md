# Yapılacaklar

<!-- Buraya yapılacak maddeleri ekle. Claude her oturumda bu dosyayı okur ve sırayla uygular. -->
<!-- Format: - [ ] Açıklama -->

## Fatura

- [x] Taslak faturaya iskonto ekleme: fatura oluşturma formuna iskonto alanı ekle (% veya ₺ seçilebilir), toplam hesaplamada dikkate alınsın, LinesJson'daki `discountValue` / `discountType` alanları kullanılsın
- [ ] Faturalar sayfası projeden bağımsız olmalı: proje switcher değişse bile tüm yetkili projelerin faturaları gösterilmeli (SuperAdmin için tüm faturalar), `useInvoices` hook'u `projectId` filtresi kaldırılacak veya backend tüm projeleri dönecek

## Müşteri

- [ ] Müşteri detayına "Kullanım Özeti" sekmesi (EMS API): EMS kaynaklı müşterilerde sekme görünür olmalı; `GET /api/v1/crm/companies/{emsCompanyId}/summary` endpoint'i çağrılacak (proje EMS API key ile), aylık bakım/arıza/teklif sayıları bar chart + tablo ile gösterilecek, totals (müşteri/asansör/kullanıcı sayısı) kart olarak üstte yer alacak
- [x] RezervAl projesi seçiliyken müşteri ekleme formu farklı olmalı: "Push to RezervAl" alanlarını içeren özel form gösterilmeli (ad, telefon, e-posta, vergi no/TC, adres, yönetici bilgileri vb.), standart CRM formu değil

## Proje Yönetimi

- [ ] Proje ayarlarında EMS ve RezervAl için ayrı yapılandırma alanları: her proje için `EmsBaseUrl`, `EmsApiKey`, `RezervAlBaseUrl`, `RezervAlApiKey` kaydedilebilmeli; mevcut `Project` entity ve settings sayfası güncellenmeli

## Senkronizasyon

- [ ] 15 dakikalık otomatik sync çalışmıyor: Railway'de hosted service / background job tetiklenmiyor, sadece manuel butona basınca çalışıyor; Railway cron job veya .NET `IHostedService` / Hangfire ile periyodik sync düzeltilmeli

## Paraşüt

- [ ] Paraşüt bağlantısı projeden bağımsız olmalı: hangi proje seçili olursa olsun aynı Paraşüt hesabına bağlanılmalı; `ParasutConnection` tek kayıt olarak tutulmalı (proje ID'ye göre değil global), bağlantı yoksa sistem otomatik bağlanmayı denemeli
- [ ] Paraşüt ürün eşleştirme — RezervAl desteği: "RezervAl Aylık Lisans Bedeli" ürünü eklenebilmeli ve Paraşüt'teki ürünle eşlenebilmeli; EMS'ten farklı olarak fiyat ürün konfigürasyonunda değil **müşteri kaydında** tutulmalı; taslak fatura oluşturulurken müşteriye kayıtlı fiyat kullanılmalı
