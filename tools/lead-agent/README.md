# Lead ajanı (tools/lead-agent)

Haftada bir (GitHub Actions, Salı 06:00 UTC) Türkiye'de asansör bakım/montaj firmalarını web'de arar
ve ION CRM'e **aday** olarak yazar. Müşteri oluşturmaz; dedupe sunucuda, onay CRM → **Lead Adayları**
(SuperAdmin).

## Kurulum (bir kez)
1. Railway `ion-crm-api` (production) → `LeadAgent__ApiKey=<rastgele anahtar>`; isteğe bağlı
   `LeadAgent__ProjectId=<hedef proje id>` (yoksa Liftdesk anahtarı olan proje seçilir).
2. GitHub repo → Settings → Secrets → Actions:
   - `CLAUDE_CODE_OAUTH_TOKEN` (Liftdesk'teki sosyal/triage ajanlarıyla aynı token)
   - `CRM_LEAD_AGENT_KEY` (1. adımdaki değerle AYNI)
3. Actions → **Lead Discovery Agent** → Run workflow (ilk koşuyu elle tetikle; `cities` ile il seçilebilir).

## Akış
`GET /api/v1/lead-agent/next-cities` (60 günlük rotasyon, büyük pazarlar önce) → il başına Claude Code
(`WebSearch,WebFetch`) → JSON dizi → `POST /api/v1/lead-agent/candidates`. Sunucu her adayı normalize eder
(isim: Türkçe harf katlama + Ltd/Şti/A.Ş. ayıklama; telefon: ulusal 10 hane; web/e-posta alan adı;
vergi no) ve TÜM müşteriler + TÜM eski adaylarla karşılaştırır: telefon/alan adı/vergi no/isim eşleşmesi →
`duplicate`, eski aday → `exists`, bulanık isim 0.80–0.92 → `Pending` + "olası eşleşme" notu.
Reddedilen adayın parmak izi kalır; aynı firma yeniden önerilmez.

## Kapsam ve etik
Yalnız Türkiye, yalnız firmalar (şahıs yok), yalnız herkese açık sayfalardaki kurumsal iletişim bilgisi.
Google doğrudan kazınmaz; Claude'un WebSearch aracı ve sayfa okuma (WebFetch) kullanılır. İlan/rehber
sitelerinin kullanım koşullarına uyum operatörün sorumluluğundadır; şüphede o kaynağı prompt'tan çıkar.
