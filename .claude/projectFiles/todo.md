# Yapılacaklar

<!-- Buraya yapılacak maddeleri ekle. Claude her oturumda bu dosyayı okur ve sırayla uygular. -->
<!-- Format: - [ ] Açıklama -->

- [ ] Süre Uzat Seçenekleri: Süre Uzat Seçeneklerinde Gün kısmı input olmalı, 3 gün 5 gün... girilebilir.
- [ ] Paraşüt Bağlanıtısı: Paraşüt bağlantısı için bilgileri db de tut, eğer sistem paraşüte bağlı değilse otomatik olarak bağlan.
- [ ] Müşteri Detayları Cari/Fatura: Buraya cariye ait tüm işlemleri paraşütten çekerek oluştur.
- [ ] Fatura oluşturma: CRM de fatura oluşturulduğunda bu önce CRM db sinde oluşturulmalı, ardından bir buton aracılığıyla fatura paraşüte aktarılmalı. 
- [ ] Fatura Aktarımı: Manuel fatura aktarımı sonrası, Müşteri E-Fatura bilgisi çekilip müşteri tablosunda tutulmalı. 
- [ ] Fatura Aktarımı: Eğer müşteri tablosunda E-Fatura bilgisi var ise fatura dbde oluşup otomatik olarak paraşüte aktarılmalı ve resmileştirilmeli.
- [ ] Faturalar ekranında gelen faturalar tek seferlik olarak crm db sine kayıt edillip (Sanki bu dbde oluşmuş gibi) o şekilde yeniden eskiye olacak şekilde gösterilmeli.
- [ ] Sms Yükleme: Süre Uzat a benzer olarak eklenmeli. Kullanımı aşağıdaki gibi;
POST /api/v1/crm/companies/3/add-sms
Authorization: Bearer jBDmsejM1KPhpRMUoV+zQcUuDUASuh29KrpGBIYJclM=
Content-Type: application/json

{ "count": 100 }
Response:

{
  "companyId": 3,
  "smsCount": 350,
  "added": 100
}