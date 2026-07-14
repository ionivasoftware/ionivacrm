import { useEffect, useState } from 'react';
import {
  RefreshCw, Play, Plus, Trash2, AlertTriangle, Receipt, Coins, Check, FileText,
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
  useCollectEmails, useDeleteInvoice, useUploadPdf, openInvoicePdf,
  type VendorInvoice, type VendorInvoiceStatus, type EmailCollectItem,
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
  GoogleWorkspaceRezerval: 'Workspace (rezerval.com)',
  GoogleWorkspaceIoniva: 'Workspace (ioniva.com)',
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
  const uploadPdf = useUploadPdf();
  const [amount, setAmount] = useState(invoice.receivedAmount?.toString() ?? '');
  const [currency, setCurrency] = useState(invoice.currency ?? 'USD');
  const [invoiceNumber, setInvoiceNumber] = useState(invoice.invoiceNumber ?? '');
  const [pdfUrl, setPdfUrl] = useState(invoice.pdfUrl ?? '');
  const [file, setFile] = useState<File | null>(null);

  async function save() {
    try {
      const res = await markReceived.mutateAsync({
        provider: invoice.provider,
        year: invoice.periodYear,
        month: invoice.periodMonth,
        receivedAmount: amount === '' ? null : Number(amount),
        currency: currency || null,
        invoiceNumber: invoiceNumber || null,
        pdfUrl: pdfUrl || null,
      });
      if (file && res?.id) await uploadPdf.mutateAsync({ id: res.id, file });
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
            <Label>PDF URL (harici link)</Label>
            <Input value={pdfUrl} onChange={(e) => setPdfUrl(e.target.value)} placeholder="https://..." />
          </div>
          <div className="space-y-1.5">
            <Label>PDF Yükle (dosya)</Label>
            <Input type="file" accept="application/pdf,.pdf" onChange={(e) => setFile(e.target.files?.[0] ?? null)}
              className="cursor-pointer file:mr-2 file:rounded file:border-0 file:bg-muted file:px-2 file:py-1 file:text-xs" />
            {invoice.hasPdf && !file && <p className="text-xs text-muted-foreground">Kayıtlı PDF var — yeni dosya seçilirse değiştirilir.</p>}
          </div>
        </div>
        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose}>İptal</Button>
          <Button onClick={save} disabled={markReceived.isPending || uploadPdf.isPending}>
            {markReceived.isPending || uploadPdf.isPending ? 'Kaydediliyor...' : 'Kaydet'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Manual add dialog ─────────────────────────────────────────────────────────

const KNOWN_PROVIDERS = ['Anthropic', 'Railway', 'GoogleCloud', 'GoogleWorkspaceRezerval', 'GoogleWorkspaceIoniva'];

function AddDialog({ defaultYear, defaultMonth, onClose }: { defaultYear: number; defaultMonth: number; onClose: () => void }) {
  const { toast } = useToast();
  const expect = useExpectInvoice();
  const markReceived = useMarkReceived();
  const uploadPdf = useUploadPdf();
  const [provider, setProvider] = useState('');
  const [year, setYear] = useState(defaultYear);
  const [month, setMonth] = useState(defaultMonth);
  const [expected, setExpected] = useState('');
  const [received, setReceived] = useState('');
  const [currency, setCurrency] = useState('TRY');
  const [file, setFile] = useState<File | null>(null);

  const years = [defaultYear + 1, defaultYear, defaultYear - 1, defaultYear - 2];
  const busy = expect.isPending || markReceived.isPending || uploadPdf.isPending;

  async function save() {
    if (!provider.trim()) {
      toast({ title: 'Sağlayıcı gerekli', variant: 'destructive' });
      return;
    }
    try {
      // Always upsert the row (expected side); then record received if given.
      const created = await expect.mutateAsync({
        provider: provider.trim(),
        year,
        month,
        expectedAmount: expected === '' ? null : Number(expected),
        currency: currency || null,
      });
      if (received !== '') {
        await markReceived.mutateAsync({
          provider: provider.trim(),
          year,
          month,
          receivedAmount: Number(received),
          currency: currency || null,
        });
      }
      if (file && created?.id) await uploadPdf.mutateAsync({ id: created.id, file });
      toast({ title: 'Kayıt eklendi', description: `${provider.trim()} · ${month}/${year}` });
      onClose();
    } catch (err) {
      toast({ title: 'Hata', description: errorMessage(err, 'Kayıt eklenemedi.'), variant: 'destructive' });
    }
  }

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader><DialogTitle>Manuel Kayıt Ekle</DialogTitle></DialogHeader>
        <div className="space-y-3">
          <div className="space-y-1.5">
            <Label>Sağlayıcı</Label>
            <Input list="known-providers" value={provider} onChange={(e) => setProvider(e.target.value)} placeholder="Anthropic, Railway…" />
            <datalist id="known-providers">
              {KNOWN_PROVIDERS.map((p) => <option key={p} value={p} />)}
            </datalist>
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label>Yıl</Label>
              <Select value={String(year)} onValueChange={(v) => setYear(Number(v))}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>{years.map((y) => <SelectItem key={y} value={String(y)}>{y}</SelectItem>)}</SelectContent>
              </Select>
            </div>
            <div className="space-y-1.5">
              <Label>Ay</Label>
              <Select value={String(month)} onValueChange={(v) => setMonth(Number(v))}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>{MONTHS.map((m, i) => <SelectItem key={m} value={String(i + 1)}>{m}</SelectItem>)}</SelectContent>
              </Select>
            </div>
          </div>
          <div className="grid grid-cols-3 gap-3">
            <div className="space-y-1.5">
              <Label>Beklenen</Label>
              <Input type="number" step="0.01" value={expected} onChange={(e) => setExpected(e.target.value)} placeholder="—" />
            </div>
            <div className="space-y-1.5">
              <Label>Alınan</Label>
              <Input type="number" step="0.01" value={received} onChange={(e) => setReceived(e.target.value)} placeholder="—" />
            </div>
            <div className="space-y-1.5">
              <Label>Birim</Label>
              <Input value={currency} onChange={(e) => setCurrency(e.target.value)} placeholder="TRY" />
            </div>
          </div>
          <div className="space-y-1.5">
            <Label>PDF Yükle (opsiyonel)</Label>
            <Input type="file" accept="application/pdf,.pdf" onChange={(e) => setFile(e.target.files?.[0] ?? null)}
              className="cursor-pointer file:mr-2 file:rounded file:border-0 file:bg-muted file:px-2 file:py-1 file:text-xs" />
          </div>
        </div>
        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose}>İptal</Button>
          <Button onClick={save} disabled={busy}>{busy ? 'Kaydediliyor...' : 'Ekle'}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Delete confirm dialog ─────────────────────────────────────────────────────

function DeleteDialog({ invoice, onClose }: { invoice: VendorInvoice; onClose: () => void }) {
  const { toast } = useToast();
  const del = useDeleteInvoice();

  async function confirm() {
    try {
      await del.mutateAsync(invoice.id);
      toast({ title: 'Kayıt silindi' });
      onClose();
    } catch (err) {
      toast({ title: 'Hata', description: errorMessage(err, 'Silinemedi.'), variant: 'destructive' });
    }
  }

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="sm:max-w-sm">
        <DialogHeader><DialogTitle>Kaydı sil</DialogTitle></DialogHeader>
        <p className="text-sm text-muted-foreground">
          <span className="text-foreground font-medium">{PROVIDER_LABELS[invoice.provider] ?? invoice.provider}</span>
          {' · '}{MONTHS[invoice.periodMonth - 1]} {invoice.periodYear} kaydı silinsin mi? Bu işlem geri alınamaz.
        </p>
        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose}>İptal</Button>
          <Button onClick={confirm} disabled={del.isPending}
            className="bg-red-600 hover:bg-red-700 text-white">
            {del.isPending ? 'Siliniyor...' : 'Sil'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── E-mail scan preview (diagnostic) ──────────────────────────────────────────

function CollectPreviewDialog({ onClose }: { onClose: () => void }) {
  const { toast } = useToast();
  const collect = useCollectEmails();
  const [items, setItems] = useState<EmailCollectItem[] | null>(null);
  const [summary, setSummary] = useState<{ scanned: number; matched: number } | null>(null);

  useEffect(() => {
    collect.mutateAsync({ dryRun: true })
      .then((r) => {
        setItems(r?.items ?? []);
        setSummary({ scanned: r?.scanned ?? 0, matched: r?.matched ?? 0 });
      })
      .catch((err) => {
        toast({ title: 'Hata', description: errorMessage(err, 'Önizleme başarısız.'), variant: 'destructive' });
        onClose();
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>
            E-posta Taraması (Önizleme){summary ? ` — ${summary.scanned} mail, ${summary.matched} eşleşme` : ''}
          </DialogTitle>
        </DialogHeader>
        {items === null ? (
          <div className="py-8 text-center text-sm text-muted-foreground">Taranıyor…</div>
        ) : items.length === 0 ? (
          <div className="py-8 text-center text-sm text-muted-foreground">Hiç mail bulunamadı. IMAP bağlantısını kontrol edin.</div>
        ) : (
          <div className="max-h-[62vh] overflow-y-auto space-y-2 pr-1">
            {items.map((it, i) => (
              <div key={i} className="rounded-md border border-border/50 p-2.5 text-xs">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="font-medium text-foreground">
                    {it.status === 'unmatched' ? '(eşleşme yok)' : (PROVIDER_LABELS[it.provider] ?? it.provider)}
                  </span>
                  <span className="text-muted-foreground">
                    {it.status !== 'unmatched' && <>{it.month}/{it.year} · {it.amount ?? '—'} {it.currency ?? ''} · </>}
                    <span className={cn(
                      it.status === 'preview' && 'text-emerald-400',
                      it.status === 'no-amount' && 'text-orange-400',
                      it.status === 'unmatched' && 'text-slate-400',
                    )}>{it.status}</span>
                  </span>
                </div>
                <div className="mt-1 text-muted-foreground truncate" title={it.subject}>{it.subject}</div>
                {it.message && (
                  <pre className="mt-1.5 whitespace-pre-wrap break-words rounded bg-muted/40 p-2 text-[11px] leading-snug text-muted-foreground max-h-28 overflow-y-auto">
                    {it.message}
                  </pre>
                )}
              </div>
            ))}
          </div>
        )}
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Kapat</Button>
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
  const [deleteRow, setDeleteRow] = useState<VendorInvoice | null>(null);
  const [addOpen, setAddOpen] = useState(false);
  const [previewOpen, setPreviewOpen] = useState(false);

  const { data, isLoading, isFetching, refetch } = useVendorInvoices({
    year,
    month: month === 'all' ? undefined : month,
    status: tab === 'all' ? undefined : tab,
  });
  const seed = useSeedMonth();
  const reconcile = useReconcile();
  const autoExpect = useAutoExpect();
  const collectEmails = useCollectEmails();

  const rows = data ?? [];
  const years = [now.getFullYear(), now.getFullYear() - 1, now.getFullYear() - 2];
  const servicing = seed.isPending || autoExpect.isPending || collectEmails.isPending || reconcile.isPending;

  // One "Servis" button runs the whole pipeline in order:
  // Ayı Hazırla → Otomatik Doldur (beklenen + Railway gelen) → E-posta İşle (gelen + PDF) → Mutabakat.
  async function handleService() {
    const y = month === 'all' ? now.getFullYear() : year;
    const m = month === 'all' ? now.getMonth() + 1 : month;
    const notes: string[] = [];

    try { await seed.mutateAsync({ year: y, month: m }); notes.push('hazırlandı'); }
    catch { notes.push('hazırlama×'); }

    try {
      const r = await autoExpect.mutateAsync({ year: y, month: m });
      const exp = (r?.items ?? []).filter((i) => i.status === 'expected').length;
      const rcv = (r?.items ?? []).filter((i) => i.status === 'received').length;
      notes.push(`${exp} beklenen${rcv ? `, ${rcv} gelen(API)` : ''}`);
    } catch { notes.push('otomatik×'); }

    try {
      const r = await collectEmails.mutateAsync({ dryRun: false });
      notes.push(`${r?.received ?? 0} mail`);
    } catch { notes.push('e-posta×'); }

    try {
      const r = await reconcile.mutateAsync();
      notes.push(`${r?.missingCount ?? 0} eksik`);
    } catch { notes.push('mutabakat×'); }

    toast({ title: 'Servis tamamlandı', description: notes.join(' · ') });
    refetch();
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
          <Button variant="outline" size="sm" onClick={() => refetch()} disabled={isFetching || servicing}>
            <RefreshCw className={`h-4 w-4 mr-1.5 ${isFetching ? 'animate-spin' : ''}`} />
            Yenile
          </Button>
          <Button variant="outline" size="sm" onClick={() => setAddOpen(true)}>
            <Plus className="h-4 w-4 mr-1.5" />
            Kayıt Ekle
          </Button>
          <Button size="sm" onClick={handleService} disabled={servicing}
            title="Hazırla → Otomatik doldur → E-posta işle → Mutabakat (sırasıyla)">
            <Play className={`h-4 w-4 mr-1.5 ${servicing ? 'animate-pulse' : ''}`} />
            {servicing ? 'Servis çalışıyor...' : 'Servis'}
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
                  {['Sağlayıcı', 'Dönem', 'Beklenen', 'Alınan', 'Durum', 'Fatura No', ''].map((h, i) => (
                    <th key={i} className="text-left px-4 py-3 text-xs font-medium text-muted-foreground whitespace-nowrap">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {isLoading ? (
                  Array.from({ length: 4 }).map((_, i) => (
                    <tr key={i} className="border-b border-border/30">
                      {Array.from({ length: 7 }).map((_, j) => (
                        <td key={j} className="px-4 py-3"><Skeleton className="h-4 w-full" /></td>
                      ))}
                    </tr>
                  ))
                ) : rows.length === 0 ? (
                  <tr>
                    <td colSpan={7} className="px-4 py-12 text-center text-muted-foreground">
                      Bu dönem/durum için kayıt yok. “Servis” ile otomatik doldurabilir veya “Kayıt Ekle” ile manuel ekleyebilirsiniz.
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
                        <td className="px-4 py-3 whitespace-nowrap">{money(r.expectedAmount, r.currency)}</td>
                        <td className={cn('px-4 py-3 whitespace-nowrap', mismatch && 'text-orange-400 font-medium')}>{money(r.receivedAmount, r.currency)}</td>
                        <td className="px-4 py-3"><StatusBadge status={r.status} /></td>
                        <td className="px-4 py-3 text-xs text-muted-foreground">{r.invoiceNumber ?? '—'}</td>
                        <td className="px-4 py-3">
                          <div className="flex items-center justify-end gap-1.5">
                            {(r.hasPdf || r.pdfUrl) && (
                              <Button variant="ghost" size="sm" className="h-7 px-2 text-xs text-sky-400" onClick={() => openInvoicePdf(r)} title="Fatura PDF'ini aç">
                                <FileText className="h-3.5 w-3.5 mr-1" />PDF
                              </Button>
                            )}
                            <Button variant="ghost" size="sm" className="h-7 px-2 text-xs" onClick={() => setExpectRow(r)} title="Beklenen tutarı belirle">
                              <Coins className="h-3.5 w-3.5 mr-1" />Tutar
                            </Button>
                            <Button variant="ghost" size="sm" className="h-7 px-2 text-xs" onClick={() => setReceiveRow(r)} title="Fatura girişi">
                              {r.receivedOn ? <Check className="h-3.5 w-3.5 mr-1 text-emerald-400" /> : <Receipt className="h-3.5 w-3.5 mr-1" />}
                              Fatura
                            </Button>
                            <Button variant="ghost" size="sm" className="h-7 w-7 px-0 text-muted-foreground hover:text-red-400" onClick={() => setDeleteRow(r)} title="Kaydı sil">
                              <Trash2 className="h-3.5 w-3.5" />
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
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="flex items-center gap-1.5 text-xs text-muted-foreground">
          <AlertTriangle className="h-3.5 w-3.5" />
          “Servis” sırasıyla çalışır: hazırla → otomatik doldur → e-posta işle → mutabakat. Her gün otomatik da çalışır.
        </p>
        <Button variant="ghost" size="sm" className="h-7 px-2 text-xs text-muted-foreground" onClick={() => setPreviewOpen(true)}>
          E-posta önizle (teşhis)
        </Button>
      </div>

      {expectRow && <ExpectDialog invoice={expectRow} onClose={() => setExpectRow(null)} />}
      {receiveRow && <ReceiveDialog invoice={receiveRow} onClose={() => setReceiveRow(null)} />}
      {deleteRow && <DeleteDialog invoice={deleteRow} onClose={() => setDeleteRow(null)} />}
      {previewOpen && <CollectPreviewDialog onClose={() => setPreviewOpen(false)} />}
      {addOpen && (
        <AddDialog
          defaultYear={year}
          defaultMonth={month === 'all' ? now.getMonth() + 1 : month}
          onClose={() => setAddOpen(false)}
        />
      )}
    </div>
  );
}
