# Yapılacaklar

<!-- Buraya yapılacak maddeleri ekle. Claude her oturumda bu dosyayı okur ve sırayla uygular. -->
<!-- Format: - [ ] Açıklama -->

- [x] Süre Uzat Seçenekleri: Gün kısmı serbest sayı girişi (input) olmalı; Ay ve Yıl seçenekleri buton olarak kalmalı.
- [ ] Paraşüt Bağlantısı: Paraşüt token/credential bilgilerini DB'de tut (ParasutConnection entity). Sistem bağlı değilse (token yok veya expired) otomatik olarak client_credentials ile token al ve kaydet. Her API çağrısında önce token geçerliliğini kontrol et.
- [ ] Müşteri Detayı — Cari Hareketleri sekmesi: Müşterinin Paraşüt'teki tüm cari hareketlerini (satış faturaları + tahsilatlar) listeleyen sekme. Backend: GET /api/v1/customers/{id}/parasut-account-activity → ParasutClient üzerinden müşterinin contact_id'sine ait tüm işlemleri çek. Frontend: Müşteri detay sayfasında yeni bir sekme olarak göster, tarih/tür/tutar kolonları.
- [x] Fatura oluşturma: CRM DB'de önce kaydet, sonra butonla Paraşüt'e aktar.
- [x] Fatura Aktarımı: Manuel aktarım sonrası müşterinin E-Fatura bilgisi Paraşüt'ten çekilip Customer tablosuna kaydedilmeli.
- [x] Fatura Aktarımı: Müşteri tablosunda E-Fatura bilgisi varsa fatura otomatik Paraşüt'e aktarılıp resmileştirilmeli.
- [ ] Faturalar ekranı — Paraşüt import: Mevcut Paraşüt faturalarını tek seferlik CRM DB'ye çek (sanki CRM'de oluşmuş gibi kaydet). Endpoint veya background job: Paraşüt'ten tüm faturaları al, Invoice tablosuna ParasutInvoiceId ile insert et (zaten varsa atla). Faturalar sayfasında yeniden eskiye sıralı göster.
- [ ] SMS Yükleme — Müşteri Detayı: Müşteri detay sayfasında "SMS Yükle" butonu ekle (Süre Uzat butonuna benzer). Tıklanınca miktar girilen modal açılsın. Onaylanınca: (1) EMS API'ye POST /api/v1/crm/companies/{legacyId}/add-sms çağrısı yap, (2) CRM DB'de o müşteri için fatura kaydı oluştur. Backend için mevcut AddSmsCommand'ı müşteri bazlı yeni endpoint'e bağla veya CustomersController'a ekle: POST /api/v1/customers/{id}/add-sms.
