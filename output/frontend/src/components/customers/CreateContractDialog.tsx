import { useEffect } from 'react';
import { useForm, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Banknote, CreditCard, FileText, Loader2 } from 'lucide-react';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Separator } from '@/components/ui/separator';
import {
  useCreateCustomerContract,
  type ContractPaymentType,
  type CreateContractRequest,
  type CustomerContract,
} from '@/api/customers';
import { useToast } from '@/hooks/use-toast';
import { cn } from '@/lib/utils';
import type { Customer } from '@/types';

// ── Helpers ──────────────────────────────────────────────────────────────────

/** Returns "yyyy-MM-dd" using local date components (no UTC drift). */
function localDateStr(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

// ── Schema ───────────────────────────────────────────────────────────────────

const schema = z.object({
  monthlyAmount: z
    .string()
    .min(1, 'Aylık tutar gereklidir')
    .refine((v) => Number(v) > 0, 'Aylık tutar 0\'dan büyük olmalıdır'),
  paymentType: z.enum(['CreditCard', 'EftWire']),
  startDate: z.string().min(1, 'Başlangıç tarihi gereklidir'),
  durationMonths: z
    .string()
    .optional()
    .refine(
      (v) => !v || (Number.isInteger(Number(v)) && Number(v) >= 1 && Number(v) <= 120),
      'Süre 1 ile 120 ay arasında olmalıdır',
    ),
});

type FormData = z.infer<typeof schema>;

// ── Props ─────────────────────────────────────────────────────────────────────

interface CreateContractDialogProps {
  isOpen: boolean;
  onClose: () => void;
  customer: Customer;
  /** Existing active contract (when present, dialog shows "Yenile" semantics). */
  activeContract?: CustomerContract | null;
}

// ── Component ─────────────────────────────────────────────────────────────────

export function CreateContractDialog({
  isOpen,
  onClose,
  customer,
  activeContract,
}: CreateContractDialogProps) {
  const { toast } = useToast();
  const createMutation = useCreateCustomerContract(customer.id);
  const isRenewal = !!activeContract;

  const {
    register,
    handleSubmit,
    control,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<FormData>({
    resolver: zodResolver(schema),
    defaultValues: getDefaults(customer, activeContract ?? null),
  });

  useEffect(() => {
    if (isOpen) reset(getDefaults(customer, activeContract ?? null));
  }, [isOpen, customer, activeContract, reset]);

  const onSubmit = async (data: FormData) => {
    try {
      const payload: CreateContractRequest = {
        monthlyAmount: Number(data.monthlyAmount),
        paymentType: data.paymentType === 'CreditCard' ? 0 : 1,
        startDate: data.startDate,
        durationMonths: data.durationMonths ? Number(data.durationMonths) : null,
      };
      await createMutation.mutateAsync(payload);
      toast({
        title: isRenewal ? 'Sözleşme yenilendi' : 'Sözleşme oluşturuldu',
        description: `${customer.companyName} için ${
          payload.paymentType === 0 ? 'Kredi Kartı' : 'EFT/Havale'
        } sözleşmesi ${isRenewal ? 'yenilendi' : 'oluşturuldu'}.`,
      });
      onClose();
    } catch (err) {
      const message =
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ??
        'Sözleşme oluşturulamadı.';
      toast({
        title: 'Hata',
        description: message,
        variant: 'destructive',
      });
    }
  };

  return (
    <Dialog open={isOpen} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <div className="flex items-center gap-3">
            <div className="w-9 h-9 rounded-lg bg-purple-500/10 flex items-center justify-center flex-shrink-0">
              <FileText className="h-5 w-5 text-purple-500" />
            </div>
            <DialogTitle className="text-lg">
              {isRenewal ? 'Sözleşme Yenile' : 'Sözleşme Oluştur'}
            </DialogTitle>
          </div>
        </DialogHeader>

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5 py-2">
          {isRenewal && (
            <div className="rounded-md border border-amber-500/30 bg-amber-500/5 p-3 text-xs text-amber-300">
              Aktif sözleşme yenilendiğinde mevcut kayıt "Tamamlandı" olarak işaretlenecek ve
              yeni bir sözleşme kaydı oluşturulacaktır.
            </div>
          )}

          {/* Customer summary */}
          <div className="space-y-1.5">
            <Label className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
              Müşteri
            </Label>
            <p className="text-sm font-medium">{customer.companyName} Abonelik</p>
          </div>

          <Separator />

          {/* Monthly amount */}
          <div className="space-y-1.5">
            <Label htmlFor="monthlyAmount">
              Aylık Tutar (TL) <span className="text-destructive">*</span>
            </Label>
            <Input
              id="monthlyAmount"
              type="number"
              step="0.01"
              min="0"
              {...register('monthlyAmount')}
              className={cn('h-11', errors.monthlyAmount && 'border-destructive')}
            />
            {errors.monthlyAmount && (
              <p className="text-xs text-destructive">{errors.monthlyAmount.message}</p>
            )}
          </div>

          {/* Payment type — toggle buttons */}
          <div className="space-y-1.5">
            <Label>
              Ödeme Türü <span className="text-destructive">*</span>
            </Label>
            <Controller
              control={control}
              name="paymentType"
              render={({ field }) => (
                <div className="grid grid-cols-2 gap-2">
                  <button
                    type="button"
                    onClick={() => field.onChange('CreditCard')}
                    className={cn(
                      'flex h-11 items-center justify-center gap-2 rounded-md border text-sm font-medium transition-colors',
                      field.value === 'CreditCard'
                        ? 'border-purple-500 bg-purple-500/10 text-purple-300'
                        : 'border-input bg-background text-muted-foreground hover:bg-accent',
                    )}
                  >
                    <CreditCard className="h-4 w-4" />
                    Kredi Kartı
                  </button>
                  <button
                    type="button"
                    onClick={() => field.onChange('EftWire')}
                    className={cn(
                      'flex h-11 items-center justify-center gap-2 rounded-md border text-sm font-medium transition-colors',
                      field.value === 'EftWire'
                        ? 'border-purple-500 bg-purple-500/10 text-purple-300'
                        : 'border-input bg-background text-muted-foreground hover:bg-accent',
                    )}
                  >
                    <Banknote className="h-4 w-4" />
                    EFT / Havale
                  </button>
                </div>
              )}
            />
          </div>

          {/* Start date + duration */}
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div className="space-y-1.5">
              <Label htmlFor="startDate">
                Başlangıç Tarihi <span className="text-destructive">*</span>
              </Label>
              <Input
                id="startDate"
                type="date"
                {...register('startDate')}
                className={cn('h-11', errors.startDate && 'border-destructive')}
              />
              {errors.startDate && (
                <p className="text-xs text-destructive">{errors.startDate.message}</p>
              )}
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="durationMonths">Süre (ay)</Label>
              <Input
                id="durationMonths"
                type="number"
                min="1"
                max="120"
                placeholder="Süresiz için boş bırakın"
                {...register('durationMonths')}
                className={cn('h-11', errors.durationMonths && 'border-destructive')}
              />
              {errors.durationMonths && (
                <p className="text-xs text-destructive">{errors.durationMonths.message}</p>
              )}
            </div>
          </div>

          <p className="text-xs text-muted-foreground">
            EFT/Havale seçildiğinde sözleşme başlangıç gününün her ay tekrarında otomatik
            olarak taslak fatura oluşturulur ("RezervAl Aylık Lisans Bedeli" ürünü ile).
          </p>

          <DialogFooter className="gap-2 sm:gap-2">
            <Button type="button" variant="outline" onClick={onClose} disabled={isSubmitting}>
              İptal
            </Button>
            <Button type="submit" disabled={isSubmitting} className="gap-2">
              {isSubmitting && <Loader2 className="h-4 w-4 animate-spin" />}
              {isRenewal ? 'Yenile' : 'Oluştur'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ── Form defaults ─────────────────────────────────────────────────────────────

function getDefaults(
  customer: Customer,
  activeContract: CustomerContract | null,
): FormData {
  // Prefer the active contract's existing values when renewing, otherwise fall back
  // to the customer's stored MonthlyLicenseFee.
  const monthlyAmount =
    activeContract?.monthlyAmount ?? customer.monthlyLicenseFee ?? 0;

  const paymentType: ContractPaymentType =
    activeContract?.paymentType ?? 1; // default EftWire (most common)

  return {
    monthlyAmount: monthlyAmount > 0 ? monthlyAmount.toString() : '',
    paymentType: paymentType === 0 ? 'CreditCard' : 'EftWire',
    startDate: localDateStr(new Date()),
    durationMonths: activeContract?.durationMonths
      ? activeContract.durationMonths.toString()
      : '',
  };
}
