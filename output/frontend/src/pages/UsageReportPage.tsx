import { useMemo, useState } from 'react';
import {
  Activity,
  AlertTriangle,
  Search,
  ChevronLeft,
  ChevronRight,
  Loader2,
  Building2,
  ArrowUpDown,
  Info,
} from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useUsageReport, type UsageReportRow } from '@/api/dashboard';

const MONTHS_TR = [
  'Ocak', 'Şubat', 'Mart', 'Nisan', 'Mayıs', 'Haziran',
  'Temmuz', 'Ağustos', 'Eylül', 'Ekim', 'Kasım', 'Aralık',
];

/** Maintenance heartbeat = maintenance per elevator. The single strongest adoption signal. */
function pulse(r: UsageReportRow): number {
  return r.elevatorCount > 0 ? r.maintenanceCount / r.elevatorCount : 0;
}

function totalActivity(r: UsageReportRow): number {
  return r.maintenanceCount + r.faultCount + r.partChangeOfferCount +
         r.revisionOfferCount + r.assemblyOfferCount + r.workOrderCount +
         accountingActivity(r);
}

/**
 * Cari-fatura (muhasebe) kullanımı. Bazı firmalar ürünü ağırlıklı buradan kullanıyor: saha
 * aktivitesi düşük olsa da fatura kesip tahsilat işliyorlar. Bu iki tablo insan eliyle dolduğu için
 * gerçek kullanım sinyali — cari hareket sayısı KULLANILMAZ, çünkü her bakım tamamlanışında
 * otomatik satır atılıyor ve bakım sayısını tekrar saymış olurduk.
 */
function accountingActivity(r: UsageReportRow): number {
  return r.invoiceCount + r.collectionCount;
}

// ── Sekme filtreleri ──────────────────────────────────────────────────────────
// Tek tanım: hem listeyi hem sekme sayacını besler (ikisine kopyalanınca sessizce ayrışıyordu).
//
// "Aktif" = CustomerStatus.Active — Dashboard'un "aktif müşteri" sayımıyla birebir aynı tanım.
// Statü CRM'de elle set edilmez; sync ExpirationDate'ten türetir (SaasSyncJob.ResolveStatus):
//   Active  = gerçek müşteri (CreatedAt+40g < exp) ve süresi dolmamış (today < exp)
//   Churned = gerçek müşteri ve süresi dolmuş
//   Demo    = kısa deneme, süresi dolmamış      Passive = kısa deneme, süresi dolmuş
// Eski filtre (status !== 'Churned') Lead/Demo/Passive'i de aktif sayıp sayıyı şişiriyordu.
const isActiveRow = (r: UsageReportRow) => r.status === 'Active';

/** Churn sekmesi: yalnız son 3 ayda düşenler (hâlâ aranmaya değer); eskiler elenir. */
const isRecentChurn = (r: UsageReportRow, cutoff: Date) =>
  r.status === 'Churned' && r.expirationDate != null && new Date(r.expirationDate) >= cutoff;

type Severity = 'critical' | 'watch' | 'healthy' | 'nodata';

/** Usage severity from the heartbeat + whether the firm did anything at all this month. */
function severity(r: UsageReportRow): Severity {
  if (r.elevatorCount === 0) return 'nodata';
  if (totalActivity(r) === 0) return 'critical'; // silent: elevators but zero activity
  const p = pulse(r);
  let s: Severity = p < 0.2 ? 'critical' : p < 0.5 ? 'watch' : 'healthy';
  // Nabız yalnız bakım/asansör oranını ölçüyor. Cari-fatura tarafını ağırlıklı kullanan firma
  // ürünü aktif kullanıyordur; düşük bakım nabzı tek başına onu "kritik" saymamalı — bir kademe
  // yukarı çekiyoruz (sessize düşen firmalar yukarıdaki totalActivity kontrolüyle zaten ayrışıyor).
  if (s === 'critical' && accountingActivity(r) > 0) s = 'watch';
  return s;
}

const SEV_META: Record<Severity, { label: string; cls: string; dot: string }> = {
  critical: { label: 'Kritik',  cls: 'text-red-600 dark:text-red-400',     dot: 'bg-red-500' },
  watch:    { label: 'İzle',    cls: 'text-amber-600 dark:text-amber-400', dot: 'bg-amber-500' },
  healthy:  { label: 'Sağlıklı', cls: 'text-emerald-600 dark:text-emerald-400', dot: 'bg-emerald-500' },
  nodata:   { label: 'Veri yok', cls: 'text-muted-foreground',            dot: 'bg-muted-foreground/40' },
};

type SortKey = 'severity' | 'company' | 'elevators' | 'maintenance' | 'fault' | 'pulse' | 'plan';

export function UsageReportPage() {
  const today = new Date();
  const [year, setYear] = useState(today.getUTCFullYear());
  const [month, setMonth] = useState(today.getUTCMonth() + 1); // 1–12
  const [search, setSearch] = useState('');
  // Default: worst usage first — exactly the "who is barely using it" worklist.
  const [sortKey, setSortKey] = useState<SortKey>('pulse');
  const [sortAsc, setSortAsc] = useState(true);
  const [view, setView] = useState<'active' | 'churn'>('active');

  const { data: rows = [], isLoading, isError } = useUsageReport(year, month);

  // Churn date ≈ ExpirationDate (statü süre dolunca Churned'a döner).
  const churnCutoff = useMemo(() => {
    const d = new Date();
    d.setMonth(d.getMonth() - 3);
    return d;
  }, []);

  const viewRows = useMemo(
    () =>
      view === 'active'
        ? rows.filter(isActiveRow)
        : rows.filter((r) => isRecentChurn(r, churnCutoff)),
    [rows, view, churnCutoff]
  );

  function prevMonth() {
    setMonth((m) => (m === 1 ? (setYear((y) => y - 1), 12) : m - 1));
  }
  function nextMonth() {
    setMonth((m) => (m === 12 ? (setYear((y) => y + 1), 1) : m + 1));
  }

  function toggleSort(key: SortKey) {
    if (key === sortKey) setSortAsc((a) => !a);
    else { setSortKey(key); setSortAsc(key === 'pulse' || key === 'company'); }
  }

  const filteredSorted = useMemo(() => {
    const q = search.trim().toLocaleLowerCase('tr');
    let out = viewRows;
    if (q) out = out.filter((r) => r.companyName.toLocaleLowerCase('tr').includes(q));

    const sevRank: Record<Severity, number> = { critical: 0, watch: 1, nodata: 2, healthy: 3 };
    const cmp = (a: UsageReportRow, b: UsageReportRow): number => {
      switch (sortKey) {
        case 'company':     return a.companyName.localeCompare(b.companyName, 'tr');
        case 'elevators':   return a.elevatorCount - b.elevatorCount;
        case 'maintenance': return a.maintenanceCount - b.maintenanceCount;
        case 'fault':       return a.faultCount - b.faultCount;
        case 'pulse':       return pulse(a) - pulse(b);
        case 'plan':        return (a.planTier ?? '').localeCompare(b.planTier ?? '', 'tr');
        case 'severity':    return sevRank[severity(a)] - sevRank[severity(b)];
      }
    };
    return [...out].sort((a, b) => (sortAsc ? cmp(a, b) : -cmp(a, b)));
  }, [viewRows, search, sortKey, sortAsc]);

  const stats = useMemo(() => {
    const critical = viewRows.filter((r) => severity(r) === 'critical').length;
    const silent = viewRows.filter((r) => r.elevatorCount > 0 && totalActivity(r) === 0).length;
    return { total: viewRows.length, critical, silent };
  }, [viewRows]);

  // Counts for the tab labels (independent of the active view).
  const tabCounts = useMemo(() => ({
    active: rows.filter(isActiveRow).length,
    churn: rows.filter((r) => isRecentChurn(r, churnCutoff)).length,
  }), [rows, churnCutoff]);

  const fmtLogin = (s: string | null) =>
    s ? new Date(s).toLocaleDateString('tr-TR') : '—';

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight flex items-center gap-2">
            <Activity className="h-6 w-6 text-primary" />
            Kullanım Raporu
          </h1>
          <p className="text-sm text-muted-foreground mt-1 max-w-2xl">
            Liftdesk firmalarının aylık kullanımı. Varsayılan sıralama en düşük <b>nabız</b>
            (asansör başına bakım) — yani ürünü en az kullanan firmalar üstte, churn öncesi
            temas için.
          </p>
        </div>
        {/* Month selector */}
        <div className="flex items-center gap-2">
          <Button variant="outline" size="icon" onClick={prevMonth} aria-label="Önceki ay">
            <ChevronLeft className="h-4 w-4" />
          </Button>
          <div className="min-w-[130px] text-center font-medium tabular-nums">
            {MONTHS_TR[month - 1]} {year}
          </div>
          <Button variant="outline" size="icon" onClick={nextMonth} aria-label="Sonraki ay">
            <ChevronRight className="h-4 w-4" />
          </Button>
        </div>
      </div>

      {/* Aktif / Churn sekmeleri */}
      <div className="flex gap-1 border-b border-border">
        {([['active', 'Aktif firmalar'], ['churn', 'Churn (son 3 ay)']] as const).map(([v, label]) => (
          <button
            key={v}
            onClick={() => setView(v)}
            className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors ${
              view === v ? 'border-primary text-primary' : 'border-transparent text-muted-foreground hover:text-foreground'
            }`}
          >
            {label} <span className="text-xs opacity-60">({tabCounts[v]})</span>
          </button>
        ))}
      </div>

      {/* Summary cards */}
      <div className="grid grid-cols-2 md:grid-cols-3 gap-3">
        <Card><CardContent className="p-4">
          <div className="text-2xl font-bold tabular-nums">{stats.total}</div>
          <div className="text-sm text-muted-foreground">firma</div>
        </CardContent></Card>
        <Card><CardContent className="p-4">
          <div className="text-2xl font-bold tabular-nums text-red-600 dark:text-red-400">{stats.critical}</div>
          <div className="text-sm text-muted-foreground">kritik (düşük nabız)</div>
        </CardContent></Card>
        <Card><CardContent className="p-4">
          <div className="text-2xl font-bold tabular-nums text-red-600 dark:text-red-400">{stats.silent}</div>
          <div className="text-sm text-muted-foreground">sessiz (asansör var, 0 aktivite)</div>
        </CardContent></Card>
      </div>

      {/* Search */}
      <div className="relative max-w-sm">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
        <Input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Firma ara…"
          className="pl-9"
        />
      </div>

      {/* Table */}
      {isLoading ? (
        <div className="flex items-center justify-center py-16 text-muted-foreground">
          <Loader2 className="h-5 w-5 animate-spin mr-2" /> Yükleniyor…
        </div>
      ) : isError ? (
        <div className="flex items-center gap-2 py-10 text-red-600">
          <AlertTriangle className="h-5 w-5" /> Rapor yüklenemedi.
        </div>
      ) : rows.length === 0 ? (
        <Card><CardContent className="py-12 text-center text-muted-foreground">
          <Building2 className="h-8 w-8 mx-auto mb-2 opacity-40" />
          Bu ay için henüz snapshot yok. Kullanım verisi her gün otomatik toplanır;
          seçili ay için veri birikince burada görünür.
        </CardContent></Card>
      ) : (
        <div className="rounded-lg border overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <SortHead label="Durum" k="severity" {...{ sortKey, sortAsc, toggleSort }} />
                <SortHead label="Firma" k="company" {...{ sortKey, sortAsc, toggleSort }} />
                <SortHead label="Asansör" k="elevators" align="right" {...{ sortKey, sortAsc, toggleSort }} />
                <SortHead label="Bakım" k="maintenance" align="right" {...{ sortKey, sortAsc, toggleSort }} />
                <SortHead label="Nabız" k="pulse" align="right" {...{ sortKey, sortAsc, toggleSort }} />
                <SortHead label="Arıza" k="fault" align="right" {...{ sortKey, sortAsc, toggleSort }} />
                <TableHead className="text-right">Teklif</TableHead>
                <TableHead className="text-right">İş emri</TableHead>
                <TableHead className="text-right">Cari-Fatura</TableHead>
                <TableHead className="text-right">Son giriş</TableHead>
                <SortHead label="Paket" k="plan" {...{ sortKey, sortAsc, toggleSort }} />
              </TableRow>
            </TableHeader>
            <TableBody>
              {filteredSorted.map((r) => {
                const sev = severity(r);
                const meta = SEV_META[sev];
                const offers = r.partChangeOfferCount + r.revisionOfferCount + r.assemblyOfferCount;
                return (
                  <TableRow key={r.customerId}>
                    <TableCell>
                      <span className={`inline-flex items-center gap-1.5 text-xs font-medium ${meta.cls}`}>
                        <span className={`h-2 w-2 rounded-full ${meta.dot}`} />
                        {meta.label}
                      </span>
                    </TableCell>
                    <TableCell className="font-medium max-w-[220px] truncate" title={r.companyName}>
                      {r.companyName}
                      {r.status && (
                        <Badge variant="secondary" className="ml-2 text-[10px] align-middle">{r.status}</Badge>
                      )}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{r.elevatorCount}</TableCell>
                    <TableCell className="text-right tabular-nums">{r.maintenanceCount}</TableCell>
                    <TableCell className={`text-right tabular-nums font-medium ${meta.cls}`}>
                      {r.elevatorCount > 0 ? pulse(r).toFixed(2) : '—'}
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{r.faultCount}</TableCell>
                    <TableCell className="text-right tabular-nums" title={`Parça ${r.partChangeOfferCount} · Revizyon ${r.revisionOfferCount} · Montaj ${r.assemblyOfferCount}`}>
                      {offers || '—'}
                    </TableCell>
                    <TableCell className="text-right tabular-nums text-muted-foreground">
                      {r.workOrderCount || '—'}
                    </TableCell>
                    <TableCell
                      className={`text-right tabular-nums ${accountingActivity(r) > 0 ? 'text-foreground' : 'text-muted-foreground'}`}
                      title={`Fatura ${r.invoiceCount} · Tahsilat ${r.collectionCount}`}
                    >
                      {accountingActivity(r) || '—'}
                    </TableCell>
                    <TableCell className="text-right tabular-nums text-muted-foreground">
                      {fmtLogin(r.lastLoginAt)}
                    </TableCell>
                    <TableCell>
                      {r.planTier ? (
                        <Badge variant="outline" className="text-xs">{r.planTier}</Badge>
                      ) : '—'}
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
      )}

      {/* Footnote — the two Liftdesk-dependent columns */}
      <p className="text-xs text-muted-foreground flex items-center gap-1.5">
        <Info className="h-3.5 w-3.5" />
        “İş emri” ve “Son giriş” sütunları Liftdesk bu alanları yayınlayınca otomatik dolacak.
        Trend (“öncekine göre azalma”) için birkaç ay veri biriktikçe churn skoru eklenecek.
      </p>
    </div>
  );
}

function SortHead({
  label, k, align, sortKey, sortAsc, toggleSort,
}: {
  label: string;
  k: SortKey;
  align?: 'right';
  sortKey: SortKey;
  sortAsc: boolean;
  toggleSort: (k: SortKey) => void;
}) {
  const active = sortKey === k;
  return (
    <TableHead className={align === 'right' ? 'text-right' : undefined}>
      <button
        onClick={() => toggleSort(k)}
        className={`inline-flex items-center gap-1 hover:text-foreground transition-colors ${active ? 'text-foreground font-semibold' : ''}`}
      >
        {label}
        <ArrowUpDown className={`h-3 w-3 ${active ? 'opacity-100' : 'opacity-30'}`} />
        {active && <span className="text-[10px]">{sortAsc ? '↑' : '↓'}</span>}
      </button>
    </TableHead>
  );
}
