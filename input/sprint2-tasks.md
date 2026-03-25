# ION CRM — Sprint 2 Task Listesi

## 🐛 Bug Fixes

### 1. Müşteri Listesi Verisi Gelmiyor
- Customers ekranında DB'den müşteri kayıtları listelenmiyor
- API endpoint test edilecek, frontend bağlantısı düzeltilecek
- 639 müşteri kaydı görünmeli

### 2. Müşteri Ekle Sayfası Boş
- Müşteri ekleme formu boş geliyor
- Form alanları ve submit işlevi düzeltilecek

### 3. Eksik Sayfalar
- Müşteri düzenleme sayfası yok
- Müşteri görüşmelerini görüntüleme sayfası yok
- Müşteri silme butonu yok

---

## ✨ Yeni Özellikler

### 4. Müşteri Label Sistemi
**Backend:**
- CustomerLabel enum: YuksekPotansiyel, Potansiyel, Notr, Vasat, Kotu
- Customer tablosuna Label kolonu ekle (migration)
- Label güncelleme endpoint'i

**Frontend:**
- Müşteri listesinde label badge gösterimi
- Label'a göre filtre (dropdown/chip)
- Müşteri düzenleme formunda kolay label tanımlama
- Görüşme detay sayfasında da label gösterimi

### 5. Müşteri Status Sistemi
**Backend:**
- CustomerStatus enum: Musteri, Potansiyel, Demo
- Customer tablosuna Status kolonu ekle (migration)
- Status güncelleme endpoint'i

**Frontend:**
- Listede status badge gösterimi
- Status'a göre filtre

### 6. Potansiyel → Müşteri Birleştirme
**Backend:**
- Potansiyel müşteriyi mevcut müşteriye bağlama endpoint'i
- İşlem sırası:
  1. Potansiyel müşterinin tüm görüşmeleri hedef müşteriye aktar
  2. Potansiyel müşteri kaydını sil
- Transaction ile yapılacak (atomik)

**Frontend:**
- Potansiyel statüslü müşteri detayında "Müşteriye Bağla" butonu
- Müşteri seçme modal'ı
- Onay dialog'u

### 7. Tüm Görüşmeler Sayfası
**Backend:**
- Tüm görüşmeleri döndüren endpoint (sayfalı)
- Filtreler: tarih aralığı, görüşme tipi, görüşme durumu
- Görüşme durumu enum: Olumlu, Olumsuz, BaşkaTedarikçi

**Frontend:**
- /contact-histories route'u
- Filtre paneli: tarih, tip, durum
- Tablo görünümü: müşteri adı, tarih, tip, durum, notlar

### 8. Pipeline (Arama Planlaması)
**Backend:**
- Pipeline tablosu: müşteri, planlanan tarih, notlar, durum
- CRUD endpoint'leri
- Tarih bazlı sorgular

**Frontend:**
- Müşteri ekranından "Pipeline Ekle" butonu
- /pipeline sayfası: Kanban veya liste görünümü
- Pipeline düzenleme/silme
- Görüşme kaydı girişi pipeline'dan

### 9. Dashboard Pipeline Widget'ı
**Frontend:**
- Sonraki 7 gün içindeki pipeline kayıtları
- Tarih sıralaması ile listeleme
- Widget'tan direkt görüşme kaydı girişi
- Dashboard grafikleri (müşteri sayısı, görüşme istatistikleri, label dağılımı)

---

## 📋 Teknik Notlar

### DB Migration Gereksinimler
```sql
-- Customers tablosuna eklenecek
ALTER TABLE "Customers" ADD "Label" integer NOT NULL DEFAULT 2; -- 0=YuksekPotansiyel,1=Potansiyel,2=Notr,3=Vasat,4=Kotu
ALTER TABLE "Customers" ADD "Status" integer NOT NULL DEFAULT 1; -- 0=Musteri,1=Potansiyel,2=Demo

-- Pipeline tablosu
CREATE TABLE "Pipelines" (
  "Id" uuid PRIMARY KEY,
  "CustomerId" uuid NOT NULL,
  "PlannedDate" timestamptz NOT NULL,
  "Notes" text,
  "Status" integer NOT NULL DEFAULT 0,
  "CreatedAt" timestamptz NOT NULL,
  "UpdatedAt" timestamptz NOT NULL,
  "IsDeleted" boolean NOT NULL DEFAULT false
);

-- ContactHistories tablosuna eklenecek
ALTER TABLE "ContactHistories" ADD "ContactResult" integer; -- 0=Olumlu,1=Olumsuz,2=BaskaTedarikci
```

### API Endpoints
```
GET    /api/v1/customers              — label, status filtresi ekle
POST   /api/v1/customers              — label, status alanları ekle
PUT    /api/v1/customers/{id}         — label, status güncelleme
DELETE /api/v1/customers/{id}         — soft delete
GET    /api/v1/customers/{id}         — detay
POST   /api/v1/customers/{id}/merge   — potansiyeli müşteriye bağla

GET    /api/v1/contact-histories      — tarih, tip, durum filtresi
PUT    /api/v1/contact-histories/{id} — durum güncelleme

GET    /api/v1/pipelines              — sonraki 7 gün filtresi
POST   /api/v1/pipelines
PUT    /api/v1/pipelines/{id}
DELETE /api/v1/pipelines/{id}
POST   /api/v1/pipelines/{id}/contact — pipeline'dan görüşme kaydı

GET    /api/v1/dashboard/stats        — grafik verileri
GET    /api/v1/dashboard/pipeline     — sonraki 7 gün pipeline
```

### Environment
- Development: https://ion-crm-api-development.up.railway.app
- Frontend Dev: https://ion-crm-frontend-development.up.railway.app
- Neon Dev DB: ep-royal-grass-a9u9toyt-pooler.gwc.azure.neon.tech / neondb
