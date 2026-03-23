import { useEffect } from 'react';
import { useForm, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Loader2, Building2 } from 'lucide-react';
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
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCreateCustomer, useUpdateCustomer } from '@/api/customers';
import { useToast } from '@/hooks/use-toast';
import { cn } from '@/lib/utils';
import type { Customer, CustomerStatus, CustomerSegment } from '@/types';

// ── Schema ──────────────────────────────────────────────────────────────────

const schema = z.object({
  companyName: z.string().min(1, 'Şirket adı gereklidir'),
  contactName: z.string(),
  email: z.string(),
  phone: z.string(),
  address: z.string(),
  taxNumber: z.string(),
  taxUnit: z.string(),
  status: z.enum(['Lead', 'Active', 'Inactive', 'Churned'] as const),
  segment: z.string(),
  code: z.string(),
});

type FormData = z.infer<typeof schema>;

// ── Props ────────────────────────────────────────────────────────────────────

interface CustomerFormDialogProps {
  isOpen: boolean;
  onClose: () => void;
  customer?: Customer;
  onSuccess?: (customer: Customer) => void;
}

// ── Component ────────────────────────────────────────────────────────────────

export function CustomerFormDialog({
  isOpen,
  onClose,
  customer,
  onSuccess,
}: CustomerFormDialogProps) {
  const { toast } = useToast();
  const createMutation = useCreateCustomer();
  const updateMutation = useUpdateCustomer();
  const isEdit = !!customer;
  const isPending = createMutation.isPending || updateMutation.isPending;

  const {
    register,
    handleSubmit,
    control,
    reset,
    formState: { errors },
  } = useForm<FormData>({
    resolver: zodResolver(schema),
    defaultValues: getDefaultValues(customer),
  });

  // Sync form values when dialog opens or customer changes
  useEffect(() => {
    if (isOpen) {
      reset(getDefaultValues(customer));
    }
  }, [isOpen, customer, reset]);

  const onSubmit = async (data: FormData) => {
    const payload = {
      companyName: data.companyName,
      contactName: data.contactName || undefined,
      email: data.email || undefined,
      phone: data.phone || undefined,
      address: data.address || undefined,
      taxNumber: data.taxNumber || undefined,
      taxUnit: data.taxUnit || undefined,
      status: data.status as CustomerStatus,
      segment: (data.segment || undefined) as CustomerSegment | undefined,
      code: data.code || undefined,
    };

    try {
      let result: Customer;
      if (isEdit && customer) {
        result = await updateMutation.mutateAsync({ ...payload, id: customer.id });
      } else {
        result = await createMutation.mutateAsync(payload);
      }
      toast({
        title: isEdit ? 'Müşteri güncellendi' : 'Müşteri oluşturuldu',
        description: `${data.companyName} başarıyla kaydedildi.`,
      });
      onSuccess?.(result);
      onClose();
    } catch {
      toast({
        title: 'Hata oluştu',
        description: 'Kayıt sırasında bir hata oluştu. Lütfen tekrar deneyiniz.',
        variant: 'destructive',
      });
    }
  };

  return (
    <Dialog open={isOpen} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-2xl max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <div className="flex items-center gap-3">
            <div className="w-9 h-9 rounded-lg bg-primary/10 flex items-center justify-center flex-shrink-0">
              <Building2 className="h-5 w-5 text-primary" />
            </div>
            <DialogTitle className="text-lg">
              {isEdit ? 'Müşteriyi Düzenle' : 'Yeni Müşteri Ekle'}
            </DialogTitle>
          </div>
        </DialogHeader>

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5 py-2">
          {/* ── Temel Bilgiler ── */}
          <div className="space-y-3">
            <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
              Temel Bilgiler
            </p>
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div className="sm:col-span-2 space-y-1.5">
                <Label htmlFor="companyName">
                  Şirket / Müşteri Adı <span className="text-destructive">*</span>
                </Label>
                <Input
                  id="companyName"
                  placeholder="Şirket veya müşteri adını giriniz"
                  {...register('companyName')}
                  className={cn('h-11', errors.companyName && 'border-destructive')}
                />
                {errors.companyName && (
                  <p className="text-xs text-destructive">{errors.companyName.message}</p>
                )}
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="code">Müşteri Kodu</Label>
                <Input
                  id="code"
                  placeholder="MUS-001"
                  {...register('code')}
                  className="h-11"
                />
              </div>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label>Durum <span className="text-destructive">*</span></Label>
                <Controller
                  control={control}
                  name="status"
                  render={({ field }) => (
                    <Select value={field.value} onValueChange={field.onChange}>
                      <SelectTrigger className="h-11">
                        <SelectValue placeholder="Durum seçin" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="Lead">🔵 Lead</SelectItem>
                        <SelectItem value="Active">🟢 Aktif</SelectItem>
                        <SelectItem value="Inactive">🟡 Pasif</SelectItem>
                        <SelectItem value="Churned">🔴 Kayıp</SelectItem>
                      </SelectContent>
                    </Select>
                  )}
                />
              </div>
              <div className="space-y-1.5">
                <Label>Segment</Label>
                <Controller
                  control={control}
                  name="segment"
                  render={({ field }) => (
                    <Select value={field.value} onValueChange={field.onChange}>
                      <SelectTrigger className="h-11">
                        <SelectValue placeholder="Segment seçin" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="">Belirtilmedi</SelectItem>
                        <SelectItem value="SME">KOBİ</SelectItem>
                        <SelectItem value="Enterprise">Kurumsal</SelectItem>
                        <SelectItem value="Startup">Startup</SelectItem>
                        <SelectItem value="Government">Kamu</SelectItem>
                        <SelectItem value="Individual">Bireysel</SelectItem>
                      </SelectContent>
                    </Select>
                  )}
                />
              </div>
            </div>
          </div>

          <Separator />

          {/* ── İletişim Bilgileri ── */}
          <div className="space-y-3">
            <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
              İletişim Bilgileri
            </p>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label htmlFor="contactName">İletişim Kişisi</Label>
                <Input
                  id="contactName"
                  placeholder="Ad Soyad"
                  {...register('contactName')}
                  className="h-11"
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="phone">Telefon</Label>
                <Input
                  id="phone"
                  type="tel"
                  placeholder="+90 5xx xxx xx xx"
                  {...register('phone')}
                  className="h-11"
                />
              </div>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="email">E-posta Adresi</Label>
              <Input
                id="email"
                type="email"
                placeholder="ornek@sirket.com"
                {...register('email')}
                className="h-11"
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="address">Adres</Label>
              <Input
                id="address"
                placeholder="Adres bilgisi"
                {...register('address')}
                className="h-11"
              />
            </div>
          </div>

          <Separator />

          {/* ── Vergi Bilgileri ── */}
          <div className="space-y-3">
            <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
              Vergi Bilgileri
            </p>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label htmlFor="taxNumber">Vergi Numarası</Label>
                <Input
                  id="taxNumber"
                  placeholder="1234567890"
                  {...register('taxNumber')}
                  className="h-11"
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="taxUnit">Vergi Dairesi</Label>
                <Input
                  id="taxUnit"
                  placeholder="Kadıköy V.D."
                  {...register('taxUnit')}
                  className="h-11"
                />
              </div>
            </div>
          </div>

          <DialogFooter className="gap-2 pt-2">
            <Button
              type="button"
              variant="outline"
              onClick={onClose}
              disabled={isPending}
              className="h-11"
            >
              İptal
            </Button>
            <Button type="submit" disabled={isPending} className="h-11 min-w-[120px]">
              {isPending && <Loader2 className="h-4 w-4 animate-spin mr-2" />}
              {isEdit ? 'Güncelle' : 'Müşteriyi Kaydet'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ── Helpers ──────────────────────────────────────────────────────────────────

function getDefaultValues(customer?: Customer): FormData {
  if (customer) {
    return {
      companyName: customer.companyName,
      contactName: customer.contactName ?? '',
      email: customer.email ?? '',
      phone: customer.phone ?? '',
      address: customer.address ?? '',
      taxNumber: customer.taxNumber ?? '',
      taxUnit: customer.taxUnit ?? '',
      status: customer.status,
      segment: customer.segment ?? '',
      code: customer.code ?? '',
    };
  }
  return {
    companyName: '',
    contactName: '',
    email: '',
    phone: '',
    address: '',
    taxNumber: '',
    taxUnit: '',
    status: 'Lead',
    segment: '',
    code: '',
  };
}
