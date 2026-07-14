import { useState } from 'react';
import {
  RefreshCw, CalendarPlus, ScanLine, AlertTriangle, Receipt, Coins, Check, DownloadCloud,
} from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from '@/components/ui/select';
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter,
} from '@/components/ui/dialog';
import { cn } from '@/lib/utils';
import { useToast } from '@/hooks/use-toast';
import {
  useVendorInvoices, useSeedMonth, useReconcile, useExpectInvoice, useMarkReceived, useAutoExpect,
  type VendorInvoice, type VendorInvoiceStatus,
} from '@/api/vendorInvoices';

// ── Config ──────────────────────────────────────────────────────────────────

const MONTHS = [
  'Ocak', 'Şubat', 'Mart', 'Nisan', 'Mayıs', 'Haziran',
  'Temmuz', 'Ağustos', 'Eylül', 'Ekim', 'Kasım', 'Aralık',
];

type TabValue = 'all' | VendorInvoiceStatus;

const TABS: { value: TabValue; label: string }[] = [
  { value: 'all',        label: 'Tümü' },
  { value: 'Expected',   label: 'Beklenen' },
  { value: 'Received',   label: 'Alındı' },
  { value: 'Reconciled', label: 'Mutabık' },
  { value: 'Mismatch',   label: 'Uyuşmazlık' },
  { value: 'Missing',    label: 'Eksik' },
];

const STATUS_CONFIG: Record<VendorInvoiceStatus, { label: string; className: string }> = {
  Expected:   { label: 'Beklenen',   className: 'bg-yellow-500/15 text-yellow-400 border-yellow-500/30' },
  Received:   { label: 'Alındı',     className: 'bg-blue-500/15 text-blue-400 border-blue-500/30' },
  Reconciled: { label: 'Mutabık',    className: 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30' },
  Mismatch:   { label: 'Uyuşmazlık', className: 'bg-orange-500/15 text-orange-400 border-orange-500/30' },
  Missing:    { label: 'Eksik',      className: 'bg-red-500/15 text-red-400 border-red-500/30' },
};

const PROVIDER_LABELS: Record<string, string> = {
  Anthropic: 'Anthropic',
  Railway: 'Railway',
  GoogleCloud: 'Google Cloud',
  GoogleWorkspace: 'Google Workspace',
};

// ── Helpers ─────────────────────────────────────────────────────────────────

function money(amount: number | null, currency: string | null) {
  if (amount == null) return '—';
  return `${amount.toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}${currency ? ' ' + currency : ''}`;
}

function formatDate(iso: string | null) {
  if (!iso) return '—';
  return new Date(iso).toLocaleDateString('tr-TR', { day: '2-digit', month: '2-digit', year: 'numeric' });
}

function StatusBadge({ status }: { status: VendorInvoiceStatus }) {
  const cfg = STATUS_CONFIG[status];
  return (
    <span className={`inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs font-semibold ${cfg.className}`}>
      {cfg.label}
    </span>
  );
}

function errorMessage(err: unknown, fallback: string): string {
  if (
    err && typeof err === 'object' && 'response' in err &&
    err.response && typeof err.response === 'object' && 'data' in err.response
  ) {
    const data = (err.response as { data?: { message?: string } }).data;
    if (data?.message) return data.message;
  }
  return fallback;
}

// ── Dialogs ─────────────────────────────────────────────────────────────────

function ExpectDialog({ invoice, onClose }: { invoice: VendorInvoice; onClose: () => void }) {
  const { toast } = useToast();
  const expect = useExpectInvoice();
  const [amount, setAmount] = useState(invoice.expectedAmount?.toString() ?? '');
  const [currency, setCurrency] = useState(invoice.currency ?? 'USD');

  async function save() {
    try {
      await expect.mutateAsync({
        provider: invoice.provider,
        year: invoice.periodYear,
        month: invoice.periodMonth,
        expectedAmount: amount === '' ? null : Number(amount),
        currency: currency || null,
      });
      toast({ title: 'Beklenen tutar güncellendi' });
      onClose();
    } catch (err) {
      toast({ title: 'Hata', description: errorMessage(err, 'Kaydedilemedi.'), variant: 'destructive' });
    }
  }

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="sm:max-w-sm">
        <DialogHeader><DialogTitle>Beklenen Tutar — {PROVIDER_LABELS[invoice.provider] ?? invoice.provider}</DialogTitle></DialogHeader>
        <div className="space-y-3">
          <div className="space-y-1.5">
            <Label>Beklenen Tutar</Label>
            <Input type="number" step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} placeholder="örn. 128.40" />
          </div>
          <div className="space-y-1.5">
            <Label>Para Birimi</Label>
            <Input value={currency} onChange={(e) => setCurrency(e.target.value)} placeholder="USD" />
          </div>
        </div>
        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose}>İptal</Button>
          <Button onClick={save} disabled={expect.isPending}>{expect.isPending ? 'Kaydediliyor...' : 'Kaydet'}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function ReceiveDialog({ invoice, onClose }: { invoice: VendorInvoice; onClose: () => void }) {
  const { toast } = useToast();
  const markReceived = useMarkReceived();
  const [amount, setAmount] = useState(invoice.receivedAmount?.toString() ?? '');
  const [currency, setCurrency] = useState(invoice.currency ?? 'USD');
  const [invoiceNumber, setInvoiceNumber] = useState(invoice.invoiceNumber ?? '');
  const [pdfUrl, setPdfUrl] = useState(invoice.pdfUrl ?? '');

  async function save() {
    try {
      await markReceived.mutateAsync({
        provider: invoice.provider,
        year: invoice.periodYear,
        month: invoice.periodMonth,
        receivedAmount: amount === '' ? null : Number(amount),
        currency: currency || null,
        invoiceNumber: invoiceNumber || null,
        pdfUrl: pdfUrl || null,
      });
      toast({ title: 'Fatura işlendi' });
      onClose();
    } catch (err) {
      toast({ title: 'Hata', description: errorMessage(err, 'Kaydedilemedi.'), variant: 'destructive' });
    }
  }

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader><DialogTitle>Fatura Girişi — {PROVIDER_LABELS[invoice.provider] ?? invoice.provider}</DialogTitle></DialogHeader>
        <div className="space-y-3">
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label>Alınan Tutar</Label>
              <Input type="number" step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} placeholder="örn. 128.40" />
            </div>
            <div className="space-y-1.5">
              <Label>Para Birimi</Label>
              <Input value={currency} onChange={(e) => setCurrency(e.target.value)} placeholder="USD" />
            </div>
          </div>
          <div className="space-y-1.5">
            <Label>Fatura No</Label>
            <Input value={invoiceNumber} onChange={(e) => setInvoiceNumber(e.target.value)} placeholder="INV-..." />
          </div>
          <div className="space-y-1.5">
            <Label>PDF URL</Label>
            <Input value={pdfUrl} onChange={(e) => setPdfUrl(e.target.value)} placeholder="https://..." />
          </div>
        </div>
        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose}>İptal</Button>
          <Button onClick={save} disabled={markReceived.isPending}>{markReceived.isPending ? 'Kaydediliyor...' : 'Kaydet'}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Page ────────────────────────────────────────────────────────────────────

const now = new Date();

export function VendorInvoicesPage() {
  const { toast } = useToast();
  const [tab, setTab] = useState<TabValue>('all');
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState<number | 'all'>(now.getMonth() + 1);

  const [expectRow, setExpectRow] = useState<VendorInvoice | null>(null);
  const [receiveRow, setReceiveRow] = useState<VendorInvoice | null>(null);

  const { data, isLoading, isFetching, refetch } = useVendorInvoices({
    year,
    month: month === 'all' ? undefined : month,
    status: tab === 'all' ? undefined : tab,
  });
  const seed = useSeedMonth();
  const reconcile = useReconcile();
  const autoExpect = useAutoExpect();

  const rows = data ?? [];
  const years = [now.getFullYear(), now.getFullYear() - 1, now.getFullYear() - 2];

  async function handleAutoExpect() {
    if (month === 'all') {
      toast({ title: 'Ay seçin', description: 'Otomatik doldurma için belirli bir ay seçmelisiniz.', variant: 'destructive' });
      return;
    }
    try {
      const res = await autoExpect.mutateAsync({ year, month });
      const items = res?.items ?? [];
      const ok = items.filter((i) => i.status === 'expected');
      const summary = ok.length
        ? ok.map((i) => `${i.provider}: ${i.amount ?? '—'}${i.currency ? ' ' + i.currency : ''}`).join(' · ')
        : 'Hiçbir sağlayıcıdan tutar alınamadı (API anahtarı/sabit tutar yapılandırılmamış olabilir).';
      toast({
        title: `Otomatik — ${ok.length}/${items.length} sağlayıcı güncellendi`,
        description: summary,
        variant: ok.length ? undefined : 'destructive',
      });
    } catch (err) {
      toast({ title: 'Hata', description: errorMessage(err, 'Otomatik doldurma başarısız.'), variant: 'destructive' });
    }
  }

  async function handleSeed() {
    if (month === 'all') {
      toast({ title: 'Ay seçin', description: 'Ayı hazırlamak için belirli bir ay seçmelisiniz.', variant: 'destructive' });
      return;
    }
    try {
      const created = await seed.mutateAsync({ year, month });
      toast({ title: 'Ay hazırlandı', description: `${created.length} sağlayıcı için baseline satır oluşturuldu.` });
    } catch (err) {
      toast({ title: 'Hata', description: errorMessage(err, 'Ay hazırlanamadı.'), variant: 'destructive' });
    }
  }

  async function handleReconcile() {
    try {
      const res = await reconcile.mutateAsync();
      toast({
        title: 'Mutabakat çalıştı',
        description: res?.missingCount ? `${res.missingCount} eksik fatura tespit edildi.` : 'Eksik fatura yok.',
        variant: res?.missingCount ? 'destructive' : undefined,
      });
    } catch (err) {
      toast({ title: 'Hata', description: errorMessage(err, 'Mutabakat çalıştırılamadı.'), variant: 'destructive' });
    }
  }

  return (
    <div className="p-6 space-y-6 max-w-7xl mx-auto">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-foreground">Fatura Mutabakatı</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Sağlayıcı maliyetleri · beklenen ↔ gelen PDF karşılaştırması · eksik alarmı
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="outline" size="sm" onClick={() => refetch()} disabled={isFetching}>
            <RefreshCw className={`h-4 w-4 mr-1.5 ${isFetching ? 'animate-spin' : ''}`} />
            Yenile
          </Button>
          <Button variant="outline" size="sm" onClick={handleSeed} disabled={seed.isPending}>
            <CalendarPlus className="h-4 w-4 mr-1.5" />
            {seed.isPending ? 'Hazırlanıyor...' : 'Ayı Hazırla'}
          </Button>
          <Button variant="outline" size="sm" onClick={handleAutoExpect} disabled={autoExpect.isPending}>
            <DownloadCloud className="h-4 w-4 mr-1.5" />
            {autoExpect.isPending ? 'Dolduruluyor...' : 'Otomatik Doldur'}
          </Button>
          <Button size="sm" onClick={handleReconcile} disabled={reconcile.isPending}>
            <ScanLine className="h-4 w-4 mr-1.5" />
            {reconcile.isPending ? 'Çalışıyor...' : 'Mutabakat Çalıştır'}
          </Button>
        </div>
      </div>

      {/* Period filter */}
      <Card className="border-border/50">
        <CardContent className="flex flex-wrap items-center gap-3 p-4">
          <Select value={String(year)} onValueChange={(v) => setYear(Number(v))}>
            <SelectTrigger className="w-28 h-8 text-sm"><SelectValue /></SelectTrigger>
            <SelectContent>
              {years.map((y) => <SelectItem key={y} value={String(y)}>{y}</SelectItem>)}
            </SelectContent>
          </Select>
          <Select value={String(month)} onValueChange={(v) => setMonth(v === 'all' ? 'all' : Number(v))}>
            <SelectTrigger className="w-36 h-8 text-sm"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="all">Tüm Aylar</SelectItem>
              {MONTHS.map((m, i) => <SelectItem key={m} value={String(i + 1)}>{m}</SelectItem>)}
            </SelectContent>
          </Select>
        </CardContent>
      </Card>

      {/* Tabs */}
      <div className="flex flex-wrap gap-1 rounded-lg border border-border/50 bg-muted/30 p-1">
        {TABS.map((t) => {
          const active = tab === t.value;
          return (
            <button
              key={t.value}
              onClick={() => setTab(t.value)}
              className={cn(
                'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                active ? 'bg-primary text-primary-foreground shadow-sm' : 'text-muted-foreground hover:text-foreground hover:bg-accent'
              )}
            >
              {t.label}
            </button>
          );
        })}
      </div>

      {/* Table */}
      <Card className="border-border/50">
        <CardContent className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border/50 bg-muted/30">
                  {['Sağlayıcı', 'Dönem', 'Tip', 'Beklenen', 'Alınan', 'Durum', 'Fatura No', 'Vade', ''].map((h) => (
                    <th key={h} className="text-left px-4 py-3 text-xs font-medium text-muted-foreground whitespace-nowrap">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {isLoading ? (
                  Array.from({ length: 4 }).map((_, i) => (
                    <tr key={i} className="border-b border-border/30">
                      {Array.from({ length: 9 }).map((_, j) => (
                        <td key={j} className="px-4 py-3"><Skeleton className="h-4 w-full" /></td>
                      ))}
                    </tr>
                  ))
                ) : rows.length === 0 ? (
                  <tr>
                    <td colSpan={9} className="px-4 py-12 text-center text-muted-foreground">
                      Bu dönem/durum için kayıt yok. “Ayı Hazırla” ile baseline satırları oluşturabilirsiniz.
                    </td>
                  </tr>
                ) : (
                  rows.map((r) => {
                    const mismatch = r.status === 'Mismatch';
                    const missing = r.status === 'Missing';
                    return (
                      <tr key={r.id} className={cn('border-b border-border/30 hover:bg-muted/20 transition-colors', missing && 'bg-red-500/5')}>
                        <td className="px-4 py-3 font-medium text-foreground whitespace-nowrap">{PROVIDER_LABELS[r.provider] ?? r.provider}</td>
                        <td className="px-4 py-3 text-muted-foreground whitespace-nowrap">{MONTHS[r.periodMonth - 1]} {r.periodYear}</td>
                        <td className="px-4 py-3 text-xs text-muted-foreground">{r.billingType === 'Fixed' ? 'Sabit' : 'Kullanım'}</td>
                        <td className="px-4 py-3 whitespace-nowrap">{money(r.expectedAmount, r.currency)}</td>
                        <td className={cn('px-4 py-3 whitespace-nowrap', mismatch && 'text-orange-400 font-medium')}>{money(r.receivedAmount, r.currency)}</td>
                        <td className="px-4 py-3"><StatusBadge status={r.status} /></td>
                        <td className="px-4 py-3 text-xs text-muted-foreground">{r.invoiceNumber ?? '—'}</td>
                        <td className="px-4 py-3 text-xs text-muted-foreground whitespace-nowrap">{formatDate(r.dueDate)}</td>
                        <td className="px-4 py-3">
                          <div className="flex items-center justify-end gap-1.5">
                            <Button variant="ghost" size="sm" className="h-7 px-2 text-xs" onClick={() => setExpectRow(r)} title="Beklenen tutarı belirle">
                              <Coins className="h-3.5 w-3.5 mr-1" />Tutar
                            </Button>
                            <Button variant="ghost" size="sm" className="h-7 px-2 text-xs" onClick={() => setReceiveRow(r)} title="Fatura girişi">
                              {r.receivedOn ? <Check className="h-3.5 w-3.5 mr-1 text-emerald-400" /> : <Receipt className="h-3.5 w-3.5 mr-1" />}
                              Fatura
                            </Button>
                          </div>
                        </td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>

      {/* Legend / note */}
      <p className="flex items-center gap-1.5 text-xs text-muted-foreground">
        <AlertTriangle className="h-3.5 w-3.5" />
        Mutabakat her gün otomatik çalışır; vadesi geçmiş beklenen faturalar “Eksik” olarak işaretlenir.
      </p>

      {expectRow && <ExpectDialog invoice={expectRow} onClose={() => setExpectRow(null)} />}
      {receiveRow && <ReceiveDialog invoice={receiveRow} onClose={() => setReceiveRow(null)} />}
    </div>
  );
}
