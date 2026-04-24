import { useEffect, useMemo } from 'react';
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
import { useAdminProjects } from '@/api/admin';
import { useAuthStore } from '@/stores/authStore';
import { getSegmentsForProject } from '@/config/projectSegments';
import { useToast } from '@/hooks/use-toast';
import { cn } from '@/lib/utils';
import type { Customer, CustomerLabel, CustomerStatus, UpdateCustomerRequest } from '@/types';

// ── Schema ──────────────────────────────────────────────────────────────────

const schema = z.object({
  companyName: z.string().min(1, 'Firma adı gereklidir'),
  title: z.string(),
  phone: z.string().min(1, 'Telefon numarası gereklidir'),
  email: z.string(),
  address: z.string(),
  taxNumber: z.string(),
  taxUnit: z.string(),
  tcNo: z.string(),
  isPersonCompany: z.enum(['true', 'false']),
  contactName: z.string(),
  // EMS-only fields — empty string when hidden, "none" sentinel when "Belirtilmedi"
  segment: z.string(),
  label: z.string(),
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

  // Show segment + label dropdowns whenever the active project has an EMS API key
  // configured — same rule for both add and edit. Rezerval-only projects keep them
  // hidden everywhere; EMS / mixed projects show them for every customer regardless
  // of whether the row was synced or created manually.
  const { currentProjectId, projectNames } = useAuthStore();
  const { data: adminProjects } = useAdminProjects();

  const showEmsFields = useMemo(() => {
    if (!currentProjectId) return false;
    const project = adminProjects?.find((p) => p.id === currentProjectId);
    return !!project?.emsApiKey;
  }, [currentProjectId, adminProjects]);

  // Project-name-driven segment options.  Falls back to an empty list — the
  // dropdown still renders but only shows "Belirtilmedi".
  const segments = useMemo(() => {
    const projectName = currentProjectId ? projectNames[currentProjectId] : undefined;
    return getSegmentsForProject(projectName);
  }, [currentProjectId, projectNames]);

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

  useEffect(() => {
    if (isOpen) reset(getDefaultValues(customer));
  }, [isOpen, customer, reset]);

  const onSubmit = async (data: FormData) => {
    // Convert the "none" sentinel back to undefined so the API stores NULL
    const segmentValue =
      showEmsFields && data.segment && data.segment !== 'none' ? data.segment : undefined;
    const labelValue =
      showEmsFields && data.label && data.label !== 'none'
        ? (data.label as CustomerLabel)
        : undefined;

    const payload = {
      companyName: data.companyName,
      contactName: data.contactName || undefined,
      email: data.email || undefined,
      phone: data.phone || undefined,
      address: data.address || undefined,
      taxNumber: data.taxNumber || undefined,
      taxUnit: data.taxUnit || undefined,
      segment: segmentValue,
      label: labelValue,
    };

    try {
      let result: Customer;
      if (isEdit && customer) {
        const updatePayload: UpdateCustomerRequest = {
          ...payload,
          id: customer.id,
          projectId: customer.projectId,
          status: customer.status as CustomerStatus,
        };
        result = await updateMutation.mutateAsync(updatePayload);
      } else {
        result = await createMutation.mutateAsync({
          ...payload,
          status: 'Lead',
        });
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
          {/* ── Firma Bilgileri ── */}
          <div className="space-y-3">
            <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
              Firma Bilgileri
            </p>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label htmlFor="companyName">
                  Firma Adı <span className="text-destructive">*</span>
                </Label>
                <Input
                  id="companyName"
                  placeholder="Firma veya müşteri adı"
                  {...register('companyName')}
                  className={cn('h-11', errors.companyName && 'border-destructive')}
                />
                {errors.companyName && (
                  <p className="text-xs text-destructive">{errors.companyName.message}</p>
                )}
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="title">Ünvan</Label>
                <Input
                  id="title"
                  placeholder="Firma ünvanı"
                  {...register('title')}
                  className="h-11"
                />
              </div>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label htmlFor="phone">
                  Telefon <span className="text-destructive">*</span>
                </Label>
                <Input
                  id="phone"
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
                <Label htmlFor="email">E-posta</Label>
                <Input
                  id="email"
                  type="email"
                  placeholder="ornek@sirket.com"
                  {...register('email')}
                  className="h-11"
                />
              </div>
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="address">Adres</Label>
              <Input
                id="address"
                placeholder="Firma adresi"
                {...register('address')}
                className="h-11"
              />
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div className="space-y-1.5">
                <Label htmlFor="taxNumber">Vergi No</Label>
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
              <div className="space-y-1.5">
                <Label htmlFor="tcNo">TC Kimlik No</Label>
                <Input
                  id="tcNo"
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
                <Label htmlFor="contactName">İletişim Kişisi</Label>
                <Input
                  id="contactName"
                  placeholder="Ad Soyad"
                  {...register('contactName')}
                  className="h-11"
                />
              </div>
            </div>
          </div>

          {/* ── EMS-only: Segment + Potansiyel Label ── */}
          {showEmsFields && (
            <>
              <Separator />
              <div className="space-y-3">
                <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
                  EMS Sınıflandırma
                </p>

                <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                  <div className="space-y-1.5">
                    <Label>Segment</Label>
                    <Controller
                      control={control}
                      name="segment"
                      render={({ field }) => (
                        <Select
                          value={field.value || 'none'}
                          onValueChange={field.onChange}
                        >
                          <SelectTrigger className="h-11">
                            <SelectValue placeholder="Segment seçin" />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectItem value="none">Belirtilmedi</SelectItem>
                            {segments.map((seg) => (
                              <SelectItem key={seg} value={seg}>
                                {seg}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      )}
                    />
                  </div>
                  <div className="space-y-1.5">
                    <Label>Potansiyel Label</Label>
                    <Controller
                      control={control}
                      name="label"
                      render={({ field }) => (
                        <Select
                          value={field.value || 'none'}
                          onValueChange={field.onChange}
                        >
                          <SelectTrigger className="h-11">
                            <SelectValue placeholder="Label seçin" />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectItem value="none">Belirtilmedi</SelectItem>
                            <SelectItem value="YuksekPotansiyel">⭐ Yüksek Potansiyel</SelectItem>
                            <SelectItem value="Potansiyel">🔵 Potansiyel</SelectItem>
                            <SelectItem value="Notr">⚪ Nötr</SelectItem>
                            <SelectItem value="Vasat">🟡 Vasat</SelectItem>
                            <SelectItem value="Kotu">🔴 Kötü</SelectItem>
                          </SelectContent>
                        </Select>
                      )}
                    />
                  </div>
                </div>
              </div>
            </>
          )}

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
      title: '',
      phone: customer.phone ?? '',
      email: customer.email ?? '',
      address: customer.address ?? '',
      taxNumber: customer.taxNumber ?? '',
      taxUnit: customer.taxUnit ?? '',
      tcNo: '',
      isPersonCompany: 'false',
      contactName: customer.contactName ?? '',
      segment: customer.segment ?? 'none',
      label: customer.label ?? 'none',
    };
  }
  return {
    companyName: '',
    title: '',
    phone: '',
    email: '',
    address: '',
    taxNumber: '',
    taxUnit: '',
    tcNo: '',
    isPersonCompany: 'false',
    contactName: '',
    segment: 'none',
    label: 'none',
  };
}
