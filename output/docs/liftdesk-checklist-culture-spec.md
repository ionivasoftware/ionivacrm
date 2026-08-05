# Liftdesk — Checklist dil (culture) desteği · ✅ UYGULANDI

> **Durum:** Liftdesk tarafında `82f69d4c` ile uygulandı, CRM tarafı da bağlandı.
> Bu doküman geçmiş kaydı olarak duruyor; güncel şema için
> [liftdesk-saas-checklist-contract.md](liftdesk-saas-checklist-contract.md).
>
> Uygulanan hâli: GET/PUT `?culture=`, yanıtta `culture` + `availableCultures`,
> reset gövdesinde `culture`, ve §2'deki veri kaybı — Replace silme predicate'lerine
> `Culture` filtresi eklenerek — giderildi (bakım + arıza, her ikisi).

## (özgün talep)

CRM'deki checklist ekranına **dil seçici** koyabilmemiz için Liftdesk tarafında gereken değişiklikler.
Bu doküman CRM ekibi tarafından, Liftdesk kaynak kodu okunarak yazılmıştır — satır referansları
`ems` reposundaki hâline aittir.

İlgili sözleşme: [liftdesk-saas-checklist-contract.md](liftdesk-saas-checklist-contract.md)

---

## 1. Mevcut durum — dil parametresi YOK

Checklist satırlarında `Culture` (int, 1=TR) alanı var ama **API'de hiçbir yerde parametre değil**.
Sunucu dili firmadan kendisi çözüyor:

```csharp
// CrmChecklistRepository.cs:213
private async Task<int> ResolveCultureAsync(Guid projectId, CancellationToken ct)
    => (await _db.CompanySettings...Select(s => (int?)s.Culture).FirstOrDefaultAsync(ct)) ?? 1;
```

ve okuma sorgularına sabit filtre olarak giriyor:

```csharp
// CrmChecklistRepository.cs:37-41 (GetMaintenanceAsync), 61-64 (GetFaultAsync)
var culture = await ResolveCultureAsync(projectId, cancellationToken);
.Where(h => h.ProjectId == projectId && h.FormId == formId && h.Type == type
         && h.Culture == culture && !h.IsDeleted)
```

Sonuç: CRM firmanın **tek** dilini görüyor, başka dile geçemiyor. `CrmChecklistController`'da
`culture` diye bir query parametresi yok.

---

## 2. ⚠️ Önce düzeltilmesi gereken: yazma tüm dilleri siliyor

Dil desteğinden **bağımsız olarak** mevcut bir veri kaybı riski var. Replace, eski satırları
silerken `Culture`'a bakmıyor:

```csharp
// CrmChecklistRepository.cs:99-103 (ReplaceMaintenanceAsync)
.Where(i => i.ProjectId == projectId && i.FormId == formId && i.Type == type)   // ← Culture YOK
_db.MaintenanceCheckItems.RemoveRange(oldItems);
.Where(h => h.ProjectId == projectId && h.FormId == formId && h.Type == type)   // ← Culture YOK
_db.MaintenanceCheckHeaders.RemoveRange(oldHeaders);
```

Ardından yeni satırlar **tek** culture ile yazılıyor (`Culture = culture`, satır 116/124).

Yani aynı formda TR+EN satırı olan **çok dilli legacy tenant'ta**, CRM'den bir kez "Kaydet"
demek diğer dilin başlık/maddelerini kalıcı olarak siler. (Repo'nun kendi yorumu bu tenant'ların
var olduğunu söylüyor: *"Çok-dilli legacy tenant'ta (aynı formda TR+EN satırlar)…"*, satır 32-34.)

Tek dilli tenant'ta zararsız — o yüzden bugüne kadar fark edilmemiş olabilir.

**Dil seçici eklenirse bu mutlaka düzeltilmeli:** kullanıcı EN listesini açıp kaydettiğinde TR
listesi silinirse özellik faydadan çok zarar verir.

---

## 3. İstenen değişiklikler

### 3.1 Okuma — `?culture=`

```
GET /api/v1/crm/companies/{companyId}/maintenance-checklist?type=1&culture=2
GET /api/v1/crm/companies/{companyId}/fault-checklist?culture=2
```

- `culture` **opsiyonel int**. Verilmezse **bugünkü davranış** (firmanın `CompanySettings.Culture`'ı)
  — geriye tam uyumluluk, CRM'in eski sürümü etkilenmez.
- Bilinmeyen/o firmada satırı olmayan culture → boş `headers` (404 değil).

### 3.2 Yanıta iki alan

```json
{
  "companyId": 7,
  "kind": "maintenance",
  "formId": 4,
  "type": "Elevator",
  "culture": 2,
  "availableCultures": [1, 2],
  "headers": [ … ]
}
```

| alan | tip | neden gerekli |
|---|---|---|
| `culture` | int | Hangi dilin döndüğü; CRM seçiciyi buna göre işaretler |
| `availableCultures` | int[] | **Seçiciyi doldurmak için.** Bu olmadan CRM hangi dilleri deneyeceğini bilemez; tek tek denemek zorunda kalır. O firmada/formda satırı olan distinct `Culture` değerleri (boşsa `[firmanın culture'ı]`) |

> `type` alanının şu an enum **adı** olarak geldiğini not düşelim (`"Elevator"`); CRM iki biçimi de
> kabul edecek şekilde düzeltildi. `culture` int kalırsa sorun yok.

### 3.3 Yazma — `?culture=` + **culture'a scope'lu silme**

```
PUT /api/v1/crm/companies/{companyId}/maintenance-checklist?type=1&culture=2
```

- `culture` verilirse: yeni satırlar o culture ile yazılır **ve silme yalnız o culture'ı kapsar**:

```csharp
.Where(i => i.ProjectId == projectId && i.FormId == formId && i.Type == type
         && i.Culture == culture)      // ← eklenmeli
```

- `culture` verilmezse: bugünkü davranış (firmanın culture'ı) — ama **silme yine o culture'a
  scope'lanmalı** (§2'deki hata). Bu, parametre gönderilmeyen eski çağrılar için de doğru davranış.
- `type` için nasıl "diğer tip korunur" diyorsanız, `culture` için de "diğer diller korunur" olmalı.

### 3.4 Reset

`POST /companies/{companyId}/checklists/reset` gövdesine opsiyonel `culture` eklenebilir.
Eklenmezse firmanın culture'ı kullanılsın; **silme yine culture'a scope'lu** olmalı ki reset bir dili
sıfırlarken diğerini uçurmasın.

### 3.5 Hata kodları

| kod | ne zaman |
|---|---|
| 400 | `culture` int'e çevrilemiyor (ör. `?culture=tr`) |

Bilinmeyen ama geçerli bir int (ör. 99) hata değil — boş liste döner.

---

## 4. CRM tarafı ne yapacak (sizden bir şey gerekmez)

1. `availableCultures` 1'den büyükse toolbar'a **dil seçici** koyacağız (liste seçicinin yanına).
2. Seçilen culture GET ve PUT'ta **birlikte** gönderilecek — tıpkı `type`'ta yaptığımız gibi, çünkü
   yazma da scope'lu olacak.
3. `availableCultures` gelmezse veya tek elemanlıysa seçiciyi hiç göstermeyeceğiz (bugünkü görünüm).

---

## 5. Özet — yapılacaklar listesi

- [ ] **(öncelikli, dilden bağımsız)** Replace silme predicate'lerine `Culture` filtresi ekle —
      `CrmChecklistRepository.cs:99-103` ve fault karşılığı (satır ~165). Çok dilli tenant'ta
      veri kaybını durdurur.
- [ ] GET maintenance/fault → opsiyonel `culture` query paramı.
- [ ] Yanıta `culture` + `availableCultures` alanları.
- [ ] PUT maintenance/fault → opsiyonel `culture` query paramı, yazma+silme o culture'a scope'lu.
- [ ] (opsiyonel) Reset gövdesine `culture`.
- [ ] Sözleşme dokümanını (`liftdesk-saas-checklist-contract.md`) güncelle — §2'ye `culture`,
      §3 veri modeline `availableCultures`.
