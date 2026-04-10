import { useEffect, useState } from 'react';
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCreateCustomer } from '@/api/customers';
import { useToast } from '@/hooks/use-toast';
import { useQueryClient } from '@tanstack/react-query';
import { cn } from '@/lib/utils';
import type { Customer } from '@/types';

// ── Schema ───────────────────────────────────────────────────────────────────
// Logo, bitiş tarihi ve yönetici alanları Rezerval Güncelle modalında.
// Bu modal sadece CRM + temel Rezerval kaydı oluşturur.

const schema = z.object({
  name: z.string().min(1, 'Firma adı gereklidir'),
  title: z.string(),
  phone: z.string().min(1, 'Telefon numarası gereklidir'),
  email: z.string().min(1, 'E-posta gereklidir'),
  taxNumber: z.string(),
  taxUnit: z.string(),
  tcNo: z.string(),
  address: z.string(),
  isPersonCompany: z.enum(['true', 'false']),
  contactName: z.string(),
});

type FormData = z.infer<typeof schema>;

// ── Props ─────────────────────────────────────────────────────────────────────

interface RezervalCustomerFormDialogProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess?: (customer: Customer) => void;
}

// ── Component ─────────────────────────────────────────────────────────────────

export function RezervalCustomerFormDialog({
  isOpen,
  onClose,
  onSuccess,
}: RezervalCustomerFormDialogProps) {
  const { toast } = useToast();
  const queryClient = useQueryClient();
  const createMutation = useCreateCustomer();
  const [isSubmitting, setIsSubmitting] = useState(false);

  const {
    register,
    handleSubmit,
    control,
    reset,
    formState: { errors },
  } = useForm<FormData>({
    resolver: zodResolver(schema),
    defaultValues: getDefaultValues(),
  });

  useEffect(() => {
    if (isOpen) reset(getDefaultValues());
  }, [isOpen, reset]);

  // ── Submit ────────────────────────────────────────────────────────────────

  const onSubmit = async (data: FormData) => {
    setIsSubmitting(true);
    try {
      const newCustomer = await createMutation.mutateAsync({
        companyName: data.name,
        contactName: data.contactName || undefined,
        phone: data.phone || undefined,
        email: data.email || undefined,
        address: data.address || undefined,
        taxNumber: data.taxNumber || undefined,
        taxUnit: data.taxUnit || undefined,
        status: 'Active',
      });

      queryClient.invalidateQueries({ queryKey: ['customers'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });

      toast({
        title: 'Müşteri oluşturuldu',
        description: `${data.name} başarıyla kaydedildi. RezervAl'a göndermek için detay sayfasından "RezervAl Güncelle" butonunu kullanabilirsiniz.`,
      });

      onSuccess?.(newCustomer);
      onClose();
    } catch (err) {
      const errMsg =
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message;
      toast({
        title: 'Hata oluştu',
        description: errMsg ?? 'Müşteri oluşturulurken bir hata oluştu.',
        variant: 'destructive',
      });
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Dialog open={isOpen} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-2xl max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <div className="flex items-center gap-3">
            <div className="w-9 h-9 rounded-lg bg-teal-500/10 flex items-center justify-center flex-shrink-0">
              <Building2 className="h-5 w-5 text-teal-600" />
            </div>
            <DialogTitle className="text-lg">Yeni RezervAl Müşterisi Ekle</DialogTitle>
          </div>
        </DialogHeader>

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5 py-2">
          {/* ── Firma Bilgileri ── */}
          <div className="space-y-3">
            <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
              Firma Bilgileri
            </p>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label htmlFor="rz-name">
                  Firma Adı <span className="text-destructive">*</span>
                </Label>
                <Input
                  id="rz-name"
                  placeholder="Firma veya müşteri adı"
                  {...register('name')}
                  className={cn('h-11', errors.name && 'border-destructive')}
                />
                {errors.name && (
                  <p className="text-xs text-destructive">{errors.name.message}</p>
                )}
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="rz-title">Ünvan</Label>
                <Input
                  id="rz-title"
                  placeholder="Firma ünvanı"
                  {...register('title')}
                  className="h-11"
                />
              </div>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label htmlFor="rz-phone">
                  Telefon <span className="text-destructive">*</span>
                </Label>
                <Input
                  id="rz-phone"
                  type="tel"
                  placeholder="+90 5xx xxx xx xx"
                  {...register('phone')}
                  className={cn('h-11', errors.phone && 'border-destructive')}
                />
                {errors.phone && (
                  <p className="text-xs text-destructive">{errors.phone.message}</p>
                )}
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="rz-email">
                  E-posta <span className="text-destructive">*</span>
                </Label>
                <Input
                  id="rz-email"
                  type="email"
                  placeholder="ornek@sirket.com"
                  {...register('email')}
                  className={cn('h-11', errors.email && 'border-destructive')}
                />
                {errors.email && (
                  <p className="text-xs text-destructive">{errors.email.message}</p>
                )}
              </div>
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="rz-address">Adres</Label>
              <Input
                id="rz-address"
                placeholder="Firma adresi"
                {...register('address')}
                className="h-11"
              />
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div className="space-y-1.5">
                <Label htmlFor="rz-taxNumber">Vergi No</Label>
                <Input
                  id="rz-taxNumber"
                  placeholder="1234567890"
                  {...register('taxNumber')}
                  className="h-11"
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="rz-taxUnit">Vergi Dairesi</Label>
                <Input
                  id="rz-taxUnit"
                  placeholder="Vergi dairesi adı"
                  {...register('taxUnit')}
                  className="h-11"
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="rz-tcNo">TC Kimlik No</Label>
                <Input
                  id="rz-tcNo"
                  placeholder="Şahıs firma ise"
                  {...register('tcNo')}
                  className="h-11"
                />
              </div>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label>Firma Tipi</Label>
                <Controller
                  control={control}
                  name="isPersonCompany"
                  render={({ field }) => (
                    <Select value={field.value} onValueChange={field.onChange}>
                      <SelectTrigger className="h-11">
                        <SelectValue placeholder="Seçin" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="false">Tüzel Kişi</SelectItem>
                        <SelectItem value="true">Şahıs Firması</SelectItem>
                      </SelectContent>
                    </Select>
                  )}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="rz-contactName">İletişim Kişisi</Label>
                <Input
                  id="rz-contactName"
                  placeholder="Ad Soyad"
                  {...register('contactName')}
                  className="h-11"
                />
              </div>
            </div>
          </div>

          <p className="text-xs text-muted-foreground">
            Logo, bitiş tarihi ve yönetici bilgilerini müşteri detayında "RezervAl Güncelle" ile ekleyebilirsiniz.
          </p>

          <DialogFooter className="gap-2 pt-2">
            <Button
              type="button"
              variant="outline"
              onClick={onClose}
              disabled={isSubmitting}
              className="h-11"
            >
              İptal
            </Button>
            <Button
              type="submit"
              disabled={isSubmitting}
              className="h-11 min-w-[160px] bg-teal-600 hover:bg-teal-700"
            >
              {isSubmitting && <Loader2 className="h-4 w-4 animate-spin mr-2" />}
              Müşteriyi Kaydet
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function getDefaultValues(): FormData {
  return {
    name: '',
    title: '',
    phone: '',
    email: '',
    taxNumber: '',
    taxUnit: '',
    tcNo: '',
    address: '',
    isPersonCompany: 'false',
    contactName: '',
  };
}
