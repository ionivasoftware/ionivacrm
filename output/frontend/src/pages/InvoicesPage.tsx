import { useState } from 'react';
import { useForm, useFieldArray } from 'react-hook-form';
import {
  FileText,
  Plus,
  Trash2,
  Loader2,
  ChevronLeft,
  ChevronRight,
  TrendingUp,
  Wallet,
  AlertCircle,
  RefreshCw,
} from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@/components/ui/dialog';
import { Skeleton } from '@/components/ui/skeleton';
import { useAuthStore } from '@/stores/authStore';
import { useCanAccessFinance } from '@/lib/roles';
import {
  useParasutStatus,
  useParasutInvoices,
  useCreateParasutInvoice,
  type ParasutInvoice,
} from '@/api/parasut';
import { useToast } from '@/hooks/use-toast';
import { useNavigate } from 'react-router-dom';

// ── Helpers ───────────────────────────────────────────────────────────────────

function formatCurrency(value: number, currency = 'TRY'): string {
  return new Intl.NumberFormat('tr-TR', {
    style: 'currency',
    currency: currency === 'TRL' ? 'TRY' : currency,
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value);
}

function formatDate(dateStr: string): string {
  return new Intl.DateTimeFormat('tr-TR', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  }).format(new Date(dateStr));
}

function today(): string {
  return new Date().toISOString().split('T')[0];
}

function daysLater(n: number): string {
  const d = new Date();
  d.setDate(d.getDate() + n);
  return d.toISOString().split('T')[0];
}

// ── Invoice status ────────────────────────────────────────────────────────────

function InvoiceStatusBadge({ invoice }: { invoice: ParasutInvoice }) {
  const remaining = invoice.remaining;
  const overdue = remaining > 0 && new Date(invoice.dueDate) < new Date();

  if (remaining <= 0) {
    return <Badge className="bg-emerald-500/15 text-emerald-400 border-emerald-500/30">Tahsil Edildi</Badge>;
  }
  if (overdue) {
    return <Badge className="bg-red-500/15 text-red-400 border-red-500/30">Vadesi Geçmiş</Badge>;
  }
  return <Badge className="bg-amber-500/15 text-amber-400 border-amber-500/30">Bekliyor</Badge>;
}

// ── Create Invoice Dialog ─────────────────────────────────────────────────────

interface InvoiceLineForm {
  description?:  string;
  quantity:      number;
  unitPrice:     number;
  vatRate:       number;
  discountValue: number;
  unit?:         string;
}

interface InvoiceForm {
  parasutContactId?: string;
  issueDate:         string;
  dueDate:           string;
  currency:          string;
  description?:      string;
  invoiceSeries?:    string;
  lines:             InvoiceLineForm[];
}

interface CreateInvoiceDialogProps {
  open: boolean;
  onClose: () => void;
  projectId: string;
  defaultContactId?: string;
}

function CreateInvoiceDialog({ open, onClose, projectId, defaultContactId }: CreateInvoiceDialogProps) {
  const { toast } = useToast();
  const createInvoice = useCreateParasutInvoice();

  const form = useForm<InvoiceForm>({
    defaultValues: {
      parasutContactId: defaultContactId ?? '',
      issueDate:    today(),
      dueDate:      daysLater(30),
      currency:     'TRL',
      description:  '',
      invoiceSeries: 'A',
      lines: [{ description: '', quantity: 1, unitPrice: 0, vatRate: 20, discountValue: 0, unit: 'Adet' }],
    },
  });

  const { fields, append, remove } = useFieldArray({ control: form.control, name: 'lines' });

  const watchedLines = form.watch('lines');
  const subtotal = watchedLines.reduce((acc, l) => {
    const lineTotal = (l.quantity || 0) * (l.unitPrice || 0);
    const discount = lineTotal * ((l.discountValue || 0) / 100);
    return acc + lineTotal - discount;
  }, 0);
  const vat = watchedLines.reduce((acc, l) => {
    const lineTotal = (l.quantity || 0) * (l.unitPrice || 0);
    const discount = lineTotal * ((l.discountValue || 0) / 100);
    return acc + (lineTotal - discount) * ((l.vatRate || 0) / 100);
  }, 0);

  async function onSubmit(data: InvoiceForm) {
    try {
      await createInvoice.mutateAsync({
        projectId,
        parasutContactId: data.parasutContactId || undefined,
        issueDate:    data.issueDate,
        dueDate:      data.dueDate,
        currency:     data.currency,
        description:  data.description,
        invoiceSeries: data.invoiceSeries,
        lines: data.lines.map(l => ({
          description:   l.description,
          quantity:      l.quantity,
          unitPrice:     l.unitPrice,
          vatRate:       l.vatRate,
          discountValue: l.discountValue,
          discountType:  'percentage',
          unit:          l.unit,
        })),
      });
      toast({ title: 'Fatura oluşturuldu', description: 'Paraşüt\'e gönderildi.' });
      form.reset();
      onClose();
    } catch {
      toast({ title: 'Hata', description: 'Fatura oluşturulamadı.', variant: 'destructive' });
    }
  }

  return (
    <Dialog open={open} onOpenChange={onClose}>
      <DialogContent className="max-w-2xl max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Yeni Satış Faturası</DialogTitle>
        </DialogHeader>

        <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-5">
          {/* Header fields */}
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-1.5">
              <Label>Paraşüt Cari ID</Label>
              <Input
                {...form.register('parasutContactId')}
                className="h-9 font-mono text-sm"
                placeholder="Opsiyonel"
              />
            </div>
            <div className="space-y-1.5">
              <Label>Döviz</Label>
              <Select
                defaultValue="TRL"
                onValueChange={v => form.setValue('currency', v)}
              >
                <SelectTrigger className="h-9">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="TRL">TRY ₺</SelectItem>
                  <SelectItem value="USD">USD $</SelectItem>
                  <SelectItem value="EUR">EUR €</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1.5">
              <Label>Fatura Tarihi *</Label>
              <Input type="date" {...form.register('issueDate')} className="h-9" />
              {form.formState.errors.issueDate && (
                <p className="text-xs text-destructive">Tarih gerekli</p>
              )}
            </div>
            <div className="space-y-1.5">
              <Label>Vade Tarihi *</Label>
              <Input type="date" {...form.register('dueDate')} className="h-9" />
              {form.formState.errors.dueDate && (
                <p className="text-xs text-destructive">Vade tarihi gerekli</p>
              )}
            </div>
            <div className="space-y-1.5">
              <Label>Fatura Serisi</Label>
              <Input {...form.register('invoiceSeries')} className="h-9" placeholder="A" />
            </div>
            <div className="space-y-1.5">
              <Label>Açıklama</Label>
              <Input {...form.register('description')} className="h-9" placeholder="İsteğe bağlı" />
            </div>
          </div>

          {/* Line items */}
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <Label className="text-sm font-medium">Kalemler *</Label>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="h-7 gap-1 text-xs"
                onClick={() => append({ description: '', quantity: 1, unitPrice: 0, vatRate: 20, discountValue: 0, unit: 'Adet' })}
              >
                <Plus className="h-3 w-3" /> Kalem Ekle
              </Button>
            </div>

            {form.formState.errors.lines && (
              <p className="text-xs text-destructive">En az bir kalem ekleyin</p>
            )}

            <div className="rounded-lg border border-border overflow-hidden">
              {/* Header */}
              <div className="grid grid-cols-[2fr_1fr_1fr_1fr_1fr_auto] gap-2 px-3 py-2 bg-muted/40 text-xs font-medium text-muted-foreground">
                <span>Açıklama</span>
                <span>Miktar</span>
                <span>Birim Fiyat</span>
                <span>KDV %</span>
                <span>İsk. %</span>
                <span />
              </div>

              {fields.map((field, idx) => (
                <div key={field.id} className="grid grid-cols-[2fr_1fr_1fr_1fr_1fr_auto] gap-2 px-3 py-2 border-t border-border/50 items-center">
                  <Input
                    {...form.register(`lines.${idx}.description`)}
                    className="h-8 text-sm"
                    placeholder="Ürün / hizmet"
                  />
                  <Input
                    type="number"
                    step="0.01"
                    {...form.register(`lines.${idx}.quantity`, { valueAsNumber: true })}
                    className="h-8 text-sm"
                  />
                  <Input
                    type="number"
                    step="0.01"
                    {...form.register(`lines.${idx}.unitPrice`, { valueAsNumber: true })}
                    className="h-8 text-sm"
                  />
                  <Select
                    defaultValue="20"
                    onValueChange={v => form.setValue(`lines.${idx}.vatRate`, Number(v))}
                  >
                    <SelectTrigger className="h-8 text-sm">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {[0, 1, 8, 10, 20].map(r => (
                        <SelectItem key={r} value={String(r)}>%{r}</SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  <Input
                    type="number"
                    step="0.01"
                    {...form.register(`lines.${idx}.discountValue`, { valueAsNumber: true })}
                    className="h-8 text-sm"
                    placeholder="0"
                  />
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="h-8 w-8 text-muted-foreground hover:text-destructive"
                    onClick={() => remove(idx)}
                    disabled={fields.length === 1}
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </Button>
                </div>
              ))}
            </div>

            {/* Totals */}
            <div className="flex flex-col items-end gap-1 text-sm pt-1">
              <div className="flex gap-6 text-muted-foreground">
                <span>Ara Toplam:</span>
                <span className="font-mono w-28 text-right">{formatCurrency(subtotal)}</span>
              </div>
              <div className="flex gap-6 text-muted-foreground">
                <span>KDV:</span>
                <span className="font-mono w-28 text-right">{formatCurrency(vat)}</span>
              </div>
              <div className="flex gap-6 font-semibold text-foreground border-t border-border pt-1">
                <span>Genel Toplam:</span>
                <span className="font-mono w-28 text-right">{formatCurrency(subtotal + vat)}</span>
              </div>
            </div>
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose}>İptal</Button>
            <Button type="submit" disabled={createInvoice.isPending} className="gap-2">
              {createInvoice.isPending && <Loader2 className="h-4 w-4 animate-spin" />}
              Fatura Oluştur
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export function InvoicesPage() {
  const navigate = useNavigate();
  const { currentProjectId } = useAuthStore();
  const canAccess = useCanAccessFinance();
  const [page, setPage] = useState(1);
  const [showCreate, setShowCreate] = useState(false);

  const statusQuery = useParasutStatus(currentProjectId);
  const invoicesQuery = useParasutInvoices(currentProjectId, page, true);

  // Redirect unauthorized
  if (!canAccess) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[60vh] gap-4 text-center">
        <AlertCircle className="h-12 w-12 text-muted-foreground/40" />
        <div>
          <p className="font-semibold text-foreground">Erişim Yetkiniz Yok</p>
          <p className="text-sm text-muted-foreground mt-1">Bu sayfayı görüntülemek için Muhasebe rolü gereklidir.</p>
        </div>
        <Button variant="outline" onClick={() => navigate('/dashboard')}>Panele Dön</Button>
      </div>
    );
  }

  if (!statusQuery.data?.isConnected && !statusQuery.isLoading) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[60vh] gap-4 text-center">
        <FileText className="h-12 w-12 text-muted-foreground/40" />
        <div>
          <p className="font-semibold text-foreground">Paraşüt Bağlı Değil</p>
          <p className="text-sm text-muted-foreground mt-1">
            Fatura sistemini kullanmak için önce Ayarlar sayfasından Paraşüt'e bağlanın.
          </p>
        </div>
        <Button variant="outline" onClick={() => navigate('/settings')}>Ayarlara Git</Button>
      </div>
    );
  }

  const invoices = invoicesQuery.data?.items ?? [];
  const totalCount = invoicesQuery.data?.totalCount ?? 0;
  const totalPages = invoicesQuery.data?.totalPages ?? 1;

  // Aggregate stats from loaded invoices
  const totalGross  = invoices.reduce((s, i) => s + i.grossTotal, 0);
  const totalPaid   = invoices.reduce((s, i) => s + i.totalPaid, 0);
  const totalRemain = invoices.reduce((s, i) => s + i.remaining, 0);
  const overdueCount = invoices.filter(
    i => i.remaining > 0 && new Date(i.dueDate) < new Date()
  ).length;

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-foreground">Faturalar</h1>
          <p className="text-muted-foreground text-sm mt-1">Paraşüt satış faturaları</p>
        </div>
        <Button onClick={() => setShowCreate(true)} className="gap-2" disabled={!statusQuery.data?.isConnected}>
          <Plus className="h-4 w-4" /> Yeni Fatura
        </Button>
      </div>

      {/* Stats */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <Card>
          <CardContent className="p-4">
            <div className="flex items-center gap-3">
              <div className="p-2 rounded-lg bg-blue-500/10">
                <FileText className="h-4 w-4 text-blue-400" />
              </div>
              <div>
                <p className="text-xs text-muted-foreground">Toplam Fatura</p>
                <p className="text-lg font-bold text-foreground">{totalCount}</p>
              </div>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="p-4">
            <div className="flex items-center gap-3">
              <div className="p-2 rounded-lg bg-indigo-500/10">
                <TrendingUp className="h-4 w-4 text-indigo-400" />
              </div>
              <div>
                <p className="text-xs text-muted-foreground">Toplam Tutar</p>
                <p className="text-lg font-bold text-foreground">{formatCurrency(totalGross)}</p>
              </div>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="p-4">
            <div className="flex items-center gap-3">
              <div className="p-2 rounded-lg bg-emerald-500/10">
                <Wallet className="h-4 w-4 text-emerald-400" />
              </div>
              <div>
                <p className="text-xs text-muted-foreground">Tahsil Edilen</p>
                <p className="text-lg font-bold text-emerald-400">{formatCurrency(totalPaid)}</p>
              </div>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="p-4">
            <div className="flex items-center gap-3">
              <div className="p-2 rounded-lg bg-amber-500/10">
                <AlertCircle className="h-4 w-4 text-amber-400" />
              </div>
              <div>
                <p className="text-xs text-muted-foreground">
                  Kalan {overdueCount > 0 && <span className="text-red-400">({overdueCount} vadesi geçmiş)</span>}
                </p>
                <p className="text-lg font-bold text-amber-400">{formatCurrency(totalRemain)}</p>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Invoice table */}
      <Card>
        <CardHeader className="flex-row items-center justify-between pb-3">
          <CardTitle className="text-base">Fatura Listesi</CardTitle>
          <Button
            variant="ghost"
            size="sm"
            className="gap-1.5 text-muted-foreground"
            onClick={() => invoicesQuery.refetch()}
            disabled={invoicesQuery.isFetching}
          >
            <RefreshCw className={`h-3.5 w-3.5 ${invoicesQuery.isFetching ? 'animate-spin' : ''}`} />
            Yenile
          </Button>
        </CardHeader>
        <CardContent className="p-0">
          {invoicesQuery.isLoading ? (
            <div className="space-y-0 divide-y divide-border">
              {Array.from({ length: 6 }).map((_, i) => (
                <div key={i} className="flex items-center gap-4 px-6 py-4">
                  <Skeleton className="h-4 w-24" />
                  <Skeleton className="h-4 w-20" />
                  <Skeleton className="h-4 w-20" />
                  <Skeleton className="h-4 w-24 ml-auto" />
                  <Skeleton className="h-5 w-20" />
                </div>
              ))}
            </div>
          ) : invoices.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-16 text-center">
              <FileText className="h-10 w-10 text-muted-foreground/30 mb-3" />
              <p className="text-sm font-medium text-muted-foreground">Fatura bulunamadı</p>
              <p className="text-xs text-muted-foreground/70 mt-1">Paraşüt'te henüz fatura yok veya bağlantıyı kontrol edin.</p>
            </div>
          ) : (
            <>
              {/* Table header */}
              <div className="grid grid-cols-[auto_1fr_1fr_1fr_1fr_1fr_1fr] gap-4 px-6 py-3 border-b border-border bg-muted/30 text-xs font-medium text-muted-foreground">
                <span className="w-24">Paraşüt ID</span>
                <span>Açıklama</span>
                <span>Fatura Tarihi</span>
                <span>Vade Tarihi</span>
                <span className="text-right">Toplam</span>
                <span className="text-right">Kalan</span>
                <span>Durum</span>
              </div>

              <div className="divide-y divide-border">
                {invoices.map(invoice => (
                  <div
                    key={invoice.id}
                    className="grid grid-cols-[auto_1fr_1fr_1fr_1fr_1fr_1fr] gap-4 px-6 py-3.5 items-center hover:bg-muted/20 transition-colors text-sm"
                  >
                    <span className="w-24 font-mono text-xs text-muted-foreground">#{invoice.id}</span>
                    <span className="text-foreground truncate">{invoice.description || '—'}</span>
                    <span className="text-muted-foreground">{formatDate(invoice.issueDate)}</span>
                    <span className={invoice.remaining > 0 && new Date(invoice.dueDate) < new Date()
                      ? 'text-red-400 font-medium'
                      : 'text-muted-foreground'
                    }>
                      {formatDate(invoice.dueDate)}
                    </span>
                    <span className="text-right font-medium text-foreground">
                      {formatCurrency(invoice.grossTotal, invoice.currency)}
                    </span>
                    <span className="text-right font-medium text-amber-400">
                      {formatCurrency(invoice.remaining, invoice.currency)}
                    </span>
                    <InvoiceStatusBadge invoice={invoice} />
                  </div>
                ))}
              </div>

              {/* Pagination */}
              {totalPages > 1 && (
                <div className="flex items-center justify-between px-6 py-3 border-t border-border">
                  <p className="text-xs text-muted-foreground">{totalCount} fatura</p>
                  <div className="flex items-center gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => setPage(p => p - 1)}
                      disabled={page <= 1}
                      className="h-8 w-8 p-0"
                    >
                      <ChevronLeft className="h-4 w-4" />
                    </Button>
                    <span className="text-xs text-muted-foreground">{page} / {totalPages}</span>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => setPage(p => p + 1)}
                      disabled={page >= totalPages}
                      className="h-8 w-8 p-0"
                    >
                      <ChevronRight className="h-4 w-4" />
                    </Button>
                  </div>
                </div>
              )}
            </>
          )}
        </CardContent>
      </Card>

      {/* Create dialog */}
      {showCreate && currentProjectId && (
        <CreateInvoiceDialog
          open={showCreate}
          onClose={() => setShowCreate(false)}
          projectId={currentProjectId}
        />
      )}
    </div>
  );
}
