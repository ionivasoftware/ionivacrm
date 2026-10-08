import { useMemo, useState } from 'react';
import {
  Globe, Eye, Users, UserPlus, CheckCircle2, AlertTriangle, Loader2, RefreshCw, MousePointerClick,
} from 'lucide-react';
import {
  AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer,
} from 'recharts';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import {
  useSiteOverview, useSiteEvents, useSiteSilenceProbe, fillDaily, todayUtc, addDaysUtc,
  type SiteBucket, type SiteFilters,
} from '@/api/siteAnalytics';

const SITE_LABEL: Record<string, string> = { tr: 'Türkiye (kök)', en: '/en/', uk: '/uk/' };
const DEVICE_TR: Record<string, string> = { mobile: 'Mobil', tablet: 'Tablet', desktop: 'Masaüstü' };
const SECTION_TR: Record<string, string> = {
  why: 'Neden', features: 'Özellikler', mobile: 'Mobil', 'how-it-works': 'Nasıl çalışır', pricing: 'Fiyatlandırma', faq: 'SSS',
};
const EVENT_TR: Record<string, string> = {
  PageView: 'Sayfa', SectionView: 'Bölüm', Click: 'Tıklama', Scroll: 'Kaydırma', SignupStart: 'Kayıt başladı', SignupVerified: 'Firma açıldı',
};

function pct(n: number, d: number): string {
  if (!d) return '—';
  return `${((n / d) * 100).toFixed(1)}%`;
}
function fmtDay(day: string): string {
  const d = new Date(`${day}T00:00:00Z`);
  return new Intl.DateTimeFormat('tr-TR', { day: 'numeric', month: 'short', timeZone: 'UTC' }).format(d);
}

/** Kırılım tablosu — anahtar · sayı · ziyaretçi. */
function BucketTable({ title, rows, labelOf, countLabel = 'Sayı' }: {
  title: string; rows: SiteBucket[] | null | undefined; labelOf?: (k: string) => string; countLabel?: string;
}) {
  const data = rows ?? [];
  const max = Math.max(1, ...data.map(r => r.count));
  return (
    <Card>
      <CardContent className="p-4">
        <h3 className="text-sm font-semibold text-foreground mb-2">{title}</h3>
        {data.length === 0 ? (
          <p className="text-xs text-muted-foreground">Veri yok</p>
        ) : (
          <table className="w-full text-xs">
            <thead>
              <tr className="text-muted-foreground">
                <th className="text-left font-medium pb-1">Anahtar</th>
                <th className="text-right font-medium pb-1">{countLabel}</th>
                <th className="text-right font-medium pb-1">Ziyaretçi</th>
              </tr>
            </thead>
            <tbody>
              {data.slice(0, 12).map(r => (
                <tr key={r.key} className="border-t border-border/40">
                  <td className="py-1 pr-2 relative">
                    <span className="absolute inset-y-0 left-0 bg-primary/10 rounded" style={{ width: `${(r.count / max) * 100}%` }} />
                    <span className="relative truncate block max-w-[220px]" title={r.key}>{labelOf ? labelOf(r.key) : r.key}</span>
                  </td>
                  <td className="py-1 text-right tabular-nums">{r.count.toLocaleString('tr-TR')}</td>
                  <td className="py-1 text-right tabular-nums text-muted-foreground">{r.visitors.toLocaleString('tr-TR')}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </CardContent>
    </Card>
  );
}

export function SiteAnalyticsPage() {
  const today = todayUtc();
  const [preset, setPreset] = useState<7 | 30 | 90 | 'custom'>(30);
  const [customFrom, setCustomFrom] = useState(addDaysUtc(today, -29));
  const [customTo, setCustomTo] = useState(today);
  const [site, setSite] = useState<string>('');
  const [country, setCountry] = useState('');
  const [eventType, setEventType] = useState('');

  const filters: SiteFilters = useMemo(() => {
    const to = preset === 'custom' ? customTo : today;
    const from = preset === 'custom' ? customFrom : addDaysUtc(today, -(preset - 1));
    return { from, to, site: site || null, country: country.trim().toUpperCase() || null };
  }, [preset, customFrom, customTo, site, country, today]);

  const countryValid = !filters.country || /^[A-Z]{2}$/.test(filters.country);
  const { data, isLoading, isError, error, refetch, isFetching } = useSiteOverview(filters, countryValid);
  const { data: events = [], isFetching: eventsFetching } = useSiteEvents(
    { site: filters.site, country: filters.country }, eventType || null, 50, countryValid);
  const { data: probe } = useSiteSilenceProbe(true);

  const daily = useMemo(() => fillDaily(data?.daily, filters.from, filters.to), [data, filters.from, filters.to]);

  // Sözleşme §6.8 — sessizlik = ölçüm kopmuş olabilir. Yoklama HAM olaylardan, süzgeçsiz ve anlık
  // (useSiteSilenceProbe); seçili dönem/süzgeçten bağımsız bir site-sağlığı sinyali olduğu için
  // her zaman gösterilir.
  const silent = probe?.silent === true;

  const t = data?.totals;
  const errMsg =
    (error as { response?: { data?: { errors?: string[]; message?: string } } })?.response?.data?.errors?.[0] ??
    (error as { response?: { data?: { message?: string } } })?.response?.data?.message ??
    'Site verisi alınamadı.';

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-foreground flex items-center gap-2">
            <Globe className="h-6 w-6" /> Site Kullanımı
          </h1>
          <p className="text-muted-foreground text-sm mt-1">
            liftdesk.app — birinci taraf, çerezsiz ölçüm; IP saklanmaz. &ldquo;Ziyaretçi&rdquo; günlük tekil
            toplamıdır: aynı kişi iki ayrı günde iki sayılır.
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={() => refetch()} disabled={isFetching}>
          <RefreshCw className={`h-4 w-4 mr-1.5 ${isFetching ? 'animate-spin' : ''}`} /> Yenile
        </Button>
      </div>

      {/* Süzgeçler */}
      <div className="flex flex-wrap items-center gap-2">
        <div className="flex rounded-md border border-input overflow-hidden">
          {([7, 30, 90] as const).map(p => (
            <button key={p} onClick={() => setPreset(p)}
              className={`px-3 py-1.5 text-sm transition-colors ${preset === p ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:text-foreground'}`}>
              {p} gün
            </button>
          ))}
          <button onClick={() => setPreset('custom')}
            className={`px-3 py-1.5 text-sm transition-colors ${preset === 'custom' ? 'bg-primary text-primary-foreground' : 'text-muted-foreground hover:text-foreground'}`}>
            Özel
          </button>
        </div>
        {preset === 'custom' && (
          <>
            <input type="date" value={customFrom} max={customTo} onChange={e => setCustomFrom(e.target.value)}
              className="h-9 rounded-md border border-input bg-transparent px-2 text-sm" />
            <span className="text-muted-foreground text-sm">—</span>
            <input type="date" value={customTo} min={customFrom} max={today} onChange={e => setCustomTo(e.target.value)}
              className="h-9 rounded-md border border-input bg-transparent px-2 text-sm" />
          </>
        )}
        <select value={site} onChange={e => setSite(e.target.value)}
          className="h-9 rounded-md border border-input bg-transparent px-2 text-sm">
          <option value="">Tüm siteler</option>
          <option value="tr">Türkiye (kök)</option>
          <option value="en">/en/</option>
          <option value="uk">/uk/</option>
        </select>
        <input value={country} onChange={e => setCountry(e.target.value)} placeholder="Ülke (GB)" maxLength={2}
          className={`h-9 w-24 rounded-md border bg-transparent px-2 text-sm uppercase ${countryValid ? 'border-input' : 'border-destructive'}`} />
      </div>

      {silent && (
        <div className="flex items-start gap-2 rounded-lg border border-amber-500/50 bg-amber-500/5 px-4 py-3 text-sm">
          <AlertTriangle className="h-4 w-4 text-amber-500 shrink-0 mt-0.5" />
          <div>
            <span className="font-medium text-foreground">Son 24 saatte hiç görüntüleme yok.</span>{' '}
            <span className="text-muted-foreground">Site canlıysa bu, ölçümün kopmuş olabileceği anlamına gelir — tracker ya da ingest ucu kontrol edilmeli. Sessizlik başarı değildir.</span>
          </div>
        </div>
      )}

      {isLoading && (
        <div className="flex items-center gap-2 text-muted-foreground text-sm"><Loader2 className="h-4 w-4 animate-spin" /> Yükleniyor…</div>
      )}
      {isError && (
        <div className="flex items-center gap-2 text-sm text-red-600 dark:text-red-400"><AlertTriangle className="h-4 w-4" /> {errMsg}</div>
      )}

      {data && t && (
        <>
          {/* Toplamlar + dönüşüm */}
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
            {[
              { label: 'Görüntüleme', value: t.pageViews, icon: Eye },
              { label: 'Ziyaretçi (günlük tekil)', value: t.visitors, icon: Users },
              { label: 'Kayıt başlangıcı', value: t.signupStarts, icon: UserPlus, sub: `ziyaretçinin ${pct(t.signupStarts, t.visitors)}` },
              { label: 'Açılan firma', value: t.signupVerified, icon: CheckCircle2, sub: `başlayanın ${pct(t.signupVerified, t.signupStarts)} · ziyaretçinin ${pct(t.signupVerified, t.visitors)}` },
            ].map(c => (
              <Card key={c.label}>
                <CardContent className="p-4">
                  <div className="flex items-center justify-between">
                    <span className="text-xs text-muted-foreground">{c.label}</span>
                    <c.icon className="h-4 w-4 text-muted-foreground" />
                  </div>
                  <div className="text-2xl font-bold tabular-nums mt-1">{c.value.toLocaleString('tr-TR')}</div>
                  {c.sub && <div className="text-xs text-muted-foreground mt-0.5">{c.sub}</div>}
                </CardContent>
              </Card>
            ))}
          </div>

          {/* Günlük seri */}
          <Card>
            <CardContent className="p-4">
              <div className="flex items-baseline justify-between mb-2">
                <h3 className="text-sm font-semibold text-foreground">
                  Günlük görüntüleme ve ziyaretçi
                  {filters.to === today && (
                    <span className="ml-2 text-xs font-normal text-muted-foreground" title="Pano 5 dakikada bir yenilenen özetten okur; bugünün sayıları en çok 5 dk geriden gelir. Canlı akış ise anlıktır.">
                      · bugün ≤ 5 dk geriden
                    </span>
                  )}
                </h3>
                {t.averageScrollDepth != null && (
                  <span className="text-xs text-muted-foreground">Ort. kaydırma derinliği {t.averageScrollDepth.toFixed(0)}%</span>
                )}
              </div>
              <ResponsiveContainer width="100%" height={220}>
                <AreaChart data={daily}>
                  <defs>
                    <linearGradient id="pvGrad" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="5%" stopColor="#6366f1" stopOpacity={0.35} /><stop offset="95%" stopColor="#6366f1" stopOpacity={0} />
                    </linearGradient>
                    <linearGradient id="visGrad" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="5%" stopColor="#22c55e" stopOpacity={0.35} /><stop offset="95%" stopColor="#22c55e" stopOpacity={0} />
                    </linearGradient>
                  </defs>
                  <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" />
                  <XAxis dataKey="day" tickFormatter={fmtDay} tick={{ fontSize: 11 }} stroke="hsl(var(--muted-foreground))" minTickGap={24} />
                  <YAxis tick={{ fontSize: 11 }} stroke="hsl(var(--muted-foreground))" allowDecimals={false} width={36} />
                  <Tooltip
                    labelFormatter={(d) => fmtDay(String(d))}
                    contentStyle={{ background: 'hsl(var(--card))', border: '1px solid hsl(var(--border))', borderRadius: 8, fontSize: 12 }}
                  />
                  <Area type="monotone" dataKey="pageViews" name="Görüntüleme" stroke="#6366f1" fill="url(#pvGrad)" strokeWidth={2} />
                  <Area type="monotone" dataKey="visitors" name="Ziyaretçi" stroke="#22c55e" fill="url(#visGrad)" strokeWidth={2} />
                </AreaChart>
              </ResponsiveContainer>
            </CardContent>
          </Card>

          {/* Huni — site başına üç çubuk */}
          <Card>
            <CardContent className="p-4">
              <h3 className="text-sm font-semibold text-foreground mb-3">Kayıt hunisi (site başına)</h3>
              {(data.funnel ?? []).length === 0 ? (
                <p className="text-xs text-muted-foreground">Veri yok</p>
              ) : (
                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                  {(data.funnel ?? []).map(f => {
                    const max = Math.max(1, f.signupPageViews, f.signupStarts, f.signupVerified);
                    const steps = [
                      { l: 'Kayıt sayfası', v: f.signupPageViews, c: '#6366f1' },
                      { l: 'Kod istendi', v: f.signupStarts, c: '#f59e0b' },
                      { l: 'Firma açıldı', v: f.signupVerified, c: '#22c55e' },
                    ];
                    return (
                      <div key={f.site} className="space-y-1.5">
                        <div className="text-xs font-medium text-foreground">{SITE_LABEL[f.site] ?? f.site}
                          <span className="text-muted-foreground font-normal"> · dönüşüm {pct(f.signupVerified, f.signupPageViews)}</span>
                        </div>
                        {steps.map(s => (
                          <div key={s.l} className="flex items-center gap-2 text-xs">
                            <span className="w-24 text-muted-foreground shrink-0">{s.l}</span>
                            <div className="flex-1 h-4 rounded bg-muted/40 overflow-hidden">
                              <div className="h-full rounded" style={{ width: `${(s.v / max) * 100}%`, background: s.c }} />
                            </div>
                            <span className="w-10 text-right tabular-nums">{s.v}</span>
                          </div>
                        ))}
                      </div>
                    );
                  })}
                </div>
              )}
            </CardContent>
          </Card>

          {/* Ülkeler + sayfalar */}
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
            <Card>
              <CardContent className="p-4">
                <h3 className="text-sm font-semibold text-foreground mb-2">Ülkeler</h3>
                <table className="w-full text-xs">
                  <thead><tr className="text-muted-foreground">
                    <th className="text-left font-medium pb-1">Ülke</th><th className="text-right font-medium pb-1">Görüntüleme</th>
                    <th className="text-right font-medium pb-1">Ziyaretçi</th><th className="text-right font-medium pb-1">Kayıt</th><th className="text-right font-medium pb-1">Açıldı</th>
                  </tr></thead>
                  <tbody>
                    {(data.countries ?? []).slice(0, 15).map(c => (
                      <tr key={c.country} className="border-t border-border/40">
                        <td className="py-1 font-medium">{c.country === 'unknown' ? 'Bilinmiyor' : c.country}</td>
                        <td className="py-1 text-right tabular-nums">{c.pageViews.toLocaleString('tr-TR')}</td>
                        <td className="py-1 text-right tabular-nums text-muted-foreground">{c.visitors.toLocaleString('tr-TR')}</td>
                        <td className="py-1 text-right tabular-nums">{c.signupStarts}</td>
                        <td className="py-1 text-right tabular-nums">{c.signupVerified}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </CardContent>
            </Card>
            <Card>
              <CardContent className="p-4">
                <h3 className="text-sm font-semibold text-foreground mb-2">Sayfalar</h3>
                <table className="w-full text-xs">
                  <thead><tr className="text-muted-foreground">
                    <th className="text-left font-medium pb-1">Yol</th><th className="text-right font-medium pb-1">Görüntüleme</th><th className="text-right font-medium pb-1">Ziyaretçi</th>
                  </tr></thead>
                  <tbody>
                    {(data.pages ?? []).slice(0, 15).map(p => (
                      <tr key={`${p.site}-${p.path}`} className="border-t border-border/40">
                        <td className="py-1 font-mono truncate max-w-[260px]" title={p.path}>{p.path}</td>
                        <td className="py-1 text-right tabular-nums">{p.pageViews.toLocaleString('tr-TR')}</td>
                        <td className="py-1 text-right tabular-nums text-muted-foreground">{p.visitors.toLocaleString('tr-TR')}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </CardContent>
            </Card>
          </div>

          {/* "Sayfada nereye bakıyor" */}
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            <BucketTable title="Bölüm görünürlüğü" rows={data.sections} labelOf={k => SECTION_TR[k] ?? k} countLabel="Görünme" />
            <BucketTable title="Kaydırma derinliği" rows={data.scrollDepth} labelOf={k => `%${k}`} />
            <BucketTable title="Tıklamalar (hedef@yer)" rows={data.clicks} />
          </div>

          {/* Kaynaklar */}
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
            <BucketTable title="Yönlendirenler" rows={data.referrers} />
            <BucketTable title="Kampanyalar (utm)" rows={data.campaigns} />
            <BucketTable title="Cihazlar" rows={data.devices} labelOf={k => DEVICE_TR[k] ?? k} />
            <BucketTable title="Diller" rows={data.languages} />
          </div>

          {/* Canlı akış */}
          <Card>
            <CardContent className="p-4">
              <div className="flex flex-wrap items-center justify-between gap-2 mb-2">
                <h3 className="text-sm font-semibold text-foreground flex items-center gap-2">
                  <MousePointerClick className="h-4 w-4" /> Canlı akış
                  <span className="text-xs font-normal text-muted-foreground">en son olaylar · 30 sn'de bir yenilenir · dönem süzgecinden bağımsız{eventsFetching ? ' · yenileniyor…' : ''}</span>
                </h3>
                <select value={eventType} onChange={e => setEventType(e.target.value)}
                  className="h-8 rounded-md border border-input bg-transparent px-2 text-xs">
                  <option value="">Tüm olaylar</option>
                  <option value="pageview">Sayfa</option>
                  <option value="section_view">Bölüm</option>
                  <option value="click">Tıklama</option>
                  <option value="scroll">Kaydırma</option>
                  <option value="signup_start">Kayıt başladı</option>
                  <option value="signup_verified">Firma açıldı</option>
                </select>
              </div>
              {events.length === 0 ? (
                <p className="text-xs text-muted-foreground">Olay yok</p>
              ) : (
                <div className="max-h-80 overflow-y-auto rounded border border-border/50">
                  <table className="w-full text-xs">
                    <tbody>
                      {events.map((e, i) => (
                        <tr key={`${e.occurredAt}-${i}`} className="border-b border-border/40 last:border-b-0">
                          <td className="px-2 py-1 whitespace-nowrap text-muted-foreground">{new Date(e.occurredAt).toLocaleTimeString('tr-TR')}</td>
                          <td className="px-2 py-1"><span className="rounded-full border border-border px-1.5 py-0.5">{EVENT_TR[e.type] ?? e.type}</span></td>
                          <td className="px-2 py-1 font-mono truncate max-w-[200px]" title={e.path}>{e.path}</td>
                          <td className="px-2 py-1 text-muted-foreground">{e.country ?? '—'} · {DEVICE_TR[e.device ?? ''] ?? e.device ?? '—'}</td>
                          <td className="px-2 py-1 text-muted-foreground truncate max-w-[180px]" title={e.label ?? ''}>
                            {e.label ?? ''}{e.value != null ? ` ${String(e.value)}` : ''}
                          </td>
                          <td className="px-2 py-1 text-muted-foreground truncate max-w-[140px]">{e.referrer ?? ''}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </CardContent>
          </Card>
        </>
      )}
    </div>
  );
}
