# Liftdesk — Ticket "Fix Ajanı Talimatı" Alanı (CRM isteği)

Bu doküman **Liftdesk (EMS) ekibi içindir**. [crm-ticket-api.md](crm-ticket-api.md) sözleşmesine
**tek yeni alan** ekler: `fixInstruction`. Durum makinesi, auth, mevcut alanlar **değişmiyor**.

**CRM tarafı hazır ve deploy edildi** — bu alanı `PATCH .../status` gövdesinde göndermeye ve
`CrmTicketDto`'dan okumaya şimdiden başladı. Siz eklemeden önce Liftdesk gelen alanı yok sayar
(bilinmeyen JSON üyesi atlanır) ve CRM'de alan `null` görünür — kırılma olmaz, sadece çalışmaz.

---

## 1. Neden gerekiyor

Bugün fix ajanı talimatı `agentSuggestedAction` (triyaj ajanı) + `decisionNote` (superadmin) ikilisinden
alıyor. Ama **`decisionNote` tenant'a gösteriliyor** (resmi yanıt) — superadmin oraya
"WorkOrdersPage'e CSV butonu ekle, yeni endpoint açma" gibi teknik talimat yazamıyor.

İhtiyaç: **yalnız CRM'de kalan**, fix ajanına "nasıl yapılacağını" söyleyen ayrı bir alan.

Kural (CRM'in beklediği davranış):
- `fixInstruction` **doluysa** → fix ajanı **buna göre** uygular (birincil kaynak).
- **boşsa** → mevcut davranış: `agentSuggestedAction` + `decisionNote`'a göre uygular.

---

## 2. Yapılacaklar

### 2.1 Entity — `SupportTicket`

`src/Ems.Domain/Entities/SupportTicket.cs`, karar alanlarının (`DecisionNote`/`DecidedBy`/`DecidedAt`)
yanına:

```csharp
/// <summary>
/// CRM-only: fix ajanına "nasıl uygulanacağı" talimatı. Superadmin onay anında yazar.
/// Tenant'a ASLA gösterilmez (DecisionNote'un aksine). Boşsa ajan AgentSuggestedAction'a düşer.
/// </summary>
public string? FixInstruction { get; set; }
```

### 2.2 Migration

Nullable text kolon — mevcut satırlar etkilenmez:

```bash
dotnet ef migrations add AddSupportTicketFixInstruction -p src/Ems.Infrastructure -s src/Ems.API
```

Configuration'da uzunluk sınırı (öneri, `Description` ile tutarlı olsun):
```csharp
builder.Property(t => t.FixInstruction).HasMaxLength(4000);
```

### 2.3 DTO — yalnız CRM görünümüne

`src/Ems.Application/Tickets/DTOs/TicketDtos.cs`:

- **`CrmTicketDto`**: `string? FixInstruction` ekle (mapper'da `t.FixInstruction`).
- **`TicketDto` (tenant görünümü): EKLEME.** Bu alan `agentComment`/`agentSuggestedAction`/`fixBranch`/
  `fixPrUrl`/`failReason`/`decidedBy` ile aynı sınıfta — sızdırılmaz.

> ⚠️ Sözleşmenin can alıcı noktası: tenant `decisionNote` + `resolutionNote` dışında hiçbir iç alanı
> görmemeli. `fixInstruction` **iç alandır**.

### 2.4 PATCH status — gövdeye alan ekle

`src/Ems.API/Controllers/CrmTicketsController.cs`:

```csharp
public record UpdateTicketStatusRequest(
    string Status,
    string? DecidedBy,
    string? DecisionNote,
    string? FixInstruction,      // ← YENİ (opsiyonel, string? — required tuzağı)
    string? FixBranch,
    string? FixPrUrl,
    string? ResolutionNote,
    string? FailReason);
```

`UpdateTicketStatusCommand`'a taşı ve handler'da **yalnız `Approved` geçişinde** yaz:

```csharp
case TicketStatus.Approved:
    Require(t, TicketStatus.Triaged, TicketStatus.New, TicketStatus.Failed);
    ...
    // Boş/eksik gönderilirse ÖNCEKİ talimat KORUNUR (Failed → re-approve'da silinmesin).
    if (!string.IsNullOrWhiteSpace(request.FixInstruction))
        t.FixInstruction = request.FixInstruction.Trim();
    break;
```

**Neden "boşsa koru":** Failed bir ticket yeniden onaylanırken CRM talimatı yeniden gönderir; ama başka
bir akış (ör. ajan retry'ı) boş gönderirse önceki talimat kaybolmamalı. CRM zaten önceki değeri forma
ön-doldurup gönderiyor.

> Not: CRM `fixInstruction`'ı **yalnız `Approved`** ile gönderir; `Rejected`'da hiç göndermez.

### 2.5 Fix ajanı — talimatı önceliklendir

**`tools/ticket-agent/prompt-apply.md`** (asıl iş burada):

- Alan listesine ekle: `fixInstruction` (superadmin'in doğrudan talimatı).
- Öncelik kuralını netleştir, ör.:

```markdown
TICKET_JSON alanları: `subject`, `description`, `platform`, `agentComment`,
`agentSuggestedAction` (triyaj ajanının önerisi), `decisionNote` (superadmin karar notu),
`fixInstruction` (superadmin'in AJANA doğrudan talimatı).

ÖNCELİK: `fixInstruction` DOLUYSA onu birincil kaynak al — kapsamı O belirler; çelişirse
`agentSuggestedAction`'ı DEĞİL `fixInstruction`'ı izle. BOŞSA mevcut davranış: `agentSuggestedAction`
+ `decisionNote`'tan git.
```

- `NOCHANGE` gerekçelerinde de aynı öncelik geçerli (talimat belirsizse yine vazgeç).

**`tools/ticket-agent/apply.sh`** — PR gövdesine ekle (opsiyonel ama faydalı; PR review'da ne
istendiği görünür):

```bash
**Fix talimatı:** \(.fixInstruction // "-")
```

---

## 3. Kabul kriterleri

- [ ] `GET /api/v1/crm/tickets` ve `/{id}` yanıtlarında `fixInstruction` alanı var (yoksa `null`).
- [ ] `GET /api/v1/tickets` (tenant JWT) yanıtında `fixInstruction` **YOK**.
- [ ] `PATCH /{id}/status` `{"status":"Approved","decisionNote":"...","fixInstruction":"..."}` →
      200, alan kaydedilir; sonraki `GET`'te döner.
- [ ] Aynı PATCH `fixInstruction` **olmadan** → önceki değer **korunur** (silinmez).
- [ ] `Rejected` PATCH'i `fixInstruction`'ı değiştirmez.
- [ ] Failed → Approved (re-approve) yeni talimatla → alan güncellenir, `FailReason` temizlenir
      (mevcut davranış).
- [ ] Fix ajanı: `fixInstruction` doluyken PR'ı ona göre açar; boşken eski davranış aynen sürer.

---

## 4. CRM tarafında hazır olan (referans)

| Katman | Durum |
|---|---|
| `LiftdeskTicket` modeli | `FixInstruction` okunuyor |
| `PATCH /api/v1/tickets/{id}/status` proxy | `fixInstruction` gövdede gönderiliyor (yalnız Approved) |
| Onay modalı | "Fix ajanına talimat — kullanıcı GÖRMEZ" alanı; boş bırakılırsa AI önerisi kullanılır uyarısı |
| Detay ekranı | Kaydedilmiş talimat "Fix Ajanına Verilen Talimat" kutusunda gösteriliyor |
| Failed retry | Önceki talimat forma ön-doldurulur, düzenlenip tekrar gönderilir |

Siz alanı ekleyip deploy ettiğiniz anda CRM tarafında ek iş gerekmez — çalışmaya başlar.
