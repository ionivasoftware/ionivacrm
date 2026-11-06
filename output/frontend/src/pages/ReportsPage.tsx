import { useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import {
  BarChart2, Users, TrendingUp, CheckSquare, MessageSquare, AlertCircle,
  Building2, Globe, Search, Info,
} from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import { Badge } from '@/components/ui/badge';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import {
  BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, Legend,
  ResponsiveContainer, PieChart, Pie, Cell,
} from 'recharts';
import { apiClient } from '@/api/client';
import { useCompanyReport, type CompetitorUsage } from '@/api/reports';
import { useAuthStore } from '@/stores/authStore';
import { cn } from '@/lib/utils';
import { countryName } from '@/lib/countries';
import { CustomerStatusBadge, STATUS_LABELS } from '@/components/customers/CustomerStatusBadge';
import type { ApiResponse, CustomerStatus } from '@/types';

// ── Types ──────────────────────────────────────────────────────────────────────

interface ReportsData {
  totalCustomers: number;
  newCustomers: number;
  newLeads: number;
  closedWon: number;
  closedLost: number;
  totalContacts: number;
  totalTasks: number;
  completedTasks: number;
  dailyActivity: { date: string; contacts: number; tasks: number }[];
  contactTypeBreakdown: { type: string; count: number }[];
}

const TYPE_LABELS: Record<string, string> = {
  Call: 'Arama',
  Email: 'E-posta',
  Meeting: 'Toplantı',
  Note: 'Not',
  WhatsApp: 'WhatsApp',
  Visit: 'Ziyaret',
};

const TYPE_COLORS: Record<string, string> = {
  Call: '#6366f1',
  Email: '#22c55e',
  Meeting: '#f59e0b',
  Note: '#64748b',
  WhatsApp: '#10b981',
  Visit: '#ef4444',
};

type TabValue = 'contacts' | 'companies';

const TABS: { value: TabValue; label: string; icon: React.ComponentType<{ className?: string }>; description: string }[] = [
  { value: 'contacts', label: 'Görüşme Raporları', icon: MessageSquare, description: 'Seçili tarih aralığında görüşme, görev ve pipeline istatistikleri.' },
  { value: 'companies', label: 'Firma ve Lead Raporları', icon: Building2, description: 'Mevcut müşteri tabanı: ülkeye göre aktif firma / lead sayıları, farklı firma ile çalışanlar ve rakip analizi.' },
];

const TOOLTIP_STYLE = {
  backgroundColor: 'hsl(var(--card))',
  border: '1px solid hsl(var(--border))',
  borderRadius: '8px',
  fontSize: 12,
};

// ── Stat Card ─────────────────────────────────────────────────────────────────

function StatCard({
  title,
  value,
  sub,
  icon: Icon,
  color = 'text-primary',
  isLoading,
}: {
  title: string;
  value: string | number;
  sub?: string;
  icon: React.ComponentType<{ className?: string }>;
  color?: string;
  isLoading?: boolean;
}) {
  return (
    <Card>
      <CardContent className="p-5">
        {isLoading ? (
          <div className="space-y-2">
            <Skeleton className="h-4 w-20" />
            <Skeleton className="h-8 w-14" />
          </div>
        ) : (
          <div className="flex items-start justify-between">
            <div className="min-w-0">
              <p className="text-sm text-muted-foreground">{title}</p>
              <p className="text-3xl font-bold text-foreground mt-0.5 tabular-nums">{value}</p>
              {sub && <p className="text-xs text-muted-foreground mt-1 truncate">{sub}</p>}
            </div>
            <div className="p-2.5 rounded-xl bg-primary/10 flex-shrink-0">
              <Icon className={`h-5 w-5 ${color}`} />
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

function formatDateInput(d: Date): string {
  return d.toISOString().split('T')[0];
}

export function ReportsPage() {
  const [params, setParams] = useSearchParams();
  const tab: TabValue = params.get('tab') === 'companies' ? 'companies' : 'contacts';
  const current = TABS.find((t) => t.value === tab) ?? TABS[0];

  function setTab(next: TabValue) {
    setParams(next === 'contacts' ? {} : { tab: next }, { replace: true });
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div>
        <h1 className="text-2xl font-bold text-foreground">Raporlar</h1>
        <p className="text-muted-foreground text-sm mt-1">{current.description}</p>
      </div>

      {/* Tabs */}
      <div className="flex flex-wrap gap-1 rounded-lg border border-border/50 bg-muted/30 p-1" role="tablist">
        {TABS.map((t) => {
          const active = tab === t.value;
          const Icon = t.icon;
          return (
            <button
              key={t.value}
              type="button"
              role="tab"
              aria-selected={active}
              onClick={() => setTab(t.value)}
              className={cn(
                'inline-flex items-center gap-2 rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                active ? 'bg-primary text-primary-foreground shadow-sm' : 'text-muted-foreground hover:text-foreground hover:bg-accent'
              )}
            >
              <Icon className="h-4 w-4" />
              {t.label}
            </button>
          );
        })}
      </div>

      {tab === 'contacts' ? <ContactReportsTab /> : <CompanyReportsTab />}
    </div>
  );
}

// ── Görüşme Raporları ─────────────────────────────────────────────────────────

function ContactReportsTab() {
  const { currentProjectId } = useAuthStore();

  const today = new Date();
  const firstOfMonth = new Date(today.getFullYear(), today.getMonth(), 1);

  const [startDate, setStartDate] = useState(formatDateInput(firstOfMonth));
  const [endDate, setEndDate] = useState(formatDateInput(today));
  const [queryDates, setQueryDates] = useState({ start: startDate, end: endDate });

  const { data, isLoading, isError } = useQuery({
    queryKey: ['reports', currentProjectId, queryDates.start, queryDates.end],
    queryFn: async () => {
      const res = await apiClient.get<ApiResponse<ReportsData>>('/reports', {
        params: {
          projectId: currentProjectId,
          startDate: queryDates.start,
          endDate: queryDates.end,
        },
      });
      return res.data.data;
    },
    enabled: !!currentProjectId,
  });

  function handleApply() {
    setQueryDates({ start: startDate, end: endDate });
  }

  // Filter daily activity to last 30 days max for readability
  const dailyData = data?.dailyActivity?.slice(-30) ?? [];

  return (
    <div className="space-y-6">
      {/* Date filter */}
      <Card>
        <CardContent className="p-4">
          <div className="flex flex-wrap items-end gap-4">
            <div className="space-y-1.5">
              <Label>Başlangıç Tarihi</Label>
              <Input
                type="date"
                value={startDate}
                onChange={(e) => setStartDate(e.target.value)}
                className="h-10 w-full sm:w-44"
              />
            </div>
            <div className="space-y-1.5">
              <Label>Bitiş Tarihi</Label>
              <Input
                type="date"
                value={endDate}
                onChange={(e) => setEndDate(e.target.value)}
                className="h-10 w-full sm:w-44"
              />
            </div>
            <Button onClick={handleApply} className="gap-2 h-10">
              <BarChart2 className="h-4 w-4" />
              Raporu Getir
            </Button>
            <div className="flex gap-2 flex-wrap">
              {[
                { label: 'Bu Ay', start: formatDateInput(firstOfMonth), end: formatDateInput(today) },
                { label: 'Son 30 Gün', start: formatDateInput(new Date(today.getTime() - 30 * 86400000)), end: formatDateInput(today) },
                { label: 'Son 90 Gün', start: formatDateInput(new Date(today.getTime() - 90 * 86400000)), end: formatDateInput(today) },
              ].map(({ label, start, end }) => (
                <Button
                  key={label}
                  variant="outline"
                  size="sm"
                  className="h-10 text-xs"
                  onClick={() => { setStartDate(start); setEndDate(end); setQueryDates({ start, end }); }}
                >
                  {label}
                </Button>
              ))}
            </div>
          </div>
        </CardContent>
      </Card>

      {isError && (
        <div className="flex items-center gap-3 p-4 rounded-lg border border-destructive/20 bg-destructive/5">
          <AlertCircle className="h-5 w-5 text-destructive" />
          <p className="text-sm text-destructive">Raporlar yüklenemedi.</p>
        </div>
      )}

      {/* KPI cards */}
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-4">
        <StatCard
          title="Toplam Müşteri"
          value={data?.totalCustomers ?? 0}
          sub={`+${data?.newCustomers ?? 0} yeni`}
          icon={Users}
          isLoading={isLoading}
        />
        <StatCard
          title="Yeni Lead"
          value={data?.newLeads ?? 0}
          icon={TrendingUp}
          isLoading={isLoading}
        />
        <StatCard
          title="Kazanılan Pipeline"
          value={data?.closedWon ?? 0}
          sub={`${data?.closedLost ?? 0} kaybedildi`}
          icon={CheckSquare}
          color="text-emerald-500"
          isLoading={isLoading}
        />
        <StatCard
          title="Toplam Görüşme"
          value={data?.totalContacts ?? 0}
          icon={MessageSquare}
          isLoading={isLoading}
        />
        <StatCard
          title="Tamamlanan Görev"
          value={data?.completedTasks ?? 0}
          sub={`${data?.totalTasks ?? 0} toplam`}
          icon={CheckSquare}
          isLoading={isLoading}
        />
      </div>

      {/* Charts row */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        {/* Daily activity bar chart */}
        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle className="text-base font-semibold">Günlük Görüşme Aktivitesi</CardTitle>
            <CardDescription>Seçili dönemdeki günlük iletişim sayısı</CardDescription>
          </CardHeader>
          <CardContent>
            {isLoading ? (
              <Skeleton className="h-[200px] w-full" />
            ) : dailyData.length === 0 ? (
              <div className="flex items-center justify-center h-[200px]">
                <p className="text-sm text-muted-foreground">Bu dönemde veri yok.</p>
              </div>
            ) : (
              <ResponsiveContainer width="100%" height={200}>
                <BarChart data={dailyData} barSize={6}>
                  <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" />
                  <XAxis
                    dataKey="date"
                    tick={{ fill: 'hsl(var(--muted-foreground))', fontSize: 10 }}
                    axisLine={false}
                    tickLine={false}
                    interval={Math.floor(dailyData.length / 7)}
                  />
                  <YAxis
                    tick={{ fill: 'hsl(var(--muted-foreground))', fontSize: 11 }}
                    axisLine={false}
                    tickLine={false}
                  />
                  <Tooltip contentStyle={TOOLTIP_STYLE} />
                  <Bar dataKey="contacts" name="Görüşme" fill="#6366f1" radius={[2, 2, 0, 0]} />
                </BarChart>
              </ResponsiveContainer>
            )}
          </CardContent>
        </Card>

        {/* Contact type pie chart */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base font-semibold">Görüşme Türleri</CardTitle>
            <CardDescription>İletişim kanalı dağılımı</CardDescription>
          </CardHeader>
          <CardContent>
            {isLoading ? (
              <Skeleton className="h-[200px] w-full" />
            ) : !data?.contactTypeBreakdown?.length ? (
              <div className="flex items-center justify-center h-[200px]">
                <p className="text-sm text-muted-foreground">Veri yok</p>
              </div>
            ) : (
              <div className="space-y-3">
                <ResponsiveContainer width="100%" height={130}>
                  <PieChart>
                    <Pie
                      data={data.contactTypeBreakdown}
                      cx="50%"
                      cy="50%"
                      innerRadius={35}
                      outerRadius={60}
                      dataKey="count"
                      nameKey="type"
                    >
                      {data.contactTypeBreakdown.map((entry) => (
                        <Cell key={entry.type} fill={TYPE_COLORS[entry.type] ?? '#64748b'} />
                      ))}
                    </Pie>
                    <Tooltip
                      contentStyle={TOOLTIP_STYLE}
                      formatter={(value, name) => [value, TYPE_LABELS[name as string] ?? name]}
                    />
                  </PieChart>
                </ResponsiveContainer>
                <div className="space-y-1.5">
                  {data.contactTypeBreakdown.map((item) => (
                    <div key={item.type} className="flex items-center justify-between text-sm">
                      <div className="flex items-center gap-2">
                        <div
                          className="w-2.5 h-2.5 rounded-full"
                          style={{ backgroundColor: TYPE_COLORS[item.type] ?? '#64748b' }}
                        />
                        <span className="text-muted-foreground text-xs">
                          {TYPE_LABELS[item.type] ?? item.type}
                        </span>
                      </div>
                      <Badge variant="secondary" className="text-xs">{item.count}</Badge>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

// ── Firma ve Lead Raporları ───────────────────────────────────────────────────

/** Yığılı ülke grafiğindeki üç seri — renkler açık/koyu temada ve renk körlüğünde ayrışacak şekilde doğrulandı. */
const STATUS_SERIES: { dataKey: 'active' | 'lead' | 'demo'; label: string; color: string }[] = [
  { dataKey: 'active', label: 'Aktif', color: '#16a34a' },
  { dataKey: 'lead', label: 'Lead', color: '#6366f1' },
  { dataKey: 'demo', label: 'Demo', color: '#d97706' },
];

const STATUS_ORDER: CustomerStatus[] = ['Active', 'Lead', 'Demo', 'Churned', 'Passive'];

const UNNAMED = 'unnamed';

function countryLabel(code: string | null | undefined): string {
  return code ? countryName(code) : 'Bilinmiyor';
}

function formatDay(iso: string | null): string {
  return iso ? new Date(iso).toLocaleDateString('tr-TR', { day: '2-digit', month: 'short', year: 'numeric' }) : '—';
}

function pct(part: number, total: number): string {
  return total > 0 ? `%${Math.round((part / total) * 100)}` : '%0';
}

function competitorFilterKey(c: { competitorId: string | null }): string {
  return c.competitorId ?? UNNAMED;
}

function CompanyReportsTab() {
  const { currentProjectId } = useAuthStore();
  const { data, isLoading, isError } = useCompanyReport(currentProjectId);

  const [search, setSearch] = useState('');
  const [competitorFilter, setCompetitorFilter] = useState<string>('all');
  const [statusFilter, setStatusFilter] = useState<string>('all');

  const totals = data?.totals;
  const byCountry = data?.byCountry ?? [];
  const competitors = data?.competitors ?? [];

  const chartData = useMemo(
    () => (data?.byCountry ?? []).slice(0, 8).map((r) => ({ name: countryLabel(r.country), active: r.active, lead: r.lead, demo: r.demo })),
    [data]
  );

  const filteredCustomers = useMemo(() => {
    const q = search.trim().toLocaleLowerCase('tr-TR');
    return (data?.competitorCustomers ?? []).filter((c) =>
      (competitorFilter === 'all' || competitorFilterKey(c) === competitorFilter) &&
      (statusFilter === 'all' || c.status === statusFilter) &&
      (!q || c.companyName.toLocaleLowerCase('tr-TR').includes(q) || (c.competitorName ?? '').toLocaleLowerCase('tr-TR').includes(q))
    );
  }, [data, search, competitorFilter, statusFilter]);

  const usingCompetitor = data?.usingCompetitor ?? 0;
  const activeShare = totals ? pct(totals.active, totals.total) : '%0';
  const competitorShare = totals ? pct(usingCompetitor, totals.total) : '%0';

  function pickCompetitor(c: CompetitorUsage) {
    setCompetitorFilter(competitorFilterKey(c));
    setStatusFilter('all');
    document.getElementById('competitor-customers')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  return (
    <div className="space-y-6">
      {isError && (
        <div className="flex items-center gap-3 p-4 rounded-lg border border-destructive/20 bg-destructive/5">
          <AlertCircle className="h-5 w-5 text-destructive" />
          <p className="text-sm text-destructive">Firma raporu yüklenemedi.</p>
        </div>
      )}

      {/* KPI cards */}
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-4">
        <StatCard title="Toplam Firma" value={totals?.total ?? 0} sub="dahili firmalar hariç" icon={Building2} isLoading={isLoading} />
        <StatCard title="Aktif" value={totals?.active ?? 0} sub={`${activeShare} · ödeyen müşteri`} icon={CheckSquare} color="text-emerald-500" isLoading={isLoading} />
        <StatCard title="Lead" value={totals?.lead ?? 0} icon={TrendingUp} color="text-indigo-400" isLoading={isLoading} />
        <StatCard title="Demo" value={totals?.demo ?? 0} icon={Users} color="text-amber-500" isLoading={isLoading} />
        <StatCard title="Kayıp / Pasif" value={totals?.churned ?? 0} sub={`${totals?.passive ?? 0} pasif`} icon={AlertCircle} color="text-red-400" isLoading={isLoading} />
        <StatCard
          title="Rakiple Çalışan"
          value={usingCompetitor}
          sub={`${competitorShare} · ${data?.usingCompetitorUnnamed ?? 0} adı bilinmiyor`}
          icon={Building2}
          color="text-rose-400"
          isLoading={isLoading}
        />
      </div>

      {/* Ülke dağılımı */}
      <Card>
        <CardHeader>
          <CardTitle className="text-base font-semibold flex items-center gap-2"><Globe className="h-4 w-4 text-muted-foreground" /> Ülkeye Göre Firmalar</CardTitle>
          <CardDescription>
            Aktif firma, lead ve demo sayıları ülke bazında. Ülke, kayıttaki ülke koduyla belirlenir; yoksa telefonun uluslararası kodundan çıkarılır.
          </CardDescription>
        </CardHeader>
        <CardContent>
          {isLoading ? (
            <Skeleton className="h-[240px] w-full" />
          ) : byCountry.length === 0 ? (
            <div className="flex items-center justify-center h-[160px]">
              <p className="text-sm text-muted-foreground">Bu projede firma yok.</p>
            </div>
          ) : (
            <div className="grid grid-cols-1 lg:grid-cols-5 gap-6">
              <div className="lg:col-span-2">
                <ResponsiveContainer width="100%" height={Math.max(180, chartData.length * 38)}>
                  <BarChart data={chartData} layout="vertical" barSize={14} margin={{ top: 4, right: 16, bottom: 0, left: 4 }}>
                    <CartesianGrid horizontal={false} strokeDasharray="3 3" stroke="hsl(var(--border))" />
                    <XAxis type="number" allowDecimals={false} tick={{ fill: 'hsl(var(--muted-foreground))', fontSize: 11 }} axisLine={false} tickLine={false} />
                    <YAxis type="category" dataKey="name" width={104} tick={{ fill: 'hsl(var(--muted-foreground))', fontSize: 11 }} axisLine={false} tickLine={false} />
                    <Tooltip contentStyle={TOOLTIP_STYLE} cursor={{ fill: 'hsl(var(--muted))', opacity: 0.35 }} />
                    <Legend wrapperStyle={{ fontSize: 11 }} iconType="circle" iconSize={8} />
                    {STATUS_SERIES.map((s) => (
                      <Bar key={s.dataKey} dataKey={s.dataKey} name={s.label} stackId="status" fill={s.color} stroke="hsl(var(--card))" strokeWidth={1} />
                    ))}
                  </BarChart>
                </ResponsiveContainer>
                {byCountry.length > chartData.length && (
                  <p className="text-[11px] text-muted-foreground mt-1">Grafikte ilk {chartData.length} ülke; tamamı tabloda.</p>
                )}
              </div>
              <div className="lg:col-span-3 overflow-x-auto max-h-[420px] overflow-y-auto rounded-md border border-border/60">
                <table className="w-full text-sm">
                  <thead className="sticky top-0 bg-card">
                    <tr className="text-left text-xs text-muted-foreground border-b border-border">
                      <th className="px-3 py-2 font-medium">Ülke</th>
                      <th className="px-3 py-2 font-medium text-right">Aktif</th>
                      <th className="px-3 py-2 font-medium text-right">Lead</th>
                      <th className="px-3 py-2 font-medium text-right">Demo</th>
                      <th className="px-3 py-2 font-medium text-right">Kayıp</th>
                      <th className="px-3 py-2 font-medium text-right">Pasif</th>
                      <th className="px-3 py-2 font-medium text-right">Toplam</th>
                    </tr>
                  </thead>
                  <tbody className="tabular-nums">
                    {byCountry.map((r) => (
                      <tr key={r.country ?? UNNAMED} className="border-b border-border/50 last:border-0">
                        <td className="px-3 py-2">
                          <span className={cn(!r.country && 'text-muted-foreground italic')}>{countryLabel(r.country)}</span>
                          {r.country && <span className="ml-1.5 text-[11px] text-muted-foreground font-mono">{r.country}</span>}
                        </td>
                        <td className="px-3 py-2 text-right font-medium text-emerald-500">{r.active || <span className="text-muted-foreground/60">0</span>}</td>
                        <td className="px-3 py-2 text-right font-medium text-indigo-400">{r.lead || <span className="text-muted-foreground/60">0</span>}</td>
                        <td className="px-3 py-2 text-right text-foreground">{r.demo || <span className="text-muted-foreground/60">0</span>}</td>
                        <td className="px-3 py-2 text-right text-muted-foreground">{r.churned || <span className="text-muted-foreground/60">0</span>}</td>
                        <td className="px-3 py-2 text-right text-muted-foreground">{r.passive || <span className="text-muted-foreground/60">0</span>}</td>
                        <td className="px-3 py-2 text-right font-semibold">{r.total}</td>
                      </tr>
                    ))}
                  </tbody>
                  {totals && (
                    <tfoot className="tabular-nums sticky bottom-0 bg-card">
                      <tr className="border-t border-border text-xs font-semibold">
                        <td className="px-3 py-2">Toplam</td>
                        <td className="px-3 py-2 text-right">{totals.active}</td>
                        <td className="px-3 py-2 text-right">{totals.lead}</td>
                        <td className="px-3 py-2 text-right">{totals.demo}</td>
                        <td className="px-3 py-2 text-right">{totals.churned}</td>
                        <td className="px-3 py-2 text-right">{totals.passive}</td>
                        <td className="px-3 py-2 text-right">{totals.total}</td>
                      </tr>
                    </tfoot>
                  )}
                </table>
              </div>
            </div>
          )}
          {data && (data.countryFromPhone > 0 || data.countryUnknown > 0) && (
            <p className="mt-3 flex items-start gap-1.5 text-[11px] text-muted-foreground">
              <Info className="h-3.5 w-3.5 mt-px flex-shrink-0" />
              <span>
                {data.countryFromPhone > 0 && <>{data.countryFromPhone} firmanın ülkesi telefon kodundan çıkarıldı. </>}
                {data.countryUnknown > 0 && <>{data.countryUnknown} firmanın ülkesi bilinmiyor (kayıtta ülke yok, telefon ulusal biçimde).</>}
              </span>
            </p>
          )}
        </CardContent>
      </Card>

      {/* Rakip analizi */}
      <Card>
        <CardHeader>
          <CardTitle className="text-base font-semibold">Rakip Analizi</CardTitle>
          <CardDescription>
            Görüşmelerde &quot;farklı bir firma ile çalışıyor&quot; denen müşteriler, çalıştıkları firmaya göre. Satıra tıklayınca aşağıdaki liste o rakibe süzülür.
          </CardDescription>
        </CardHeader>
        <CardContent className="p-0">
          {isLoading ? (
            <div className="p-6"><Skeleton className="h-[120px] w-full" /></div>
          ) : competitors.length === 0 ? (
            <div className="py-10 text-center text-sm text-muted-foreground flex flex-col items-center gap-2">
              <Info className="h-6 w-6" />
              Henüz hiçbir görüşmede rakip firma işaretlenmedi. Görüşme kaydında &quot;Farklı bir firma ile çalışıyor&quot; seçildikçe burada toplanır.
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="table-stack w-full text-sm">
                <thead>
                  <tr className="text-left text-xs text-muted-foreground border-b border-border">
                    <th className="px-4 py-2.5 font-medium">Rakip Firma</th>
                    <th className="px-4 py-2.5 font-medium">Firma Sayısı</th>
                    <th className="px-4 py-2.5 font-medium text-right">Aktif</th>
                    <th className="px-4 py-2.5 font-medium text-right">Lead</th>
                    <th className="px-4 py-2.5 font-medium text-right">Demo</th>
                    <th className="px-4 py-2.5 font-medium text-right">Kayıp / Pasif</th>
                    <th className="px-4 py-2.5 font-medium">Ülkeler</th>
                  </tr>
                </thead>
                <tbody className="tabular-nums">
                  {competitors.map((c) => {
                    const key = competitorFilterKey(c);
                    const selected = competitorFilter === key;
                    return (
                      <tr
                        key={key}
                        className={cn('border-b border-border/50 last:border-0 cursor-pointer hover:bg-accent/40 transition-colors', selected && 'bg-accent/50')}
                        onClick={() => pickCompetitor(c)}
                      >
                        <td data-stack="primary" data-label="Rakip" className="px-4 py-2.5">
                          {c.competitorId ? (
                            <div className="min-w-0">
                              <div className="font-medium text-foreground">{c.name}</div>
                              {c.website && (
                                <a
                                  href={c.website.startsWith('http') ? c.website : `https://${c.website}`}
                                  target="_blank"
                                  rel="noreferrer"
                                  onClick={(e) => e.stopPropagation()}
                                  className="text-[11px] text-muted-foreground hover:text-primary hover:underline truncate block"
                                >
                                  {c.website.replace(/^https?:\/\//, '')}
                                </a>
                              )}
                            </div>
                          ) : (
                            <span className="italic text-muted-foreground">Adı belirtilmedi</span>
                          )}
                        </td>
                        <td data-label="Firma sayısı" className="px-4 py-2.5">
                          <div className="flex items-center gap-2 min-w-[140px]">
                            <span className="w-7 text-right font-semibold">{c.total}</span>
                            <div className="h-1.5 flex-1 rounded-full bg-muted overflow-hidden">
                              <div className="h-full rounded-full bg-rose-500/70" style={{ width: pct(c.total, usingCompetitor) }} />
                            </div>
                            <span className="w-10 text-right text-xs text-muted-foreground">{pct(c.total, usingCompetitor)}</span>
                          </div>
                        </td>
                        <td data-label="Aktif" className="px-4 py-2.5 text-right font-medium text-emerald-500">{c.active || <span className="text-muted-foreground/60">0</span>}</td>
                        <td data-label="Lead" className="px-4 py-2.5 text-right font-medium text-indigo-400">{c.lead || <span className="text-muted-foreground/60">0</span>}</td>
                        <td data-label="Demo" className="px-4 py-2.5 text-right">{c.demo || <span className="text-muted-foreground/60">0</span>}</td>
                        <td data-label="Kayıp / Pasif" className="px-4 py-2.5 text-right text-muted-foreground">{c.churned + c.passive || <span className="text-muted-foreground/60">0</span>}</td>
                        <td data-label="Ülkeler" className="px-4 py-2.5 text-xs text-muted-foreground">
                          {c.countries.slice(0, 3).map((x) => `${countryLabel(x.country)} ${x.count}`).join(' · ')}
                          {c.countries.length > 3 && ` · +${c.countries.length - 3}`}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>

      {/* Farklı firma ile çalışan müşteriler */}
      <Card id="competitor-customers">
        <CardHeader>
          <CardTitle className="text-base font-semibold">Farklı Firma ile Çalışan Firmalar</CardTitle>
          <CardDescription>Listemizdeki hangi firma hangi rakiple çalışıyor; son görüşme, bunun söylendiği en yeni kayıt.</CardDescription>
        </CardHeader>
        <CardContent className="p-0">
          <div className="flex flex-wrap items-center gap-2 px-4 py-3 border-b border-border">
            <div className="relative flex-1 min-w-[200px] max-w-sm">
              <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <Input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Firma ya da rakip ara…"
                className="h-9 pl-8"
              />
            </div>
            <Select value={competitorFilter} onValueChange={setCompetitorFilter}>
              <SelectTrigger className="h-9 w-[200px]"><SelectValue placeholder="Rakip" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">Tüm rakipler</SelectItem>
                {competitors.map((c) => (
                  <SelectItem key={competitorFilterKey(c)} value={competitorFilterKey(c)}>
                    {c.competitorId ? c.name : 'Adı belirtilmedi'} ({c.total})
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Select value={statusFilter} onValueChange={setStatusFilter}>
              <SelectTrigger className="h-9 w-[150px]"><SelectValue placeholder="Durum" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">Tüm durumlar</SelectItem>
                {STATUS_ORDER.map((s) => (
                  <SelectItem key={s} value={s}>{STATUS_LABELS[s]}</SelectItem>
                ))}
              </SelectContent>
            </Select>
            <span className="ml-auto text-xs text-muted-foreground tabular-nums">{filteredCustomers.length} firma</span>
          </div>

          {isLoading ? (
            <div className="p-6"><Skeleton className="h-[160px] w-full" /></div>
          ) : filteredCustomers.length === 0 ? (
            <div className="py-10 text-center text-sm text-muted-foreground">
              {usingCompetitor === 0 ? 'Farklı firma ile çalıştığı kaydedilen müşteri yok.' : 'Süzgeçlere uyan firma yok.'}
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="table-stack w-full text-sm">
                <thead>
                  <tr className="text-left text-xs text-muted-foreground border-b border-border">
                    <th className="px-4 py-2.5 font-medium">Firma</th>
                    <th className="px-4 py-2.5 font-medium">Durum</th>
                    <th className="px-4 py-2.5 font-medium">Ülke</th>
                    <th className="px-4 py-2.5 font-medium">Çalıştığı Rakip</th>
                    <th className="px-4 py-2.5 font-medium">Son Görüşme</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredCustomers.map((c) => (
                    <tr key={c.customerId} className="border-b border-border/50 last:border-0">
                      <td data-stack="primary" data-label="Firma" className="px-4 py-2.5">
                        <Link to={`/customers/${c.customerId}`} className="font-medium text-foreground hover:text-primary hover:underline">
                          {c.companyName}
                        </Link>
                      </td>
                      <td data-label="Durum" className="px-4 py-2.5"><CustomerStatusBadge status={c.status} /></td>
                      <td data-label="Ülke" className="px-4 py-2.5 text-xs">
                        <span className={cn(!c.country && 'text-muted-foreground italic')}>{countryLabel(c.country)}</span>
                        {c.countrySource === 'phone' && <span className="ml-1 text-muted-foreground" title="Telefonun uluslararası kodundan çıkarıldı">(tel.)</span>}
                      </td>
                      <td data-label="Rakip" className="px-4 py-2.5">
                        {c.competitorName
                          ? <span className="inline-flex items-center gap-1 rounded-full border border-rose-500/30 bg-rose-500/10 px-2 py-0.5 text-[11px] font-semibold text-rose-400"><Building2 className="h-3 w-3" /> {c.competitorName}</span>
                          : <span className="text-xs italic text-muted-foreground">Adı belirtilmedi</span>}
                      </td>
                      <td data-label="Son görüşme" className="px-4 py-2.5 text-xs text-muted-foreground whitespace-nowrap">{formatDay(c.lastContactAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
