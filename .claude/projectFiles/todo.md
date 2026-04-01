# Yapılacaklar

<!-- Buraya yapılacak maddeleri ekle. Claude her oturumda bu dosyayı okur ve sırayla uygular. -->
<!-- Format: - [ ] Açıklama -->

- [x] Ayarlar Sayfası — SMS Yükleme Widget'ı Kaldır: Ayarlar (Settings) sayfasındaki SMS yükleme bölümünü/widget'ını kaldır. SMS yükleme işlemi müşteri detay sayfasından yapılıyor, ayarlarda gerekmez.

- [x] Senkronizasyon — Yalnızca EMS→CRM Müşteri Aktif + Durum Kuralları Düzeltmesi: Diğer tüm sync işlemlerini (RezervAl vb.) şimdilik devre dışı bırak; sadece EMS→CRM Müşteri sync'i çalışsın. `SyncSaasAAsync` ve `SyncSaasBAsync` içindeki Customer/Subscription/Order sync çağrılarını devre dışı bırak (sadece `SyncEmsCrmCustomersAsync` kalsın). `SyncEmsCrmCustomersAsync` içindeki `updatedSince` delta sync mantığını tamamen kaldır — her çalışmada her sayfayı çekecek şekilde her zaman tam (full) sync yap; böylece `ExpirationDate` geçen müşterilerin durumu EMS'ten güncelleme gelmese bile otomatik yeniden hesaplanır. `ComputeStatusFromExpiration` fonksiyonunu aşağıdaki kurallara göre düzelt:
  - CreatedAt + 40 gün > ExpirationDate  VE  bugün < ExpirationDate  → Demo   (kısa deneme süresi, henüz dolmamış)
  - CreatedAt + 40 gün > ExpirationDate  VE  ExpirationDate < bugün  → Pasif  (kısa deneme süresi, süresi dolmuş)
  - CreatedAt + 40 gün < ExpirationDate  VE  ExpirationDate < bugün  → Churn  (gerçek müşteri, süresi dolmuş)
  - CreatedAt + 40 gün < ExpirationDate  VE  bugün < ExpirationDate  → Aktif  (gerçek müşteri, süresi dolmamış)
  SaasACustomer modelinde `CreatedAt` ve `ExpirationDate` alanlarının doğru map edildiğinden emin ol.

- [x] Müşteri Detayı — Cari Hareketleri Sekmesini Kaldır: Müşteri detay sayfasında Faturalar/Cari sekmesinin altındaki "Cari Hareketleri" alt sekmesini kaldır. Sadece faturalar listesi yeterli. İlgili backend endpoint ve frontend kodu temizlenebilir.

- [x] Müşteri Detayı — Fatura Tutarı Düzeltmesi: Faturalar listesinde "Tutar" sütunu şu an vergi hariç tutarı (net_total) gösteriyor, "Ödenen" sütunu ise vergi dahil tutarı gösteriyor. Tutarlılık için "Tutar" sütununu da vergi dahil tutar (gross_total) olarak göster.

- [x] [BACKEND] Paraşüt Ürün Eşleştirmesi: `ParasutProduct` entity'si oluştur (Id, ProjectId, ProductKey string, ParasutProductId string, ParasutProductName string, UnitPrice decimal, VatRate decimal). Migration ekle (`ADD COLUMN IF NOT EXISTS` pattern). Repository ve CRUD komutlarını yaz: GetParasutProducts, SaveParasutProduct. ProductKey sabit değerler: "membership_monthly", "membership_yearly", "sms_1000", "sms_2500", "sms_5000", "sms_10000". `ExtendEmsExpiration` ve `AddCustomerSms` command handler'larında taslak fatura oluştururken artık sabit fiyat yerine `ParasutProduct` tablosundan fiyat/vergi bilgisini çek (kayıt yoksa mevcut davranışı koru).

- [x] [FRONTEND] Paraşüt Ürün Eşleştirmesi: `SettingsPage.tsx`'e Paraşüt ürün eşleştirme kartı ekle. 6 ürün için (1 Aylık Üyelik, 1 Yıllık Üyelik, 1000/2500/5000/10000 SMS) Paraşüt ürün listesinden arama/seçim yapılabilsin. Her satırda: ürün adı etiketi, Paraşüt ürün dropdown/arama, kaydet butonu. `parasut.ts`'e `useParasutProducts` ve `useSaveParasutProduct` hook'larını ekle, `types/index.ts`'e `ParasutProduct` tipini ekle.

- [x] [BACKEND] "Fırsatlar" → "Pipeline" + Değer Alanı Kaldır: `Opportunity` entity ve DTO'larında `Value`/`value` alanını kaldır. İlgili migration ile DB kolonunu kaldır (DROP COLUMN IF EXISTS). API response ve request modellerinden `value` alanını temizle. Controller ve handler'larda value'ya yapılan referansları kaldır.

- [x] [FRONTEND] "Fırsatlar" → "Pipeline" Yeniden Adlandırma: Menü, sayfa başlıkları, route etiketlerinde "Fırsatlar" → "Pipeline" olarak değiştir. `PipelinePage.tsx`'ten ve `ReportsPage.tsx`'teki pipeline widget'ından "Değer" sütunu/alanını kaldır. `types/index.ts`'te `Opportunity` tipinden `value` alanını kaldır. `DashboardStats`'tan `pipelineValue`/`openOpportunities` value widget'ını kaldır.

