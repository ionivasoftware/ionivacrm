import { useState, useEffect, useCallback } from 'react';
import { Search, ArrowRight, AlertTriangle, Loader2, CheckCircle2 } from 'lucide-react';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { useCustomers, useTransferLeadCustomer } from '@/api/customers';
import { useToast } from '@/hooks/use-toast';
import type { Customer } from '@/types';

// ── Step type ─────────────────────────────────────────────────────────────────

type Step = 'select' | 'confirm';

// ── Props ─────────────────────────────────────────────────────────────────────

interface TransferLeadModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  lead: Customer;
  onSuccess: () => void;
}

// ── Component ─────────────────────────────────────────────────────────────────

export function TransferLeadModal({ open, onOpenChange, lead, onSuccess }: TransferLeadModalProps) {
  const { toast } = useToast();
  const [step, setStep] = useState<Step>('select');
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [selected, setSelected] = useState<Customer | null>(null);

  const transfer = useTransferLeadCustomer();

  // Debounce search
  useEffect(() => {
    const t = setTimeout(() => setDebouncedSearch(search), 400);
    return () => clearTimeout(t);
  }, [search]);

  // Reset state when dialog opens/closes
  useEffect(() => {
    if (!open) {
      setStep('select');
      setSearch('');
      setDebouncedSearch('');
      setSelected(null);
    }
  }, [open]);

  // Fetch active customers (Status !== 'Lead')
  // We fetch with no status filter and filter client-side for non-Lead ones.
  // Using a large pageSize so all appear in search results.
  const { data: customersData, isLoading } = useCustomers({
    search: debouncedSearch || undefined,
    pageSize: 50,
    page: 1,
  });

  // Exclude Lead customers and the lead itself
  const activeCustomers = (customersData?.items ?? []).filter(
    (c) => c.status !== 'Lead' && c.id !== lead.id
  );

  const handleSelect = useCallback((customer: Customer) => {
    setSelected(customer);
    setStep('confirm');
  }, []);

  const handleBack = () => {
    setStep('select');
    setSelected(null);
  };

  const handleConfirm = async () => {
    if (!selected) return;
    try {
      await transfer.mutateAsync({ leadId: lead.id, targetCustomerId: selected.id });
      toast({
        title: 'Aktarım başarılı',
        description: `"${lead.companyName}" kaydındaki veriler "${selected.companyName}" müşterisine aktarıldı ve lead silindi.`,
      });
      onOpenChange(false);
      onSuccess();
    } catch (err: unknown) {
      const message =
        err instanceof Error ? err.message : 'Aktarım sırasında bir hata oluştu.';
      toast({
        title: 'Aktarım başarısız',
        description: message,
        variant: 'destructive',
      });
    }
  };

  const handleClose = () => {
    if (transfer.isPending) return;
    onOpenChange(false);
  };

  // ── Status badge helper ────────────────────────────────────────────────────

  const STATUS_LABELS: Record<string, string> = {
    Active: 'Aktif',
    Demo: 'Demo',
    Churned: 'Ayrıldı',
    Passive: 'Pasif',
    Lead: 'Lead',
  };

  const STATUS_CLASSES: Record<string, string> = {
    Active: 'bg-green-500/15 text-green-400 border-green-500/30',
    Demo: 'bg-violet-500/15 text-violet-400 border-violet-500/30',
    Churned: 'bg-red-500/15 text-red-400 border-red-500/30',
    Passive: 'bg-slate-500/15 text-slate-400 border-slate-500/30',
    Lead: 'bg-amber-500/15 text-amber-400 border-amber-500/30',
  };

  // ── Render ─────────────────────────────────────────────────────────────────

  return (
    <Dialog open={open} onOpenChange={handleClose}>
      <DialogContent className="sm:max-w-2xl max-h-[85vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <ArrowRight className="h-5 w-5 text-primary" />
            Aktif Müşteriye Aktar
          </DialogTitle>
        </DialogHeader>

        {/* ── Warning banner ── */}
        <div className="flex items-start gap-3 rounded-lg border border-amber-500/30 bg-amber-500/10 p-3 text-sm">
          <AlertTriangle className="h-4 w-4 text-amber-400 mt-0.5 flex-shrink-0" />
          <div className="text-amber-200">
            <span className="font-semibold">{lead.companyName}</span> lead kaydındaki tüm görüşmeler, görevler ve
            fırsatlar seçilen müşteriye taşınacak, ardından lead kaydı silinecektir.
            Bu işlem <span className="font-semibold">geri alınamaz.</span>
          </div>
        </div>

        {/* ── Step: Select ── */}
        {step === 'select' && (
          <div className="space-y-3">
            {/* Search */}
            <div className="relative">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <Input
                placeholder="Müşteri ara..."
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                className="pl-9"
                autoFocus
              />
            </div>

            {/* List */}
            <div className="max-h-72 overflow-y-auto space-y-1 pr-1">
              {isLoading && (
                <div className="flex items-center justify-center py-8">
                  <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
                </div>
              )}

              {!isLoading && activeCustomers.length === 0 && (
                <div className="text-center py-8 text-sm text-muted-foreground">
                  {debouncedSearch ? 'Eşleşen müşteri bulunamadı.' : 'Aktif müşteri bulunamadı.'}
                </div>
              )}

              {!isLoading &&
                activeCustomers.map((customer) => (
                  <button
                    key={customer.id}
                    type="button"
                    onClick={() => handleSelect(customer)}
                    className="w-full flex items-center justify-between gap-3 px-3 py-2.5 rounded-lg border border-transparent hover:border-border hover:bg-muted/50 transition-colors text-left group"
                  >
                    <div className="flex items-center gap-3 min-w-0">
                      <div className="w-8 h-8 rounded-lg bg-primary/10 flex items-center justify-center flex-shrink-0">
                        <span className="text-primary text-xs font-bold">
                          {customer.companyName.slice(0, 2).toUpperCase()}
                        </span>
                      </div>
                      <div className="min-w-0">
                        <p className="text-sm font-medium text-foreground truncate">
                          {customer.companyName}
                        </p>
                        {customer.contactName && (
                          <p className="text-xs text-muted-foreground truncate">
                            {customer.contactName}
                          </p>
                        )}
                      </div>
                    </div>
                    <Badge
                      variant="outline"
                      className={`text-xs flex-shrink-0 ${STATUS_CLASSES[customer.status] ?? ''}`}
                    >
                      {STATUS_LABELS[customer.status] ?? customer.status}
                    </Badge>
                  </button>
                ))}
            </div>
          </div>
        )}

        {/* ── Step: Confirm ── */}
        {step === 'confirm' && selected && (
          <div className="space-y-4">
            <div className="flex items-center gap-3 rounded-lg border bg-muted/30 p-4">
              {/* From */}
              <div className="flex-1 min-w-0 text-center">
                <div className="w-10 h-10 rounded-xl bg-amber-500/10 flex items-center justify-center mx-auto mb-2">
                  <span className="text-amber-400 text-sm font-bold">
                    {lead.companyName.slice(0, 2).toUpperCase()}
                  </span>
                </div>
                <p className="text-sm font-medium text-foreground truncate">{lead.companyName}</p>
                <p className="text-xs text-amber-400 mt-0.5">Lead (silinecek)</p>
              </div>

              {/* Arrow */}
              <ArrowRight className="h-5 w-5 text-muted-foreground flex-shrink-0" />

              {/* To */}
              <div className="flex-1 min-w-0 text-center">
                <div className="w-10 h-10 rounded-xl bg-green-500/10 flex items-center justify-center mx-auto mb-2">
                  <span className="text-green-400 text-sm font-bold">
                    {selected.companyName.slice(0, 2).toUpperCase()}
                  </span>
                </div>
                <p className="text-sm font-medium text-foreground truncate">{selected.companyName}</p>
                <p className="text-xs text-green-400 mt-0.5">
                  {STATUS_LABELS[selected.status] ?? selected.status}
                </p>
              </div>
            </div>

            <p className="text-sm text-muted-foreground text-center">
              Tüm veriler aktarılacak ve lead kaydı silinecek. Onaylıyor musunuz?
            </p>
          </div>
        )}

        <DialogFooter className="gap-2">
          {step === 'select' && (
            <Button variant="outline" onClick={handleClose}>
              İptal
            </Button>
          )}

          {step === 'confirm' && (
            <>
              <Button variant="outline" onClick={handleBack} disabled={transfer.isPending}>
                Geri
              </Button>
              <Button
                variant="destructive"
                onClick={handleConfirm}
                disabled={transfer.isPending}
                className="gap-2"
              >
                {transfer.isPending ? (
                  <Loader2 className="h-4 w-4 animate-spin" />
                ) : (
                  <CheckCircle2 className="h-4 w-4" />
                )}
                Evet, Aktar
              </Button>
            </>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
