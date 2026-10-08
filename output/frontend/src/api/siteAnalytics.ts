import { useQuery } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse } from '@/types';

// ── liftdesk.app kullanım verisi (docs/crm-site-analytics-api.md) ─────────────
// Birinci taraf, çerezsiz; IP saklanmaz. DİKKAT: "visitors" GÜNLÜK tekil toplamıdır — ziyaretçi
// kimliği her gün değişen bir özet olduğundan aynı kişi iki ayrı günde iki sayılır.

export interface SiteTotals {
  pageViews: number;
  visitors: number;
  signupStarts: number;
  signupVerified: number;
  averageScrollDepth: number | null;
}
export interface SiteDaily { day: string; pageViews: number; visitors: number; signupStarts: number; signupVerified: number }
export interface SiteCountry { country: string; pageViews: number; visitors: number; signupStarts: number; signupVerified: number }
export interface SitePage { path: string; site: string | null; pageViews: number; visitors: number }
export interface SiteBucket { key: string; count: number; visitors: number }
export interface SiteFunnel { site: string; signupPageViews: number; signupStarts: number; signupVerified: number }

export interface SiteOverview {
  from: string;
  to: string;
  site: string | null;
  country: string | null;
  totals: SiteTotals;
  /** Yalnız olay olan günler — eksik günler 0 ile DOLDURULMALI. */
  daily: SiteDaily[] | null;
  countries: SiteCountry[] | null;
  pages: SitePage[] | null;
  sites: SiteBucket[] | null;
  referrers: SiteBucket[] | null;
  devices: SiteBucket[] | null;
  languages: SiteBucket[] | null;
  campaigns: SiteBucket[] | null;
  sections: SiteBucket[] | null;
  clicks: SiteBucket[] | null;
  scrollDepth: SiteBucket[] | null;
  funnel: SiteFunnel[] | null;
}

export interface SiteEvent {
  occurredAt: string;
  type: string;
  path: string;
  site: string | null;
  country: string | null;
  referrer: string | null;
  device: string | null;
  language: string | null;
  label: string | null;
  value: string | number | null;
  utmSource: string | null;
  utmMedium: string | null;
  utmCampaign: string | null;
}

export interface SiteFilters {
  from: string;
  to: string;
  site: string | null;
  country: string | null;
}

/** Pano: tek istek, 60 sn önbellek (sözleşme §6.1). */
export function useSiteOverview(f: SiteFilters, enabled = true) {
  return useQuery({
    queryKey: ['site-analytics', 'overview', f.from, f.to, f.site, f.country],
    queryFn: async () => {
      const r = await apiClient.get<ApiResponse<SiteOverview>>('/site-analytics/overview', {
        params: { from: f.from, to: f.to, site: f.site || undefined, country: f.country || undefined },
      });
      return r.data.data;
    },
    enabled,
    staleTime: 60 * 1000,
    retry: 1,
  });
}

/**
 * Canlı akış: en son olaylar, 30 sn'de bir (sözleşme §6.7). DÖNEM süzgeci bilerek YOK — akış "şimdi"dir;
 * ham olaylar yalnız 30 gün tutulduğu için (§5) eski bir aralık seçiliyken dönemi geçirmek akışı boş
 * bırakırdı. Site/ülke süzgeci kalır ("GB'den gelen ne yapıyor" teşhisi için).
 */
export function useSiteEvents(f: Pick<SiteFilters, 'site' | 'country'>, type: string | null, limit = 50, enabled = true) {
  return useQuery({
    queryKey: ['site-analytics', 'events', f.site, f.country, type, limit],
    queryFn: async () => {
      const r = await apiClient.get<ApiResponse<SiteEvent[]>>('/site-analytics/events', {
        params: { site: f.site || undefined, country: f.country || undefined, type: type || undefined, limit },
      });
      return r.data.data ?? [];
    },
    enabled,
    refetchInterval: 30 * 1000,
    staleTime: 15 * 1000,
    retry: 1,
  });
}

/**
 * §6.8 sessizlik yoklaması — "son 24 saatte hiç PageView var mı?" sorusu HAM olaylardan, SÜZGEÇSİZ
 * ve anlık sorulur. overview özetten okur ve "bugün" 5 dakikaya kadar geriden gelir; üstelik overview
 * süzgeçlidir — country=GB seçiliyken GB'den ziyaret olmaması ölçüm arızası değildir. Bu yüzden ayrı,
 * tüm siteyi kapsayan tek olaylık bir sorgu: boş dönerse ölçüm kopmuş olabilir.
 */
export function useSiteSilenceProbe(enabled = true) {
  const today = todayUtc();
  const yesterday = addDaysUtc(today, -1);
  return useQuery({
    queryKey: ['site-analytics', 'silence-probe', today],
    queryFn: async () => {
      const r = await apiClient.get<ApiResponse<SiteEvent[]>>('/site-analytics/events', {
        params: { from: yesterday, to: today, type: 'pageview', limit: 1 },
      });
      const last24h = Date.now() - 24 * 3600 * 1000;
      const recent = (r.data.data ?? []).some(e => new Date(e.occurredAt).getTime() >= last24h);
      return { silent: !recent };
    },
    enabled,
    refetchInterval: 60 * 1000,
    staleTime: 30 * 1000,
    retry: 1,
  });
}

// ── Tarih yardımcıları (UTC gün, yyyy-MM-dd) ──────────────────────────────────

export function utcDay(d: Date): string {
  return d.toISOString().slice(0, 10);
}
export function todayUtc(): string {
  return utcDay(new Date());
}
export function addDaysUtc(day: string, delta: number): string {
  const d = new Date(`${day}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + delta);
  return utcDay(d);
}

/** Eksik günleri 0 ile doldurur (sözleşme: boş gün satırı gelmez). */
export function fillDaily(daily: SiteDaily[] | null | undefined, from: string, to: string): SiteDaily[] {
  const map = new Map((daily ?? []).map(d => [d.day, d]));
  const out: SiteDaily[] = [];
  for (let day = from; day <= to; day = addDaysUtc(day, 1)) {
    out.push(map.get(day) ?? { day, pageViews: 0, visitors: 0, signupStarts: 0, signupVerified: 0 });
    if (out.length > 400) break; // sözleşme üst sınırı — sonsuz döngü koruması
  }
  return out;
}
