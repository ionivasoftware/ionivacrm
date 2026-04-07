import { useState, useMemo } from 'react';
import type { AxiosError } from 'axios';
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
  Send,
  Search,
  CheckCircle2,
  Clock,
  ExternalLink,
  Pencil,
  Merge,
  X,
} from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
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
import { useCustomers } from '@/api/customers';
import { useInvoices, useCreateInvoice, useUpdateInvoice, useTransferToParasut, useDeleteInvoice, useMergeInvoices } from '@/api/invoices';
import { useParasutStatus } from '@/api/parasut';
import { useToast } from '@/hooks/use-toast';
import { useNavigate } from 'react-router-dom';
import type { Invoice, InvoiceStatus, InvoiceLineItem, Customer } from '@/types';

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

// ── Status Badge ─────────────────────────────────────────────────────────────

const statusConfig: Record<InvoiceStatus, { label: string; className: string }> = {
  Draft: { label: 'Taslak', className: 'bg-slate-500/15 text-slate-400 border-slate-500/30' },
  TransferredToParasut: { label: 'Paraşüt\'e Aktarıldı', className: 'bg-blue-500/15 text-blue-400 border-blue-500/30' },
  Paid: { label: 'Ödendi', className: 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30' },
  Cancelled: { label: 'İptal', className: 'bg-red-500/15 text-red-400 border-red-500/30' },
};

function InvoiceStatusBadge({ status }: { status: InvoiceStatus }) {
  const config = statusConfig[status] ?? statusConfig.Draft;
  return <Badge className={config.className}>{config.label}</Badge>;
}

// ── Customer Search Combobox ─────────────────────────────────────────────────

interface CustomerSelectProps {
  value: string;
  onChange: (customerId: string, customer: Customer | null) => void;
}

function CustomerSelect({ value, onChange }: CustomerSelectProps) {
  const [search, setSearch] = useState('');
  const [open, setOpen] = useState(false);
  const customersQuery = useCustomers({ search, pageSize: 15 });
  const customers = customersQuery.data?.items ?? [];

  const selectedCustomer = customers.find(c => c.id === value);

  return (
    <div className="relative">
      <div
        className="flex items-center h-9 w-full rounded-md border border-input bg-background px-3 py-1 text-sm cursor-pointer hover:bg-accent/50"
        onClick={() => setOpen(!open)}
      >
        {value && selectedCustomer ? (
          <span className="truncate">{selectedCustomer.companyName}</span>
        ) : value ? (
          <span className="truncate text-muted-foreground">Seçili müşteri</span>
        ) : (
          <span className="text-muted-foreground">Müşteri seçin...</span>
        )}
      </div>

      {open && (
        <div className="absolute z-50 mt-1 w-full rounded-md border border-border bg-popover shadow-lg">
          <div className="flex items-center gap-2 p-2 border-b border-border">
            <Search className="h-3.5 w-3.5 text-muted-foreground" />
            <input
              type="text"
              className="flex-1 bg-transparent text-sm outline-none placeholder:text-muted-foreground"
              placeholder="Müşteri ara..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              autoFocus
            />
          </div>
          <div className="max-h-48 overflow-y-auto p-1">
            {customersQuery.isLoading ? (
              <div className="px-3 py-2 text-xs text-muted-foreground">Yükleniyor...</div>
            ) : customers.length === 0 ? (
              <div className="px-3 py-2 text-xs text-muted-foreground">Sonuç bulunamadı</div>
            ) : (
              customers.map((c) => (
                <button
                  key={c.id}
                  type="button"
                  className="w-full flex items-center justify-between px-3 py-2 text-sm rounded-sm hover:bg-accent text-left"
                  onClick={() => {
                    onChange(c.id, c);
                    setOpen(false);
                    setSearch('');
                  }}
                >
                  <div className="min-w-0">
                    <div className="font-medium truncate">{c.companyName}</div>
                    {c.contactName && (
                      <div className="text-xs text-muted-foreground truncate">{c.contactName}</div>
                    )}
                  </div>
                  {c.parasutContactId && (
                    <Badge variant="outline" className="text-[10px] ml-2 shrink-0 border-blue-500/30 text-blue-400">
                      E-Fatura
                    </Badge>
                  )}
                </button>
              ))
            )}
          </div>
        </div>
      )}
    </div>
  );
}

// ── Create Invoice Dialog ────────────────────────────────────────────────────

type DiscountType = 'percentage' | 'amount';

interface InvoiceLineForm {
  description: string;
  quantity: number;
  unitPrice: number;
  vatRate: number;
  discountValue: number;
  discountType: DiscountType;
  unit: string;
}

/** Returns the absolute discount amount for a single line */
function calcLineDiscount(line: InvoiceLineForm): number {
  const lineTotal = (line.quantity || 0) * (line.unitPrice || 0);
  if (line.discountType === 'amount') {
    return Math.min(line.discountValue || 0, lineTotal);
  }
  return lineTotal * ((line.discountValue || 0) / 100);
}

interface InvoiceFormData {
  customerId: string;
  title: string;
  description: string;
  invoiceSeries: string;
  issueDate: string;
  dueDate: string;
  currency: string;
  lines: InvoiceLineForm[];
}

interface CreateInvoiceDialogProps {
  open: boolean;
  onClose: () => void;
}

function CreateInvoiceDialog({ open, onClose }: CreateInvoiceDialogProps) {
  const { toast } = useToast();
  const createInvoice = useCreateInvoice();
  const [selectedCustomer, setSelectedCustomer] = useState<Customer | null>(null);

  const form = useForm<InvoiceFormData>({
    defaultValues: {
      customerId: '',
      title: '',
      description: '',
      invoiceSeries: 'A',
      issueDate: today(),
      dueDate: daysLater(30),
      currency: 'TRL',
      lines: [{ description: '', quantity: 1, unitPrice: 0, vatRate: 20, discountValue: 0, discountType: 'percentage', unit: 'Adet' }],
    },
  });

  const { fields, append, remove } = useFieldArray({ control: form.control, name: 'lines' });

  const watchedLines = form.watch('lines');

  // Raw total before any discount
  const rawTotal = watchedLines.reduce(
    (acc, l) => acc + (l.quantity || 0) * (l.unitPrice || 0),
    0
  );
  // Total discount amount across all lines
  const totalDiscount = watchedLines.reduce((acc, l) => acc + calcLineDiscount(l), 0);
  // Net subtotal (after discounts, before VAT)
  const subtotal = rawTotal - totalDiscount;
  // VAT calculated on discounted amounts
  const vat = watchedLines.reduce((acc, l) => {
    const lineTotal = (l.quantity || 0) * (l.unitPrice || 0);
    const discount = calcLineDiscount(l);
    return acc + (lineTotal - discount) * ((l.vatRate || 0) / 100);
  }, 0);

  async function onSubmit(data: InvoiceFormData) {
    if (!data.customerId) {
      toast({ title: 'Hata', description: 'Müşteri seçimi zorunludur.', variant: 'destructive' });
      return;
    }
    if (!data.title.trim()) {
      toast({ title: 'Hata', description: 'Fatura başlığı zorunludur.', variant: 'destructive' });
      return;
    }

    const lines: InvoiceLineItem[] = data.lines.map(l => ({
      description: l.description || '',
      quantity: l.quantity,
      unitPrice: l.unitPrice,
      vatRate: l.vatRate,
      discountValue: l.discountValue,
      discountType: l.discountType,
      unit: l.unit || 'Adet',
    }));

    const netTotal = lines.reduce((acc, l) => {
      const lt = l.quantity * l.unitPrice;
      const d = l.discountType === 'amount'
        ? Math.min(l.discountValue, lt)
        : lt * (l.discountValue / 100);
      return acc + lt - d;
    }, 0);

    const grossTotal = lines.reduce((acc, l) => {
      const lt = l.quantity * l.unitPrice;
      const d = l.discountType === 'amount'
        ? Math.min(l.discountValue, lt)
        : lt * (l.discountValue / 100);
      const net = lt - d;
      return acc + net + net * (l.vatRate / 100);
    }, 0);

    try {
      await createInvoice.mutateAsync({
        customerId: data.customerId,
        title: data.title,
        description: data.description || undefined,
        invoiceSeries: data.invoiceSeries || undefined,
        issueDate: data.issueDate,
        dueDate: data.dueDate,
        currency: data.currency,
        grossTotal,
        netTotal,
        linesJson: JSON.stringify(lines),
      });
      toast({ title: 'Fatura oluşturuldu', description: 'Taslak olarak kaydedildi.' });
      form.reset();
      setSelectedCustomer(null);
      onClose();
    } catch {
      toast({ title: 'Hata', description: 'Fatura oluşturulamadı.', variant: 'destructive' });
    }
  }

  return (
    <Dialog open={open} onOpenChange={onClose}>
      <DialogContent className="max-w-2xl max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Yeni Fatura Oluştur</DialogTitle>
        </DialogHeader>

        <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-5">
          {/* Customer & Title */}
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-1.5">
              <Label>Müşteri *</Label>
              <CustomerSelect
                value={form.watch('customerId')}
                onChange={(id, customer) => {
                  form.setValue('customerId', id);
                  setSelectedCustomer(customer);
                }}
              />
              {selectedCustomer?.parasutContactId && (
                <p className="text-xs text-blue-400 flex items-center gap-1">
                  <CheckCircle2 className="h-3 w-3" /> Paraşüt carisi bağlı — aktarımda otomatik eşleşecek
                </p>
              )}
            </div>
            <div className="space-y-1.5">
              <Label>Fatura Başlığı *</Label>
              <Input
                {...form.register('title')}
                className="h-9"
                placeholder="Ör: Yıllık lisans faturası"
              />
            </div>
          </div>

          {/* Dates & Currency */}
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-1.5">
              <Label>Fatura Tarihi *</Label>
              <Input type="date" {...form.register('issueDate')} className="h-9" />
            </div>
            <div className="space-y-1.5">
              <Label>Vade Tarihi *</Label>
              <Input type="date" {...form.register('dueDate')} className="h-9" />
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
              <Label>Fatura Serisi</Label>
              <Input {...form.register('invoiceSeries')} className="h-9" placeholder="A" />
            </div>
          </div>

          {/* Description */}
          <div className="space-y-1.5">
            <Label>Açıklama</Label>
            <Textarea
              {...form.register('description')}
              className="resize-none"
              rows={2}
              placeholder="İsteğe bağlı açıklama"
            />
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
                onClick={() => append({ description: '', quantity: 1, unitPrice: 0, vatRate: 20, discountValue: 0, discountType: 'percentage', unit: 'Adet' })}
              >
                <Plus className="h-3 w-3" /> Kalem Ekle
              </Button>
            </div>

            <div className="rounded-lg border border-border overflow-hidden">
              {/* Header */}
              <div className="grid grid-cols-[2fr_0.8fr_1fr_0.8fr_1.5fr_auto] gap-2 px-3 py-2 bg-muted/40 text-xs font-medium text-muted-foreground">
                <span>Açıklama</span>
                <span>Miktar</span>
                <span>Birim Fiyat</span>
                <span>KDV %</span>
                <span>İskonto</span>
                <span />
              </div>

              {fields.map((field, idx) => (
                <div key={field.id} className="grid grid-cols-[2fr_0.8fr_1fr_0.8fr_1.5fr_auto] gap-2 px-3 py-2 border-t border-border/50 items-center">
                  <Input
                    {...form.register(`lines.${idx}.description`)}
                    className="h-8 text-sm"
                    placeholder="Ürün / hizmet"
                  />
                  <Input
                    type="number"
                    step="0.01"
                    min="0"
                    {...form.register(`lines.${idx}.quantity`, { valueAsNumber: true })}
                    className="h-8 text-sm"
                  />
                  <Input
                    type="number"
                    step="0.01"
                    min="0"
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
                  {/* Compound discount: value + type toggle */}
                  <div className="flex h-8 rounded-md border border-input overflow-hidden text-sm focus-within:ring-1 focus-within:ring-ring">
                    <input
                      type="number"
                      step="0.01"
                      min="0"
                      {...form.register(`lines.${idx}.discountValue`, { valueAsNumber: true })}
                      className="w-0 flex-1 bg-transparent px-2 text-sm outline-none placeholder:text-muted-foreground"
                      placeholder="0"
                    />
                    <select
                      {...form.register(`lines.${idx}.discountType`)}
                      className="border-l border-input bg-muted text-xs px-1.5 cursor-pointer outline-none text-muted-foreground"
                    >
                      <option value="percentage">%</option>
                      <option value="amount">₺</option>
                    </select>
                  </div>
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
                <span className="font-mono w-28 text-right">{formatCurrency(rawTotal)}</span>
              </div>
              {totalDiscount > 0 && (
                <div className="flex gap-6 text-orange-400">
                  <span>İskonto:</span>
                  <span className="font-mono w-28 text-right">-{formatCurrency(totalDiscount)}</span>
                </div>
              )}
              {totalDiscount > 0 && (
                <div className="flex gap-6 text-muted-foreground">
                  <span>Net Toplam:</span>
                  <span className="font-mono w-28 text-right">{formatCurrency(subtotal)}</span>
                </div>
              )}
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
              Taslak Olarak Kaydet
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ── Edit Invoice Dialog ──────────────────────────────────────────────────────

interface EditInvoiceDialogProps {
  invoice: Invoice;
  onClose: () => void;
}

function EditInvoiceDialog({ invoice, onClose }: EditInvoiceDialogProps) {
  const { toast } = useToast();
  const updateInvoice = useUpdateInvoice();

  const parsedLines: InvoiceLineForm[] = useMemo(() => {
    try {
      const raw: InvoiceLineItem[] = JSON.parse(invoice.linesJson);
      return raw.map(l => ({
        description: l.description ?? '',
        quantity: l.quantity,
        unitPrice: l.unitPrice,
        vatRate: l.vatRate,
        discountValue: l.discountValue,
        discountType: (l.discountType === 'percent' ? 'percentage' : l.discountType) as DiscountType,
        unit: l.unit ?? 'Adet',
      }));
    } catch {
      return [{ description: '', quantity: 1, unitPrice: 0, vatRate: 20, discountValue: 0, discountType: 'percentage', unit: 'Adet' }];
    }
  }, [invoice.linesJson]);

  const form = useForm<InvoiceFormData>({
    defaultValues: {
      customerId: invoice.customerId,
      title: invoice.title,
      description: invoice.description ?? '',
      invoiceSeries: invoice.invoiceSeries ?? 'A',
      issueDate: invoice.issueDate.split('T')[0],
      dueDate: invoice.dueDate.split('T')[0],
      currency: invoice.currency,
      lines: parsedLines,
    },
  });

  const { fields, append, remove } = useFieldArray({ control: form.control, name: 'lines' });
  const watchedLines = form.watch('lines');

  const rawTotal = watchedLines.reduce((acc, l) => acc + (l.quantity || 0) * (l.unitPrice || 0), 0);
  const totalDiscount = watchedLines.reduce((acc, l) => acc + calcLineDiscount(l), 0);
  const subtotal = rawTotal - totalDiscount;
  const vat = watchedLines.reduce((acc, l) => {
    const lineTotal = (l.quantity || 0) * (l.unitPrice || 0);
    const discount = calcLineDiscount(l);
    return acc + (lineTotal - discount) * ((l.vatRate || 0) / 100);
  }, 0);

  async function onSubmit(data: InvoiceFormData) {
    if (!data.title.trim()) {
      toast({ title: 'Hata', description: 'Fatura başlığı zorunludur.', variant: 'destructive' });
      return;
    }

    const lines: InvoiceLineItem[] = data.lines.map(l => ({
      description: l.description || '',
      quantity: l.quantity,
      unitPrice: l.unitPrice,
      vatRate: l.vatRate,
      discountValue: l.discountValue,
      discountType: l.discountType,
      unit: l.unit || 'Adet',
    }));

    const netTotal = lines.reduce((acc, l) => {
      const lt = l.quantity * l.unitPrice;
      const d = l.discountType === 'amount' ? Math.min(l.discountValue, lt) : lt * (l.discountValue / 100);
      return acc + lt - d;
    }, 0);

    const grossTotal = lines.reduce((acc, l) => {
      const lt = l.quantity * l.unitPrice;
      const d = l.discountType === 'amount' ? Math.min(l.discountValue, lt) : lt * (l.discountValue / 100);
      const net = lt - d;
      return acc + net + net * (l.vatRate / 100);
    }, 0);

    try {
      await updateInvoice.mutateAsync({
        id: invoice.id,
        customerId: invoice.customerId,
        title: data.title,
        description: data.description || undefined,
        invoiceSeries: data.invoiceSeries || undefined,
        issueDate: data.issueDate,
        dueDate: data.dueDate,
        currency: data.currency,
        grossTotal,
        netTotal,
        linesJson: JSON.stringify(lines),
      });
      toast({ title: 'Fatura güncellendi' });
      onClose();
    } catch {
      toast({ title: 'Hata', description: 'Fatura güncellenemedi.', variant: 'destructive' });
    }
  }

  return (
    <Dialog open onOpenChange={onClose}>
      <DialogContent className="max-w-2xl max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Faturayı Düzenle</DialogTitle>
        </DialogHeader>

        <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-5">
          {/* Customer (read-only) & Title */}
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-1.5">
              <Label>Müşteri</Label>
              <div className="flex items-center h-9 w-full rounded-md border border-input bg-muted/40 px-3 py-1 text-sm text-muted-foreground">
                {invoice.customerName}
              </div>
            </div>
            <div className="space-y-1.5">
              <Label>Fatura Başlığı *</Label>
              <Input {...form.register('title')} className="h-9" placeholder="Ör: Yıllık lisans faturası" />
            </div>
          </div>

          {/* Dates & Currency */}
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-1.5">
              <Label>Fatura Tarihi *</Label>
              <Input type="date" {...form.register('issueDate')} className="h-9" />
            </div>
            <div className="space-y-1.5">
              <Label>Vade Tarihi *</Label>
              <Input type="date" {...form.register('dueDate')} className="h-9" />
            </div>
            <div className="space-y-1.5">
              <Label>Döviz</Label>
              <Select
                defaultValue={invoice.currency}
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
              <Label>Fatura Serisi</Label>
              <Input {...form.register('invoiceSeries')} className="h-9" placeholder="A" />
            </div>
          </div>

          {/* Description */}
          <div className="space-y-1.5">
            <Label>Açıklama</Label>
            <Textarea {...form.register('description')} className="resize-none" rows={2} placeholder="İsteğe bağlı açıklama" />
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
                onClick={() => append({ description: '', quantity: 1, unitPrice: 0, vatRate: 20, discountValue: 0, discountType: 'percentage', unit: 'Adet' })}
              >
                <Plus className="h-3 w-3" /> Kalem Ekle
              </Button>
            </div>

            <div className="rounded-lg border border-border overflow-hidden">
              <div className="grid grid-cols-[2fr_0.8fr_1fr_0.8fr_1.5fr_auto] gap-2 px-3 py-2 bg-muted/40 text-xs font-medium text-muted-foreground">
                <span>Açıklama</span>
                <span>Miktar</span>
                <span>Birim Fiyat</span>
                <span>KDV %</span>
                <span>İskonto</span>
                <span />
              </div>

              {fields.map((field, idx) => (
                <div key={field.id} className="grid grid-cols-[2fr_0.8fr_1fr_0.8fr_1.5fr_auto] gap-2 px-3 py-2 border-t border-border/50 items-center">
                  <Input {...form.register(`lines.${idx}.description`)} className="h-8 text-sm" placeholder="Ürün / hizmet" />
                  <Input type="number" step="0.01" min="0" {...form.register(`lines.${idx}.quantity`, { valueAsNumber: true })} className="h-8 text-sm" />
                  <Input type="number" step="0.01" min="0" {...form.register(`lines.${idx}.unitPrice`, { valueAsNumber: true })} className="h-8 text-sm" />
                  <Select
                    defaultValue={String(parsedLines[idx]?.vatRate ?? 20)}
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
                  <div className="flex h-8 rounded-md border border-input overflow-hidden text-sm focus-within:ring-1 focus-within:ring-ring">
                    <input
                      type="number"
                      step="0.01"
                      min="0"
                      {...form.register(`lines.${idx}.discountValue`, { valueAsNumber: true })}
                      className="w-0 flex-1 bg-transparent px-2 text-sm outline-none placeholder:text-muted-foreground"
                      placeholder="0"
                    />
                    <select
                      {...form.register(`lines.${idx}.discountType`)}
                      className="border-l border-input bg-muted text-xs px-1.5 cursor-pointer outline-none text-muted-foreground"
                    >
                      <option value="percentage">%</option>
                      <option value="amount">₺</option>
                    </select>
                  </div>
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
                <span className="font-mono w-28 text-right">{formatCurrency(rawTotal)}</span>
              </div>
              {totalDiscount > 0 && (
                <div className="flex gap-6 text-orange-400">
                  <span>İskonto:</span>
                  <span className="font-mono w-28 text-right">-{formatCurrency(totalDiscount)}</span>
                </div>
              )}
              {totalDiscount > 0 && (
                <div className="flex gap-6 text-muted-foreground">
                  <span>Net Toplam:</span>
                  <span className="font-mono w-28 text-right">{formatCurrency(subtotal)}</span>
                </div>
              )}
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
            <Button type="submit" disabled={updateInvoice.isPending} className="gap-2">
              {updateInvoice.isPending && <Loader2 className="h-4 w-4 animate-spin" />}
              Kaydet
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ── Transfer Confirm Dialog ──────────────────────────────────────────────────

interface TransferDialogProps {
  invoice: Invoice | null;
  open: boolean;
  onClose: () => void;
}

function TransferToParasutDialog({ invoice, open, onClose }: TransferDialogProps) {
  const { toast } = useToast();
  const transfer = useTransferToParasut();

  if (!invoice) return null;

  async function handleTransfer() {
    try {
      await transfer.mutateAsync(invoice!.id);
      toast({
        title: 'Paraşüt\'e aktarıldı',
        description: `"${invoice!.title}" faturası başarıyla Paraşüt'e gönderildi.`,
      });
      onClose();
    } catch (err) {
      const axiosErr = err as AxiosError<{ errors?: string[] }>;
      const apiMsg = axiosErr.response?.data?.errors?.[0];
      toast({
        title: 'Aktarım hatası',
        description: apiMsg ?? 'Fatura Paraşüt\'e aktarılamadı. Paraşüt bağlantısını kontrol edin.',
        variant: 'destructive',
      });
    }
  }

  return (
    <Dialog open={open} onOpenChange={onClose}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Paraşüt'e Aktar</DialogTitle>
        </DialogHeader>
        <div className="space-y-3 text-sm">
          <p className="text-muted-foreground">
            Bu fatura Paraşüt'e satış faturası olarak aktarılacaktır. İşlem geri alınamaz.
          </p>
          <div className="rounded-lg border border-border p-3 space-y-1.5">
            <div className="flex justify-between">
              <span className="text-muted-foreground">Fatura:</span>
              <span className="font-medium">{invoice.title}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">Müşteri:</span>
              <span>{invoice.customerName}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">Tutar:</span>
              <span className="font-mono font-medium">{formatCurrency(invoice.grossTotal, invoice.currency)}</span>
            </div>
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>İptal</Button>
          <Button onClick={handleTransfer} disabled={transfer.isPending} className="gap-2">
            {transfer.isPending && <Loader2 className="h-4 w-4 animate-spin" />}
            <Send className="h-4 w-4" />
            Paraşüt'e Gönder
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Main Page ────────────────────────────────────────────────────────────────

const PAGE_SIZE = 20;

export function InvoicesPage() {
  const navigate = useNavigate();
  const { currentProjectId } = useAuthStore();
  const canAccess = useCanAccessFinance();
  const { toast } = useToast();
  const [showCreate, setShowCreate] = useState(false);
  const [editInvoice, setEditInvoice] = useState<Invoice | null>(null);
  const [transferInvoice, setTransferInvoice] = useState<Invoice | null>(null);
  const [deleteInvoice, setDeleteInvoice] = useState<Invoice | null>(null);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [statusFilter, setStatusFilter] = useState<InvoiceStatus | 'all'>('all');
  const [searchQuery, setSearchQuery] = useState('');
  const [page, setPage] = useState(1);

  const invoicesQuery = useInvoices();
  const parasutStatus = useParasutStatus(currentProjectId);
  const deleteInvoiceMutation = useDeleteInvoice();
  const mergeInvoicesMutation = useMergeInvoices();

  const isParasutConnected = parasutStatus.data?.isConnected ?? false;

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

  const allInvoices = invoicesQuery.data ?? [];

  // Selection helpers
  function handleSelect(id: string, checked: boolean) {
    setSelectedIds(prev => {
      const next = new Set(prev);
      checked ? next.add(id) : next.delete(id);
      return next;
    });
  }

  const selectedInvoices = allInvoices.filter(i => selectedIds.has(i.id));
  const allSameCustomer = selectedInvoices.length >= 2
    && selectedInvoices.every(i => i.customerId === selectedInvoices[0].customerId);
  const canMerge = allSameCustomer;

  async function handleDelete() {
    if (!deleteInvoice) return;
    try {
      await deleteInvoiceMutation.mutateAsync(deleteInvoice.id);
      toast({ title: 'Fatura silindi' });
      setDeleteInvoice(null);
      setSelectedIds(prev => { const n = new Set(prev); n.delete(deleteInvoice.id); return n; });
    } catch {
      toast({ title: 'Hata', description: 'Fatura silinemedi.', variant: 'destructive' });
    }
  }

  async function handleMerge() {
    if (!canMerge) return;
    try {
      const merged = await mergeInvoicesMutation.mutateAsync({ invoiceIds: Array.from(selectedIds) });
      toast({ title: 'Faturalar birleştirildi', description: merged?.title });
      setSelectedIds(new Set());
    } catch {
      toast({ title: 'Hata', description: 'Faturalar birleştirilemedi.', variant: 'destructive' });
    }
  }

  // Client-side filter & search
  const filtered = useMemo(() => {
    let result = allInvoices;
    if (statusFilter !== 'all') {
      result = result.filter(i => i.status === statusFilter);
    }
    if (searchQuery.trim()) {
      const q = searchQuery.toLowerCase();
      result = result.filter(i =>
        i.title.toLowerCase().includes(q) ||
        i.customerName.toLowerCase().includes(q) ||
        (i.description ?? '').toLowerCase().includes(q) ||
        (i.invoiceSeries ?? '').toLowerCase().includes(q)
      );
    }
    return result;
  }, [allInvoices, statusFilter, searchQuery]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const paginated = filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE);

  // Stats
  const draftCount = allInvoices.filter(i => i.status === 'Draft').length;
  const transferredCount = allInvoices.filter(i => i.status === 'TransferredToParasut').length;
  const totalGross = allInvoices.reduce((s, i) => s + i.grossTotal, 0);

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-foreground">Faturalar</h1>
          <p className="text-muted-foreground text-sm mt-1">CRM faturaları — oluşturun, yönetin ve Paraşüt'e aktarın</p>
        </div>
        <Button onClick={() => setShowCreate(true)} className="gap-2">
          <Plus className="h-4 w-4" /> Yeni Fatura
        </Button>
      </div>

      {/* Stats cards */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        <Card>
          <CardContent className="p-4">
            <div className="flex items-center gap-3">
              <div className="p-2 rounded-lg bg-blue-500/10">
                <FileText className="h-4 w-4 text-blue-400" />
              </div>
              <div>
                <p className="text-xs text-muted-foreground">Toplam Fatura</p>
                <p className="text-lg font-bold text-foreground">{allInvoices.length}</p>
              </div>
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="p-4">
            <div className="flex items-center gap-3">
              <div className="p-2 rounded-lg bg-slate-500/10">
                <Clock className="h-4 w-4 text-slate-400" />
              </div>
              <div>
                <p className="text-xs text-muted-foreground">Taslak</p>
                <p className="text-lg font-bold text-foreground">{draftCount}</p>
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
                <p className="text-xs text-muted-foreground">Toplam Tutar (Brüt)</p>
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
                <p className="text-xs text-muted-foreground">Paraşüt'e Aktarılan</p>
                <p className="text-lg font-bold text-emerald-400">{transferredCount}</p>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Filters */}
      <div className="flex items-center gap-3 flex-wrap">
        <div className="relative flex-1 max-w-sm">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
          <Input
            className="pl-9 h-9"
            placeholder="Fatura ara..."
            value={searchQuery}
            onChange={(e) => { setSearchQuery(e.target.value); setPage(1); }}
          />
        </div>
        <Select
          value={statusFilter}
          onValueChange={(v) => { setStatusFilter(v as InvoiceStatus | 'all'); setPage(1); }}
        >
          <SelectTrigger className="h-9 w-44">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Tüm Durumlar</SelectItem>
            <SelectItem value="Draft">Taslak</SelectItem>
            <SelectItem value="TransferredToParasut">Paraşüt'e Aktarıldı</SelectItem>
            <SelectItem value="Paid">Ödendi</SelectItem>
            <SelectItem value="Cancelled">İptal</SelectItem>
          </SelectContent>
        </Select>

        {/* Merge action bar — visible when drafts are selected */}
        {selectedIds.size > 0 && (
          <div className="flex items-center gap-2 ml-auto bg-muted/60 border border-border rounded-lg px-3 py-1.5">
            <span className="text-sm text-muted-foreground">{selectedIds.size} seçili</span>
            {canMerge ? (
              <Button
                size="sm"
                className="h-7 gap-1.5 text-xs"
                onClick={handleMerge}
                disabled={mergeInvoicesMutation.isPending}
              >
                {mergeInvoicesMutation.isPending
                  ? <Loader2 className="h-3 w-3 animate-spin" />
                  : <Merge className="h-3 w-3" />}
                Birleştir
              </Button>
            ) : (
              <span className="text-xs text-amber-400">Birleştirmek için aynı müşteriye ait taslakları seçin</span>
            )}
            <Button
              variant="ghost"
              size="sm"
              className="h-7 w-7 p-0 text-muted-foreground"
              onClick={() => setSelectedIds(new Set())}
            >
              <X className="h-3.5 w-3.5" />
            </Button>
          </div>
        )}
      </div>

      {/* Invoice table */}
      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-base">Fatura Listesi</CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          {invoicesQuery.isLoading ? (
            <div className="space-y-0 divide-y divide-border">
              {Array.from({ length: 6 }).map((_, i) => (
                <div key={i} className="flex items-center gap-4 px-6 py-4">
                  <Skeleton className="h-4 w-32" />
                  <Skeleton className="h-4 w-24" />
                  <Skeleton className="h-4 w-20" />
                  <Skeleton className="h-4 w-24 ml-auto" />
                  <Skeleton className="h-5 w-20" />
                </div>
              ))}
            </div>
          ) : paginated.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-16 text-center">
              <FileText className="h-10 w-10 text-muted-foreground/30 mb-3" />
              <p className="text-sm font-medium text-muted-foreground">
                {allInvoices.length === 0 ? 'Henüz fatura yok' : 'Filtrelerle eşleşen fatura bulunamadı'}
              </p>
              {allInvoices.length === 0 && (
                <p className="text-xs text-muted-foreground/70 mt-1">
                  Yeni Fatura butonuna tıklayarak ilk faturanızı oluşturun.
                </p>
              )}
            </div>
          ) : (
            <>
              {/* Table header */}
              <div className="grid grid-cols-[2rem_1.5fr_1fr_0.8fr_0.8fr_0.8fr_1fr_0.8fr_7rem] gap-4 px-4 py-3 border-b border-border bg-muted/30 text-xs font-medium text-muted-foreground">
                <span />
                <span>Fatura</span>
                <span>Müşteri</span>
                <span>Proje</span>
                <span>Tarih</span>
                <span>Vade</span>
                <span className="text-right">Tutar</span>
                <span>Durum</span>
                <span />
              </div>

              <div className="divide-y divide-border">
                {paginated.map(invoice => (
                  <InvoiceRow
                    key={invoice.id}
                    invoice={invoice}
                    isParasutConnected={isParasutConnected}
                    selected={selectedIds.has(invoice.id)}
                    onSelect={handleSelect}
                    onEdit={() => setEditInvoice(invoice)}
                    onTransfer={() => setTransferInvoice(invoice)}
                    onDelete={() => setDeleteInvoice(invoice)}
                  />
                ))}
              </div>

              {/* Pagination */}
              {totalPages > 1 && (
                <div className="flex items-center justify-between px-6 py-3 border-t border-border">
                  <p className="text-xs text-muted-foreground">{filtered.length} fatura</p>
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

      {/* Dialogs */}
      {showCreate && (
        <CreateInvoiceDialog
          open={showCreate}
          onClose={() => setShowCreate(false)}
        />
      )}

      {editInvoice && (
        <EditInvoiceDialog
          invoice={editInvoice}
          onClose={() => setEditInvoice(null)}
        />
      )}

      <TransferToParasutDialog
        invoice={transferInvoice}
        open={!!transferInvoice}
        onClose={() => setTransferInvoice(null)}
      />

      {/* Delete confirm dialog */}
      <Dialog open={!!deleteInvoice} onOpenChange={() => setDeleteInvoice(null)}>
        <DialogContent className="max-w-sm">
          <DialogHeader>
            <DialogTitle>Faturayı Sil</DialogTitle>
          </DialogHeader>
          <p className="text-sm text-muted-foreground">
            <span className="font-medium text-foreground">&ldquo;{deleteInvoice?.title}&rdquo;</span>{' '}
            taslak faturasını silmek istediğinize emin misiniz? Bu işlem geri alınamaz.
          </p>
          <DialogFooter>
            <Button variant="outline" onClick={() => setDeleteInvoice(null)}>İptal</Button>
            <Button
              variant="destructive"
              onClick={handleDelete}
              disabled={deleteInvoiceMutation.isPending}
              className="gap-2"
            >
              {deleteInvoiceMutation.isPending && <Loader2 className="h-4 w-4 animate-spin" />}
              <Trash2 className="h-4 w-4" />
              Sil
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

// ── Invoice Row ──────────────────────────────────────────────────────────────

interface InvoiceRowProps {
  invoice: Invoice;
  isParasutConnected: boolean;
  selected: boolean;
  onSelect: (id: string, checked: boolean) => void;
  onEdit: () => void;
  onTransfer: () => void;
  onDelete: () => void;
}

function InvoiceRow({ invoice, isParasutConnected, selected, onSelect, onEdit, onTransfer, onDelete }: InvoiceRowProps) {
  const isOverdue = invoice.status === 'Draft' && new Date(invoice.dueDate) < new Date();
  const seriesLabel = invoice.invoiceSeries
    ? `${invoice.invoiceSeries}${invoice.invoiceNumber ? `-${invoice.invoiceNumber}` : ''}`
    : null;

  return (
    <div className={`grid grid-cols-[2rem_1.5fr_1fr_0.8fr_0.8fr_0.8fr_1fr_0.8fr_7rem] gap-4 px-4 py-3.5 items-center hover:bg-muted/20 transition-colors text-sm ${selected ? 'bg-primary/5' : ''}`}>
      {/* Checkbox — only for Draft */}
      <div className="flex items-center justify-center">
        {invoice.status === 'Draft' ? (
          <input
            type="checkbox"
            checked={selected}
            onChange={(e) => onSelect(invoice.id, e.target.checked)}
            className="h-4 w-4 rounded border-border accent-primary cursor-pointer"
          />
        ) : <span />}
      </div>

      {/* Title */}
      <div className="min-w-0">
        <div className="font-medium text-foreground truncate">{invoice.title}</div>
        {seriesLabel && (
          <div className="text-xs text-muted-foreground font-mono">{seriesLabel}</div>
        )}
      </div>

      {/* Customer */}
      <span className="text-muted-foreground truncate">{invoice.customerName}</span>

      {/* Project */}
      <span className="text-muted-foreground truncate text-xs">{invoice.projectName}</span>

      {/* Issue Date */}
      <span className="text-muted-foreground">{formatDate(invoice.issueDate)}</span>

      {/* Due Date */}
      <span className={isOverdue ? 'text-red-400 font-medium' : 'text-muted-foreground'}>
        {formatDate(invoice.dueDate)}
      </span>

      {/* Amount */}
      <span className="text-right font-medium font-mono text-foreground">
        {formatCurrency(invoice.grossTotal, invoice.currency)}
      </span>

      {/* Status */}
      <div className="flex items-center gap-1.5">
        <InvoiceStatusBadge status={invoice.status} />
        {invoice.parasutId && (
          <span title={`Paraşüt #${invoice.parasutId}`}>
            <ExternalLink className="h-3 w-3 text-blue-400" />
          </span>
        )}
      </div>

      {/* Actions */}
      <div className="flex justify-end gap-1">
        {invoice.status === 'Draft' && (
          <Button
            variant="ghost"
            size="sm"
            className="h-7 w-7 p-0 text-muted-foreground hover:text-foreground"
            onClick={onEdit}
            title="Düzenle"
          >
            <Pencil className="h-3.5 w-3.5" />
          </Button>
        )}
        {invoice.status === 'Draft' && (
          <Button
            variant="ghost"
            size="sm"
            className="h-7 w-7 p-0 text-muted-foreground hover:text-destructive"
            onClick={onDelete}
            title="Sil"
          >
            <Trash2 className="h-3.5 w-3.5" />
          </Button>
        )}
        {invoice.status === 'Draft' && isParasutConnected && (
          <Button
            variant="outline"
            size="sm"
            className="h-7 gap-1 text-xs"
            onClick={onTransfer}
          >
            <Send className="h-3 w-3" />
            Aktar
          </Button>
        )}
      </div>
    </div>
  );
}
