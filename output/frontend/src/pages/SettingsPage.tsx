import { useState, useMemo } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { User, Lock, Loader2, CheckCircle, Link2, Link2Off, Building2, Eye, EyeOff, Package, Search, ChevronDown } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { Separator } from '@/components/ui/separator';
import { useAuthStore } from '@/stores/authStore';
import { apiClient } from '@/api/client';
import type { ApiResponse, ParasutProduct, ParasutProductKey, ParasutProductListItem } from '@/types';
import { useToast } from '@/hooks/use-toast';
import {
  useParasutStatus,
  useConnectParasut,
  useDisconnectParasut,
  useParasutProducts,
  useParasutProductList,
  useSaveParasutProduct,
} from '@/api/parasut';

// ── Profile form ──────────────────────────────────────────────────────────────

const profileSchema = z.object({
  firstName: z.string().min(1, 'Ad gereklidir'),
  lastName: z.string().min(1, 'Soyad gereklidir'),
});
type ProfileForm = z.infer<typeof profileSchema>;

// ── Password form ─────────────────────────────────────────────────────────────

const passwordSchema = z.object({
  newPassword: z.string()
    .min(8, 'En az 8 karakter')
    .regex(/[A-Z]/, 'En az bir büyük harf')
    .regex(/[a-z]/, 'En az bir küçük harf')
    .regex(/[0-9]/, 'En az bir rakam'),
  confirmPassword: z.string(),
}).refine(data => data.newPassword === data.confirmPassword, {
  message: 'Şifreler eşleşmiyor',
  path: ['confirmPassword'],
});
type PasswordForm = z.infer<typeof passwordSchema>;

// ── Paraşüt form ──────────────────────────────────────────────────────────────

const parasutSchema = z.object({
  companyId: z.string().min(1, 'Firma ID gereklidir').regex(/^\d+$/, 'Sayısal olmalıdır'),
  clientId: z.string().min(1, 'Client ID gereklidir'),
  clientSecret: z.string().min(1, 'Client Secret gereklidir'),
  username: z.string().email('Geçerli e-posta giriniz'),
  password: z.string().min(1, 'Şifre gereklidir'),
});
type ParasutForm = z.infer<typeof parasutSchema>;

// ── Paraşüt Product Mapping ───────────────────────────────────────────────────

interface CrmProductDef {
  key: ParasutProductKey;
  label: string;
}

const CRM_PRODUCTS: CrmProductDef[] = [
  { key: 'membership_monthly', label: '1 Aylık Üyelik' },
  { key: 'membership_yearly', label: '1 Yıllık Üyelik' },
  { key: 'sms_1000', label: '1000 SMS' },
  { key: 'sms_2500', label: '2500 SMS' },
  { key: 'sms_5000', label: '5000 SMS' },
  { key: 'sms_10000', label: '10000 SMS' },
];

// Inline searchable combobox for Paraşüt products
function ParasutProductCombobox({
  value,
  onChange,
  products,
  isLoading,
}: {
  value: string;
  onChange: (id: string, name: string, unitPrice: number, vatRate: number) => void;
  products: ParasutProductListItem[];
  isLoading: boolean;
}) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState('');

  const selectedProduct = products.find(p => p.id === value);

  const filtered = useMemo(() => {
    if (!search.trim()) return products;
    const q = search.toLowerCase();
    return products.filter(p => p.name.toLowerCase().includes(q));
  }, [products, search]);

  function handleSelect(product: ParasutProductListItem) {
    onChange(product.id, product.name, product.unitPrice, product.vatRate);
    setOpen(false);
    setSearch('');
  }

  return (
    <div className="relative">
      <button
        type="button"
        onClick={() => setOpen(o => !o)}
        className="flex h-9 w-full items-center justify-between rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
      >
        <span className={selectedProduct ? 'text-foreground truncate' : 'text-muted-foreground'}>
          {isLoading
            ? 'Yükleniyor...'
            : selectedProduct
            ? selectedProduct.name
            : 'Ürün seçin...'}
        </span>
        <ChevronDown className="h-4 w-4 opacity-50 flex-shrink-0 ml-1" />
      </button>

      {open && (
        <div className="absolute z-50 mt-1 w-full rounded-md border bg-popover shadow-md">
          <div className="flex items-center border-b px-3 py-2 gap-2">
            <Search className="h-3.5 w-3.5 text-muted-foreground flex-shrink-0" />
            <input
              autoFocus
              className="flex-1 bg-transparent text-sm outline-none placeholder:text-muted-foreground"
              placeholder="Ürün ara..."
              value={search}
              onChange={e => setSearch(e.target.value)}
            />
          </div>
          <div className="max-h-48 overflow-y-auto p-1">
            {isLoading ? (
              <div className="flex items-center gap-2 px-2 py-3 text-sm text-muted-foreground">
                <Loader2 className="h-3.5 w-3.5 animate-spin" /> Yükleniyor...
              </div>
            ) : filtered.length === 0 ? (
              <p className="px-2 py-3 text-center text-sm text-muted-foreground">Sonuç bulunamadı.</p>
            ) : (
              filtered.map(product => (
                <button
                  key={product.id}
                  type="button"
                  onClick={() => handleSelect(product)}
                  className="flex w-full items-center justify-between rounded-sm px-2 py-1.5 text-sm hover:bg-accent hover:text-accent-foreground focus:bg-accent"
                >
                  <span className="truncate text-left">{product.name}</span>
                  <span className="ml-2 flex-shrink-0 text-xs text-muted-foreground">
                    {product.unitPrice.toLocaleString('tr-TR', { style: 'currency', currency: product.currency || 'TRY', minimumFractionDigits: 2 })}
                  </span>
                </button>
              ))
            )}
          </div>
        </div>
      )}

      {/* Overlay to close on outside click */}
      {open && (
        <div
          className="fixed inset-0 z-40"
          onClick={() => { setOpen(false); setSearch(''); }}
        />
      )}
    </div>
  );
}

function ParasutProductRow({
  productDef,
  existing,
  projectId,
  parasutProducts,
  parasutProductsLoading,
}: {
  productDef: CrmProductDef;
  existing: ParasutProduct | undefined;
  projectId: string;
  parasutProducts: ParasutProductListItem[];
  parasutProductsLoading: boolean;
}) {
  const { toast } = useToast();
  const save = useSaveParasutProduct();
  const [selectedId, setSelectedId] = useState(existing?.parasutProductId ?? '');
  const [selectedName, setSelectedName] = useState(existing?.parasutProductName ?? '');
  const [unitPrice, setUnitPrice] = useState(existing?.unitPrice?.toString() ?? '');
  const [taxRate, setTaxRate] = useState(
    existing ? (existing.taxRate * 100).toFixed(0) : '20'
  );
  const [saved, setSaved] = useState(false);

  function handleProductSelect(id: string, name: string, price: number, vatRate: number) {
    setSelectedId(id);
    setSelectedName(name);
    // Auto-fill price and tax from Paraşüt product if not already set
    if (!unitPrice) setUnitPrice(price.toString());
    setTaxRate((vatRate * 100).toFixed(0));
  }

  async function handleSave() {
    if (!selectedId) {
      toast({
        title: 'Hata',
        description: 'Bir Paraşüt ürünü seçin.',
        variant: 'destructive',
      });
      return;
    }
    try {
      await save.mutateAsync({
        existingId: existing?.id,
        projectId,
        productKey: productDef.key,
        productName: productDef.label,
        parasutProductId: selectedId,
        parasutProductName: selectedName,
        unitPrice: parseFloat(unitPrice) || 0,
        taxRate: (parseFloat(taxRate) || 0) / 100,
      });
      setSaved(true);
      setTimeout(() => setSaved(false), 3000);
      toast({ title: `${productDef.label} eşleştirmesi kaydedildi.` });
    } catch {
      toast({ title: 'Hata', description: 'Kaydedilemedi.', variant: 'destructive' });
    }
  }

  return (
    <div className="grid grid-cols-[1.5fr_2fr_90px_72px_auto] gap-2 items-center py-3 border-b last:border-b-0">
      <span className="text-sm font-medium text-foreground truncate">{productDef.label}</span>
      <ParasutProductCombobox
        value={selectedId}
        onChange={handleProductSelect}
        products={parasutProducts}
        isLoading={parasutProductsLoading}
      />
      <Input
        value={unitPrice}
        onChange={e => setUnitPrice(e.target.value)}
        placeholder="0.00"
        type="number"
        min="0"
        step="0.01"
        className="h-9 text-right"
      />
      <div className="relative">
        <Input
          value={taxRate}
          onChange={e => setTaxRate(e.target.value)}
          placeholder="20"
          type="number"
          min="0"
          max="100"
          step="1"
          className="h-9 text-right pr-5"
        />
        <span className="absolute right-2 top-1/2 -translate-y-1/2 text-xs text-muted-foreground pointer-events-none">%</span>
      </div>
      <div className="flex items-center gap-1.5">
        <Button
          size="sm"
          onClick={handleSave}
          disabled={save.isPending}
          className="h-9 px-3 text-xs"
        >
          {save.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : 'Kaydet'}
        </Button>
        {saved && <CheckCircle className="h-4 w-4 text-emerald-500 flex-shrink-0" />}
      </div>
    </div>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function SettingsPage() {
  const { user, currentProjectId } = useAuthStore();
  const { toast } = useToast();
  const [profileSaved, setProfileSaved] = useState(false);
  const [passwordSaved, setPasswordSaved] = useState(false);
  const [showParasutSecret, setShowParasutSecret] = useState(false);
  const [showParasutPassword, setShowParasutPassword] = useState(false);
  const [loadParasutProductList, setLoadParasutProductList] = useState(false);
  // Paraşüt
  const parasutStatus = useParasutStatus(currentProjectId);
  const connectParasut = useConnectParasut();
  const disconnectParasut = useDisconnectParasut();
  const parasutProducts = useParasutProducts(currentProjectId);
  const parasutProductList = useParasutProductList(currentProjectId, loadParasutProductList);

  const parasutForm = useForm<ParasutForm>({
    resolver: zodResolver(parasutSchema),
    defaultValues: { companyId: '', clientId: '', clientSecret: '', username: '', password: '' },
  });

  const profileForm = useForm<ProfileForm>({
    resolver: zodResolver(profileSchema),
    defaultValues: {
      firstName: user?.firstName ?? '',
      lastName: user?.lastName ?? '',
    },
  });

  const passwordForm = useForm<PasswordForm>({
    resolver: zodResolver(passwordSchema),
    defaultValues: { newPassword: '', confirmPassword: '' },
  });

  const [profileLoading, setProfileLoading] = useState(false);
  const [passwordLoading, setPasswordLoading] = useState(false);

  async function handleProfileSubmit(data: ProfileForm) {
    setProfileLoading(true);
    setProfileSaved(false);
    try {
      await apiClient.put<ApiResponse<unknown>>('/auth/profile', {
        firstName: data.firstName,
        lastName: data.lastName,
      });
      // Update stored user info
      const savedUser = localStorage.getItem('user');
      if (savedUser) {
        const u = JSON.parse(savedUser);
        u.firstName = data.firstName;
        u.lastName = data.lastName;
        localStorage.setItem('user', JSON.stringify(u));
      }
      setProfileSaved(true);
      toast({ title: 'Profil güncellendi' });
    } catch {
      toast({ title: 'Hata', description: 'Profil güncellenemedi.', variant: 'destructive' });
    } finally {
      setProfileLoading(false);
    }
  }

  async function handleParasutConnect(data: ParasutForm) {
    if (!currentProjectId) return;
    try {
      await connectParasut.mutateAsync({
        projectId: currentProjectId,
        companyId: Number(data.companyId),
        clientId: data.clientId,
        clientSecret: data.clientSecret,
        username: data.username,
        password: data.password,
      });
      parasutForm.reset();
      toast({ title: 'Paraşüt bağlandı', description: 'Muhasebe entegrasyonu aktif.' });
    } catch {
      toast({ title: 'Bağlantı hatası', description: 'Bilgileri kontrol edip tekrar deneyin.', variant: 'destructive' });
    }
  }

  async function handleParasutDisconnect() {
    if (!currentProjectId) return;
    try {
      await disconnectParasut.mutateAsync(currentProjectId);
      toast({ title: 'Paraşüt bağlantısı kaldırıldı' });
    } catch {
      toast({ title: 'Hata', description: 'Bağlantı kaldırılamadı.', variant: 'destructive' });
    }
  }

  async function handlePasswordSubmit(data: PasswordForm) {
    setPasswordLoading(true);
    setPasswordSaved(false);
    try {
      await apiClient.put<ApiResponse<unknown>>('/auth/profile', {
        firstName: user?.firstName ?? '',
        lastName: user?.lastName ?? '',
        newPassword: data.newPassword,
      });
      setPasswordSaved(true);
      passwordForm.reset();
      toast({ title: 'Şifre güncellendi' });
    } catch {
      toast({ title: 'Hata', description: 'Şifre güncellenemedi.', variant: 'destructive' });
    } finally {
      setPasswordLoading(false);
    }
  }

  const isConnected = parasutStatus.data?.isConnected ?? false;

  return (
    <div className="space-y-6 max-w-2xl">
      <div>
        <h1 className="text-2xl font-bold text-foreground">Ayarlar</h1>
        <p className="text-muted-foreground text-sm mt-1">Profil, şifre ve entegrasyon ayarlarını yönetin.</p>
      </div>

      {/* ── Paraşüt Integration (SuperAdmin only) ── */}
      {user?.isSuperAdmin && (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              <Building2 className="h-4 w-4" /> Paraşüt Entegrasyonu
            </CardTitle>
            <CardDescription>
              Muhasebe yazılımı Paraşüt ile bağlantı kurun. Fatura ve cari yönetimi bu entegrasyon üzerinden yapılır.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            {/* Connection status banner */}
            {parasutStatus.isLoading ? (
              <div className="flex items-center gap-2 text-sm text-muted-foreground">
                <Loader2 className="h-4 w-4 animate-spin" /> Durum kontrol ediliyor...
              </div>
            ) : isConnected ? (
              <div className="flex items-center justify-between p-4 rounded-lg bg-emerald-500/10 border border-emerald-500/20">
                <div className="flex items-center gap-3">
                  <div className="w-2 h-2 rounded-full bg-emerald-500" />
                  <div>
                    <p className="text-sm font-medium text-foreground">Bağlı</p>
                    <p className="text-xs text-muted-foreground">
                      {parasutStatus.data?.username} · Firma #{parasutStatus.data?.companyId}
                    </p>
                  </div>
                </div>
                <Button
                  variant="outline"
                  size="sm"
                  className="gap-1.5 text-destructive border-destructive/30 hover:bg-destructive/10"
                  onClick={handleParasutDisconnect}
                  disabled={disconnectParasut.isPending}
                >
                  {disconnectParasut.isPending
                    ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
                    : <Link2Off className="h-3.5 w-3.5" />}
                  Bağlantıyı Kes
                </Button>
              </div>
            ) : (
              <div className="flex items-center gap-3 p-4 rounded-lg bg-muted/30 border border-border">
                <div className="w-2 h-2 rounded-full bg-muted-foreground/40" />
                <p className="text-sm text-muted-foreground">Henüz bağlantı kurulmamış.</p>
              </div>
            )}

            {/* Connect form — only shown when not connected */}
            {!isConnected && !parasutStatus.isLoading && (
              <>
                <Separator />
                <form onSubmit={parasutForm.handleSubmit(handleParasutConnect)} className="space-y-4">
                  <div className="grid grid-cols-2 gap-4">
                    <div className="space-y-1.5">
                      <Label>Firma ID (Company ID) *</Label>
                      <Input
                        {...parasutForm.register('companyId')}
                        className="h-10"
                        placeholder="123456"
                      />
                      {parasutForm.formState.errors.companyId && (
                        <p className="text-xs text-destructive">{parasutForm.formState.errors.companyId.message}</p>
                      )}
                    </div>
                    <div className="space-y-1.5">
                      <Label>Paraşüt E-postası *</Label>
                      <Input
                        {...parasutForm.register('username')}
                        className="h-10"
                        placeholder="hesap@firma.com"
                        type="email"
                      />
                      {parasutForm.formState.errors.username && (
                        <p className="text-xs text-destructive">{parasutForm.formState.errors.username.message}</p>
                      )}
                    </div>
                  </div>

                  <div className="space-y-1.5">
                    <Label>Paraşüt Şifresi *</Label>
                    <div className="relative">
                      <Input
                        {...parasutForm.register('password')}
                        type={showParasutPassword ? 'text' : 'password'}
                        className="h-10 pr-10"
                        placeholder="Paraşüt hesap şifresi"
                      />
                      <button
                        type="button"
                        className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
                        onClick={() => setShowParasutPassword(v => !v)}
                      >
                        {showParasutPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                      </button>
                    </div>
                    {parasutForm.formState.errors.password && (
                      <p className="text-xs text-destructive">{parasutForm.formState.errors.password.message}</p>
                    )}
                  </div>

                  <div className="space-y-1.5">
                    <Label>Client ID *</Label>
                    <Input
                      {...parasutForm.register('clientId')}
                      className="h-10 font-mono text-sm"
                      placeholder="OAuth client ID"
                    />
                    {parasutForm.formState.errors.clientId && (
                      <p className="text-xs text-destructive">{parasutForm.formState.errors.clientId.message}</p>
                    )}
                  </div>

                  <div className="space-y-1.5">
                    <Label>Client Secret *</Label>
                    <div className="relative">
                      <Input
                        {...parasutForm.register('clientSecret')}
                        type={showParasutSecret ? 'text' : 'password'}
                        className="h-10 pr-10 font-mono text-sm"
                        placeholder="OAuth client secret"
                      />
                      <button
                        type="button"
                        className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
                        onClick={() => setShowParasutSecret(v => !v)}
                      >
                        {showParasutSecret ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                      </button>
                    </div>
                    {parasutForm.formState.errors.clientSecret && (
                      <p className="text-xs text-destructive">{parasutForm.formState.errors.clientSecret.message}</p>
                    )}
                    <p className="text-xs text-muted-foreground">
                      Client ID ve Secret için Paraşüt destek ekibine başvurun: support@parasut.com
                    </p>
                  </div>

                  <Button
                    type="submit"
                    disabled={connectParasut.isPending}
                    className="gap-2"
                  >
                    {connectParasut.isPending
                      ? <Loader2 className="h-4 w-4 animate-spin" />
                      : <Link2 className="h-4 w-4" />}
                    Paraşüt'e Bağlan
                  </Button>
                </form>
              </>
            )}
          </CardContent>
        </Card>
      )}

      {/* ── Paraşüt Product Mapping (SuperAdmin + connected) ── */}
      {user?.isSuperAdmin && (
        <Card>
          <CardHeader>
            <div className="flex items-start justify-between gap-4">
              <div>
                <CardTitle className="flex items-center gap-2 text-base">
                  <Package className="h-4 w-4" /> Paraşüt Ürün Eşleştirmesi
                </CardTitle>
                <CardDescription className="mt-1">
                  CRM ürünlerini Paraşüt'teki ürünlerle eşleştirin. Fatura oluştururken bu eşleştirmeler otomatik kullanılır.
                </CardDescription>
              </div>
              {currentProjectId && !loadParasutProductList && (
                <Button
                  variant="outline"
                  size="sm"
                  className="flex-shrink-0 gap-1.5 text-xs"
                  onClick={() => setLoadParasutProductList(true)}
                >
                  <Search className="h-3.5 w-3.5" />
                  Paraşüt Ürünlerini Yükle
                </Button>
              )}
              {loadParasutProductList && parasutProductList.isLoading && (
                <div className="flex items-center gap-1.5 text-xs text-muted-foreground flex-shrink-0">
                  <Loader2 className="h-3.5 w-3.5 animate-spin" /> Yükleniyor...
                </div>
              )}
            </div>
          </CardHeader>
          <CardContent>
            {parasutProducts.isLoading ? (
              <div className="flex items-center gap-2 text-sm text-muted-foreground py-4">
                <Loader2 className="h-4 w-4 animate-spin" /> Yükleniyor...
              </div>
            ) : !currentProjectId ? (
              <p className="text-sm text-muted-foreground py-4 text-center">
                Ürün eşleştirmesi için önce bir proje seçin.
              </p>
            ) : (
              <>
                {/* Column headers */}
                <div className="grid grid-cols-[1.5fr_2fr_90px_72px_auto] gap-2 pb-2 border-b">
                  <span className="text-xs font-medium text-muted-foreground uppercase tracking-wide">CRM Ürünü</span>
                  <span className="text-xs font-medium text-muted-foreground uppercase tracking-wide">Paraşüt Ürünü</span>
                  <span className="text-xs font-medium text-muted-foreground uppercase tracking-wide text-right">Birim Fiyat</span>
                  <span className="text-xs font-medium text-muted-foreground uppercase tracking-wide text-right">KDV</span>
                  <span className="text-xs font-medium text-muted-foreground uppercase tracking-wide" />
                </div>
                {CRM_PRODUCTS.map(productDef => (
                  <ParasutProductRow
                    key={productDef.key}
                    productDef={productDef}
                    existing={parasutProducts.data?.find(p => p.productKey === productDef.key)}
                    projectId={currentProjectId}
                    parasutProducts={parasutProductList.data ?? []}
                    parasutProductsLoading={parasutProductList.isLoading}
                  />
                ))}
              </>
            )}
          </CardContent>
        </Card>
      )}

      {/* Profile section */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <User className="h-4 w-4" /> Profil Bilgileri
          </CardTitle>
          <CardDescription>Ad, soyad ve diğer hesap bilgileriniz.</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="flex items-center gap-4 mb-6 p-4 rounded-lg bg-muted/30 border border-border">
            <div className="w-14 h-14 rounded-full bg-primary/15 flex items-center justify-center flex-shrink-0">
              <span className="text-primary text-xl font-bold">
                {user?.firstName?.[0]}{user?.lastName?.[0]}
              </span>
            </div>
            <div>
              <p className="font-semibold text-foreground">{user?.firstName} {user?.lastName}</p>
              <p className="text-sm text-muted-foreground">{user?.email}</p>
              {user?.isSuperAdmin && (
                <Badge variant="secondary" className="mt-1 text-xs">SuperAdmin</Badge>
              )}
            </div>
          </div>

          <form onSubmit={profileForm.handleSubmit(handleProfileSubmit)} className="space-y-4">
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label>Ad *</Label>
                <Input {...profileForm.register('firstName')} className="h-10" />
                {profileForm.formState.errors.firstName && (
                  <p className="text-xs text-destructive">{profileForm.formState.errors.firstName.message}</p>
                )}
              </div>
              <div className="space-y-1.5">
                <Label>Soyad *</Label>
                <Input {...profileForm.register('lastName')} className="h-10" />
                {profileForm.formState.errors.lastName && (
                  <p className="text-xs text-destructive">{profileForm.formState.errors.lastName.message}</p>
                )}
              </div>
            </div>
            <div className="space-y-1.5">
              <Label>E-posta</Label>
              <Input value={user?.email ?? ''} disabled className="h-10 opacity-60" />
              <p className="text-xs text-muted-foreground">E-posta adresi değiştirilemez.</p>
            </div>
            <div className="flex items-center gap-2">
              <Button type="submit" disabled={profileLoading} className="gap-2">
                {profileLoading && <Loader2 className="h-4 w-4 animate-spin" />}
                Kaydet
              </Button>
              {profileSaved && (
                <span className="flex items-center gap-1 text-sm text-emerald-500">
                  <CheckCircle className="h-4 w-4" /> Kaydedildi
                </span>
              )}
            </div>
          </form>
        </CardContent>
      </Card>

      {/* Password section */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Lock className="h-4 w-4" /> Şifre Değiştir
          </CardTitle>
          <CardDescription>Hesabınızın güvenliği için düzenli olarak şifrenizi değiştirin.</CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={passwordForm.handleSubmit(handlePasswordSubmit)} className="space-y-4">
            <div className="space-y-1.5">
              <Label>Yeni Şifre *</Label>
              <Input
                type="password"
                {...passwordForm.register('newPassword')}
                className="h-10"
                placeholder="En az 8 karakter"
              />
              {passwordForm.formState.errors.newPassword && (
                <p className="text-xs text-destructive">{passwordForm.formState.errors.newPassword.message}</p>
              )}
              <p className="text-xs text-muted-foreground">En az 8 karakter, büyük/küçük harf ve rakam içermelidir.</p>
            </div>
            <div className="space-y-1.5">
              <Label>Yeni Şifre (Tekrar) *</Label>
              <Input
                type="password"
                {...passwordForm.register('confirmPassword')}
                className="h-10"
                placeholder="Şifreyi tekrar girin"
              />
              {passwordForm.formState.errors.confirmPassword && (
                <p className="text-xs text-destructive">{passwordForm.formState.errors.confirmPassword.message}</p>
              )}
            </div>
            <div className="flex items-center gap-2">
              <Button type="submit" disabled={passwordLoading} variant="outline" className="gap-2">
                {passwordLoading && <Loader2 className="h-4 w-4 animate-spin" />}
                Şifreyi Güncelle
              </Button>
              {passwordSaved && (
                <span className="flex items-center gap-1 text-sm text-emerald-500">
                  <CheckCircle className="h-4 w-4" /> Güncellendi
                </span>
              )}
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
