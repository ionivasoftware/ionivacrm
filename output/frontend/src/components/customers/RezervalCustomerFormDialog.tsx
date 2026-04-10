import { useCallback, useEffect, useRef, useState } from 'react';
import { useForm, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Loader2, Building2, Upload, X } from 'lucide-react';
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
import { useCreateCustomer } from '@/api/customers';
import { apiClient } from '@/api/client';
import { useToast } from '@/hooks/use-toast';
import { useQueryClient } from '@tanstack/react-query';
import { cn } from '@/lib/utils';
import type { ApiResponse, Customer } from '@/types';

// ── Schema ───────────────────────────────────────────────────────────────────

const schema = z.object({
  // Firma bilgileri
  name: z.string().min(1, 'Firma adı gereklidir'),
  title: z.string(),
  phone: z.string().min(1, 'Telefon numarası gereklidir'),
  email: z.string().min(1, 'E-posta gereklidir'),
  taxNumber: z.string(),
  taxUnit: z.string(),
  tcNo: z.string(),
  address: z.string(),
  isPersonCompany: z.enum(['true', 'false']),
  experationDate: z.string(),
  // Yönetici bilgileri
  adminFirstName: z.string(),
  adminLastName: z.string(),
  adminLoginName: z.string(),
  adminPassword: z.string(),
  adminEmail: z.string(),
  adminPhone: z.string(),
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

  // ── Logo upload state ────────────────────────────────────────────────────
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [logoFile, setLogoFile] = useState<File | null>(null);
  const [logoPreviewUrl, setLogoPreviewUrl] = useState<string | null>(null);
  const [logoBase64, setLogoBase64] = useState<string | null>(null);
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

  // Reset form when dialog opens/closes
  useEffect(() => {
    if (isOpen) {
      reset(getDefaultValues());
      setLogoFile(null);
      setLogoPreviewUrl(null);
      setLogoBase64(null);
    }
  }, [isOpen, reset]);

  // ── Logo file handling ────────────────────────────────────────────────────

  const handleLogoChange = useCallback((file: File | null) => {
    if (!file) {
      setLogoFile(null);
      setLogoPreviewUrl(null);
      setLogoBase64(null);
      return;
    }

    setLogoFile(file);
    const objectUrl = URL.createObjectURL(file);
    setLogoPreviewUrl(objectUrl);

    // Convert to base64
    const reader = new FileReader();
    reader.onload = (e) => {
      const result = e.target?.result as string;
      // Strip the data URL prefix (e.g. "data:image/png;base64,")
      const base64 = result.split(',')[1] ?? '';
      setLogoBase64(base64);
    };
    reader.readAsDataURL(file);
  }, []);

  function handleFileInputChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0] ?? null;
    handleLogoChange(file);
  }

  function handleRemoveLogo() {
    handleLogoChange(null);
    if (fileInputRef.current) fileInputRef.current.value = '';
  }

  // Cleanup object URL when component unmounts or logo changes
  useEffect(() => {
    return () => {
      if (logoPreviewUrl) URL.revokeObjectURL(logoPreviewUrl);
    };
  }, [logoPreviewUrl]);

  // ── Submit ────────────────────────────────────────────────────────────────

  const onSubmit = async (data: FormData) => {
    setIsSubmitting(true);
    try {
      // Step 1: Create a basic CRM customer entry
      const newCustomer = await createMutation.mutateAsync({
        companyName: data.name,
        phone: data.phone || undefined,
        email: data.email || undefined,
        address: data.address || undefined,
        taxNumber: data.taxNumber || undefined,
        taxUnit: data.taxUnit || undefined,
        contactName: [data.adminFirstName, data.adminLastName].filter(Boolean).join(' ') || undefined,
        // RezervAl customers start as Active
        status: 'Active',
      });

      // Step 2: Push to RezervAl via existing endpoint
      const adminFullName = [data.adminFirstName, data.adminLastName]
        .filter(Boolean)
        .join(' ');

      await apiClient.post<ApiResponse<Customer>>(
        `/customers/${newCustomer.id}/push-to-rezerval`,
        {
          name: data.name,
          title: data.title || data.name,
          phone: data.phone,
          email: data.email,
          taxUnit: data.taxUnit || '',
          taxNumber: data.taxNumber || '',
          tcNo: data.tcNo || undefined,
          isPersonCompany: data.isPersonCompany === 'true',
          address: data.address || '',
          language: 1,
          countryPhoneCode: 90,
          experationDate: data.experationDate || undefined,
          adminNameSurname: adminFullName || undefined,
          adminLoginName: data.adminLoginName || undefined,
          adminPassword: data.adminPassword || undefined,
          adminEmail: data.adminEmail || undefined,
          adminPhone: data.adminPhone || undefined,
          logoBase64: logoBase64 || undefined,
          logoFileName: logoFile?.name || undefined,
        }
      );

      // Invalidate caches
      queryClient.invalidateQueries({ queryKey: ['customers'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });

      toast({
        title: 'Müşteri oluşturuldu',
        description: `${data.name} RezervAl'a başarıyla aktarıldı.`,
      });

      onSuccess?.(newCustomer);
      onClose();
    } catch (err) {
      const errMsg =
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message;
      toast({
        title: 'Hata oluştu',
        description: errMsg ?? 'Müşteri oluşturulurken bir hata oluştu. Lütfen tekrar deneyiniz.',
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
                  placeholder="Firma veya müşteri adını giriniz"
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
                <Label htmlFor="rz-experationDate">Bitiş Tarihi</Label>
                <Input
                  id="rz-experationDate"
                  type="date"
                  {...register('experationDate')}
                  className="h-11"
                />
              </div>
            </div>
          </div>

          <Separator />

          {/* ── Logo ── */}
          <div className="space-y-3">
            <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
              Logo
            </p>
            <div className="flex items-center gap-4">
              {logoPreviewUrl ? (
                <div className="relative w-20 h-20 rounded-lg border border-border overflow-hidden flex-shrink-0">
                  <img
                    src={logoPreviewUrl}
                    alt="Logo önizleme"
                    className="w-full h-full object-contain"
                  />
                  <button
                    type="button"
                    onClick={handleRemoveLogo}
                    className="absolute top-1 right-1 bg-destructive text-destructive-foreground rounded-full w-5 h-5 flex items-center justify-center hover:opacity-90"
                  >
                    <X className="w-3 h-3" />
                  </button>
                </div>
              ) : (
                <div className="w-20 h-20 rounded-lg border border-dashed border-border flex items-center justify-center flex-shrink-0 bg-muted/50">
                  <Building2 className="w-8 h-8 text-muted-foreground/40" />
                </div>
              )}
              <div className="flex-1 space-y-1">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="gap-2"
                  onClick={() => fileInputRef.current?.click()}
                >
                  <Upload className="h-4 w-4" />
                  {logoFile ? 'Logoyu Değiştir' : 'Logo Yükle'}
                </Button>
                <p className="text-xs text-muted-foreground">
                  PNG, JPG veya GIF · Maks. 2 MB
                </p>
                {logoFile && (
                  <p className="text-xs text-foreground font-medium truncate max-w-[200px]">
                    {logoFile.name}
                  </p>
                )}
              </div>
            </div>
            <input
              ref={fileInputRef}
              type="file"
              accept="image/png,image/jpeg,image/gif,image/webp"
              className="hidden"
              onChange={handleFileInputChange}
            />
          </div>

          <Separator />

          {/* ── Yönetici Bilgileri ── */}
          <div className="space-y-3">
            <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
              Yönetici Bilgileri
            </p>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label htmlFor="rz-adminFirstName">Yönetici Adı</Label>
                <Input
                  id="rz-adminFirstName"
                  placeholder="Ad"
                  {...register('adminFirstName')}
                  className="h-11"
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="rz-adminLastName">Yönetici Soyadı</Label>
                <Input
                  id="rz-adminLastName"
                  placeholder="Soyad"
                  {...register('adminLastName')}
                  className="h-11"
                />
              </div>
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label htmlFor="rz-adminLoginName">Kullanıcı Adı</Label>
                <Input
                  id="rz-adminLoginName"
                  placeholder="Giriş kullanıcı adı"
                  {...register('adminLoginName')}
                  className="h-11"
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="rz-adminPassword">Şifre</Label>
                <Input
                  id="rz-adminPassword"
                  type="password"
                  placeholder="Giriş şifresi"
                  {...register('adminPassword')}
                  className="h-11"
                />
              </div>
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label htmlFor="rz-adminEmail">Yönetici E-posta</Label>
                <Input
                  id="rz-adminEmail"
                  type="email"
                  placeholder="yonetici@sirket.com"
                  {...register('adminEmail')}
                  className="h-11"
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="rz-adminPhone">Yönetici Telefonu</Label>
                <Input
                  id="rz-adminPhone"
                  type="tel"
                  placeholder="+90 5xx xxx xx xx"
                  {...register('adminPhone')}
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
              RezervAl'a Ekle
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
    experationDate: '',
    adminFirstName: '',
    adminLastName: '',
    adminLoginName: '',
    adminPassword: '',
    adminEmail: '',
    adminPhone: '',
  };
}
