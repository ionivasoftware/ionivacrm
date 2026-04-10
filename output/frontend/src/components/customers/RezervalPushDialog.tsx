import { useCallback, useEffect, useRef, useState } from 'react';
import { useForm, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Loader2, Send, Building2, Upload, X } from 'lucide-react';
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
import { usePushToRezerval } from '@/api/customers';
import { useToast } from '@/hooks/use-toast';
import { cn } from '@/lib/utils';
import type { Customer } from '@/types';

// ── Schema ───────────────────────────────────────────────────────────────────

const schema = z.object({
  name: z.string().min(1, 'Firma adı gereklidir'),
  title: z.string().min(1, 'Ünvan gereklidir'),
  phone: z.string().min(1, 'Telefon gereklidir'),
  email: z.string().min(1, 'E-posta gereklidir'),
  taxUnit: z.string(),
  taxNumber: z.string(),
  tcNo: z.string(),
  isPersonCompany: z.enum(['true', 'false']),
  address: z.string(),
  experationDate: z.string(),
  // Admin fields — only used for new Rezerval registrations (isUpdate=false)
  adminFirstName: z.string(),
  adminLastName: z.string(),
  adminLoginName: z.string(),
  adminPassword: z.string(),
  adminEmail: z.string(),
  adminPhone: z.string(),
});

type FormData = z.infer<typeof schema>;

// ── Props ─────────────────────────────────────────────────────────────────────

interface RezervalPushDialogProps {
  isOpen: boolean;
  onClose: () => void;
  customer: Customer;
}

// ── Component ─────────────────────────────────────────────────────────────────

export function RezervalPushDialog({ isOpen, onClose, customer }: RezervalPushDialogProps) {
  const { toast } = useToast();
  const pushMutation = usePushToRezerval(customer.id);
  const isUpdate = customer.legacyId?.startsWith('REZV-') || customer.legacyId?.startsWith('SAASB-');

  // ── Logo upload state ────────────────────────────────────────────────────
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [logoFile, setLogoFile] = useState<File | null>(null);
  const [logoPreviewUrl, setLogoPreviewUrl] = useState<string | null>(null);
  const [logoBase64, setLogoBase64] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    control,
    reset,
    formState: { errors },
  } = useForm<FormData>({
    resolver: zodResolver(schema),
    defaultValues: getDefaults(customer),
  });

  useEffect(() => {
    if (isOpen) {
      reset(getDefaults(customer));
      setLogoFile(null);
      setLogoPreviewUrl(null);
      setLogoBase64(null);
    }
  }, [isOpen, customer, reset]);

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
    const reader = new FileReader();
    reader.onload = (e) => {
      const result = e.target?.result as string;
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

  useEffect(() => {
    return () => {
      if (logoPreviewUrl) URL.revokeObjectURL(logoPreviewUrl);
    };
  }, [logoPreviewUrl]);

  // ── Submit ────────────────────────────────────────────────────────────────

  const onSubmit = async (data: FormData) => {
    try {
      const adminFullName = [data.adminFirstName, data.adminLastName]
        .filter(Boolean)
        .join(' ');
      await pushMutation.mutateAsync({
        name: data.name,
        title: data.title,
        phone: data.phone,
        email: data.email,
        taxUnit: data.taxUnit,
        taxNumber: data.taxNumber,
        tcNo: data.tcNo || undefined,
        isPersonCompany: data.isPersonCompany === 'true',
        address: data.address,
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
      });
      toast({
        title: isUpdate ? 'RezervAl güncellendi' : "RezervAl'a gönderildi",
        description: isUpdate
          ? `${customer.companyName} RezervAl'da güncellendi.`
          : `${customer.companyName} RezervAl'a aktarıldı.`,
      });
      onClose();
    } catch {
      toast({
        title: 'Hata',
        description: isUpdate ? 'RezervAl güncellenemedi.' : "RezervAl'a gönderilemedi.",
        variant: 'destructive',
      });
    }
  };

  return (
    <Dialog open={isOpen} onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-2xl max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <div className="flex items-center gap-3">
            <div className="w-9 h-9 rounded-lg bg-teal-500/10 flex items-center justify-center flex-shrink-0">
              <Send className="h-5 w-5 text-teal-500" />
            </div>
            <DialogTitle className="text-lg">
              {isUpdate ? 'RezervAl Firma Güncelle' : "RezervAl'a Gönder"}
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
                <Label htmlFor="rp-name">
                  Firma Adı <span className="text-destructive">*</span>
                </Label>
                <Input
                  id="rp-name"
                  {...register('name')}
                  className={cn('h-11', errors.name && 'border-destructive')}
                />
                {errors.name && <p className="text-xs text-destructive">{errors.name.message}</p>}
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="rp-title">
                  Ünvan <span className="text-destructive">*</span>
                </Label>
                <Input
                  id="rp-title"
                  {...register('title')}
                  className={cn('h-11', errors.title && 'border-destructive')}
                />
                {errors.title && <p className="text-xs text-destructive">{errors.title.message}</p>}
              </div>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label htmlFor="rp-phone">
                  Telefon <span className="text-destructive">*</span>
                </Label>
                <Input id="rp-phone" type="tel" {...register('phone')} className={cn('h-11', errors.phone && 'border-destructive')} />
                {errors.phone && <p className="text-xs text-destructive">{errors.phone.message}</p>}
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="rp-email">
                  E-posta <span className="text-destructive">*</span>
                </Label>
                <Input id="rp-email" type="email" {...register('email')} className={cn('h-11', errors.email && 'border-destructive')} />
                {errors.email && <p className="text-xs text-destructive">{errors.email.message}</p>}
              </div>
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="rp-address">Adres</Label>
              <Input id="rp-address" {...register('address')} className="h-11" />
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div className="space-y-1.5">
                <Label htmlFor="rp-taxNumber">Vergi No</Label>
                <Input id="rp-taxNumber" {...register('taxNumber')} className="h-11" />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="rp-taxUnit">Vergi Dairesi</Label>
                <Input id="rp-taxUnit" {...register('taxUnit')} className="h-11" />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="rp-tcNo">TC Kimlik No</Label>
                <Input id="rp-tcNo" {...register('tcNo')} className="h-11" placeholder="Şahıs firma ise" />
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
                <Label htmlFor="rp-experationDate">Bitiş Tarihi</Label>
                <Input id="rp-experationDate" type="date" {...register('experationDate')} className="h-11" />
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
              ) : customer.logoUrl ? (
                <div className="w-20 h-20 rounded-lg border border-border overflow-hidden flex-shrink-0">
                  <img
                    src={customer.logoUrl}
                    alt="Mevcut logo"
                    className="w-full h-full object-contain"
                  />
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

          {/* ── Admin Kullanıcı Bilgileri — only for NEW Rezerval registrations ── */}
          {!isUpdate && (
            <>
              <Separator />
              <div className="space-y-3">
                <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
                  Yönetici Bilgileri
                </p>
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                  <div className="space-y-1.5">
                    <Label htmlFor="rp-adminFirstName">Yönetici Adı</Label>
                    <Input id="rp-adminFirstName" placeholder="Ad" {...register('adminFirstName')} className="h-11" />
                  </div>
                  <div className="space-y-1.5">
                    <Label htmlFor="rp-adminLastName">Yönetici Soyadı</Label>
                    <Input id="rp-adminLastName" placeholder="Soyad" {...register('adminLastName')} className="h-11" />
                  </div>
                </div>
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                  <div className="space-y-1.5">
                    <Label htmlFor="rp-adminLoginName">Kullanıcı Adı</Label>
                    <Input id="rp-adminLoginName" {...register('adminLoginName')} className="h-11" />
                  </div>
                  <div className="space-y-1.5">
                    <Label htmlFor="rp-adminPassword">Şifre</Label>
                    <Input id="rp-adminPassword" type="password" {...register('adminPassword')} className="h-11" />
                  </div>
                </div>
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                  <div className="space-y-1.5">
                    <Label htmlFor="rp-adminEmail">Yönetici E-posta</Label>
                    <Input id="rp-adminEmail" type="email" {...register('adminEmail')} className="h-11" />
                  </div>
                  <div className="space-y-1.5">
                    <Label htmlFor="rp-adminPhone">Yönetici Telefonu</Label>
                    <Input id="rp-adminPhone" type="tel" {...register('adminPhone')} className="h-11" />
                  </div>
                </div>
              </div>
            </>
          )}

          <DialogFooter className="gap-2 pt-2">
            <Button type="button" variant="outline" onClick={onClose} disabled={pushMutation.isPending} className="h-11">
              İptal
            </Button>
            <Button
              type="submit"
              disabled={pushMutation.isPending}
              className="h-11 min-w-[140px] bg-teal-600 hover:bg-teal-700"
            >
              {pushMutation.isPending && <Loader2 className="h-4 w-4 animate-spin mr-2" />}
              {isUpdate ? 'Güncelle' : "RezervAl'a Gönder"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function getDefaults(customer: Customer): FormData {
  const nameParts = (customer.contactName ?? '').split(' ');
  const firstName = nameParts[0] ?? '';
  const lastName = nameParts.slice(1).join(' ');
  return {
    name: customer.companyName,
    title: customer.companyName,
    phone: customer.phone ?? '',
    email: customer.email ?? '',
    taxUnit: customer.taxUnit ?? '',
    taxNumber: customer.taxNumber ?? '',
    tcNo: '',
    isPersonCompany: 'false',
    address: customer.address ?? '',
    experationDate: customer.expirationDate
      ? new Date(customer.expirationDate).toISOString().split('T')[0]
      : '',
    adminFirstName: firstName,
    adminLastName: lastName,
    adminLoginName: '',
    adminPassword: '',
    adminEmail: customer.email ?? '',
    adminPhone: customer.phone ?? '',
  };
}
