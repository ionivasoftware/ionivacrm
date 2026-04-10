import { AlertTriangle, Loader2, XCircle } from 'lucide-react';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { useCancelCustomerContract, type CustomerContract } from '@/api/customers';
import { useToast } from '@/hooks/use-toast';
import type { Customer } from '@/types';

// ── Helpers ──────────────────────────────────────────────────────────────────

function formatCurrency(value: number): string {
  return new Intl.NumberFormat('tr-TR', {
    style: 'currency',
    currency: 'TRY',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value);
}

function formatDate(iso: string): string {
  return new Intl.DateTimeFormat('tr-TR', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  }).format(new Date(iso));
}

// ── Props ────────────────────────────────────────────────────────────────────

interface CancelContractDialogProps {
  isOpen: boolean;
  onClose: () => void;
  customer: Customer;
  contract: CustomerContract;
}

// ── Component ────────────────────────────────────────────────────────────────

export function CancelContractDialog({
  isOpen,
  onClose,
  customer,
  contract,
}: CancelContractDialogProps) {
  const { toast } = useToast();
  const cancelMutation = useCancelCustomerContract(customer.id);

  const handleConfirm = async () => {
    try {
      const result = await cancelMutation.mutateAsync();
      const warnings = result?.iyzicoWarnings ?? [];

      if (warnings.length > 0) {
        toast({
          title: 'Sözleşme iptal edildi (uyarılarla)',
          description: `${warnings.length} iyzico uyarısı: ${warnings.join('; ')}`,
        });
      } else {
        toast({
          title: 'Sözleşme iptal edildi',
          description: `${customer.companyName} aboneliği iptal edildi.`,
        });
      }
      onClose();
    } catch (err) {
      const message =
        (err as { response?: { data?: { errors?: string[] } } })?.response?.data?.errors?.[0] ??
        'Sözleşme iptal edilemedi.';
      toast({
        title: 'Hata',
        description: message,
        variant: 'destructive',
      });
    }
  };

  return (
    <Dialog open={isOpen} onOpenChange={(open) => !open && !cancelMutation.isPending && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <div className="flex items-center gap-3">
            <div className="w-9 h-9 rounded-lg bg-red-500/10 flex items-center justify-center flex-shrink-0">
              <XCircle className="h-5 w-5 text-red-500" />
            </div>
            <DialogTitle className="text-lg">Sözleşmeyi İptal Et</DialogTitle>
          </div>
        </DialogHeader>

        <div className="space-y-4 py-2">
          {/* Warning */}
          <div className="flex items-start gap-3 rounded-lg border border-red-500/30 bg-red-500/10 p-3 text-sm">
            <AlertTriangle className="h-4 w-4 text-red-400 mt-0.5 flex-shrink-0" />
            <div className="text-red-200">
              Bu işlem Rezerval'daki iyzico aboneliğini kalıcı olarak silecek ve firmanın
              erişimi <span className="font-semibold">anında</span> sonlandırılacaktır.{' '}
              <span className="font-semibold">Geri alınamaz.</span>
            </div>
          </div>

          {/* Contract summary */}
          <div className="rounded-lg border border-border bg-muted/30 p-4 space-y-2 text-sm">
            <div className="flex justify-between">
              <span className="text-muted-foreground">Müşteri</span>
              <span className="font-medium truncate ml-2">{customer.companyName}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">Aylık tutar (KDV dahil)</span>
              <span className="font-mono font-medium">{formatCurrency(contract.monthlyAmount)}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">Başlangıç</span>
              <span>{formatDate(contract.startDate)}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-muted-foreground">Ödeme türü</span>
              <span>{contract.paymentType === 'CreditCard' ? 'Kredi Kartı' : 'EFT / Havale'}</span>
            </div>
          </div>
        </div>

        <DialogFooter className="gap-2">
          <Button
            type="button"
            variant="outline"
            onClick={onClose}
            disabled={cancelMutation.isPending}
          >
            Vazgeç
          </Button>
          <Button
            type="button"
            variant="destructive"
            onClick={handleConfirm}
            disabled={cancelMutation.isPending}
            className="gap-2"
          >
            {cancelMutation.isPending ? (
              <Loader2 className="h-4 w-4 animate-spin" />
            ) : (
              <XCircle className="h-4 w-4" />
            )}
            Evet, İptal Et
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
