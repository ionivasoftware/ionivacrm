import { useState } from 'react';
import {
  RefreshCw, Pencil, Plus, Trash2, Loader2, Tags, MessageSquare,
  Users, Building2, AlertTriangle, Inbox,
} from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter,
} from '@/components/ui/dialog';
import { cn } from '@/lib/utils';
import { useToast } from '@/hooks/use-toast';
import {
  usePricingPlans, useUpdatePlan, useSmsPackages, useCreateSmsPackage,
  useUpdateSmsPackage, useDeleteSmsPackage,
  type PricingPlan, type SmsPackage,
} from '@/api/pricing';

// ── Helpers ───────────────────────────────────────────────────────────────────

function formatTL(n: number): string {
  return `${n.toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ₺`;
}

function limitLabel(n: number): string {
  return n === 0 ? 'Sınırsız' : n.toLocaleString('tr-TR');
}

function errorMessage(err: unknown, fallback: string): string {
  if (
    err && typeof err === 'object' && 'response' in err &&
    err.response && typeof err.response === 'object' && 'data' in err.response
  ) {
    const data = (err.response as { data?: { message?: string; errors?: string[] } }).data;
    if (data?.message) return data.message;
    if (data?.errors?.length) return data.errors[0];
  }
  return fallback;
}

function Pill({ className, children }: { className: string; children: React.ReactNode }) {
  return (
    <span className={`inline-flex items-center gap-1 rounded-full border px-2.5 py-0.5 text-xs font-semibold ${className}`}>
      {children}
    </span>
  );
}

function ActivePill({ active }: { active: boolean }) {
  return active
    ? <Pill className="bg-emerald-500/15 text-emerald-400 border-emerald-500/30">Aktif</Pill>
    : <Pill className="bg-slate-500/15 text-slate-400 border-slate-500/30">Pasif</Pill>;
}

const TIER_CLASS: Record<string, string> = {
  Standart: 'bg-blue-500/15 text-blue-300 border-blue-500/30',
  Pro:      'bg-violet-500/15 text-violet-300 border-violet-500/30',
  Prime:    'bg-amber-500/15 text-amber-300 border-amber-500/30',
};

// ── Plan edit dialog ────────────────────────────────────────────────────────

function PlanDialog({ plan, onClose }: { plan: PricingPlan; onClose: () => void }) {
  const { toast } = useToast();
  const update = useUpdatePlan();
  const [name, setName] = useState(plan.name);
  const [description, setDescription] = useState(plan.description ?? '');
  const [priceMonthly, setPriceMonthly] = useState(String(plan.priceMonthly));
  const [priceYearly, setPriceYearly] = useState(String(plan.priceYearly));
  const [maxUsers, setMaxUsers] = useState(String(plan.maxUsers));
  const [maxElevators, setMaxElevators] = useState(String(plan.maxElevators));
  const [isActive, setIsActive] = useState(plan.isActive);

  async function save() {
    const pm = Number(priceMonthly), py = Number(priceYearly);
    const mu = Number(maxUsers), me = Number(maxElevators);
    if (!name.trim()) return toast({ title: 'Plan adı zorunludur.', variant: 'destructive' });
    if (!(pm > 0) || !(py > 0)) return toast({ title: 'Aylık ve yıllık fiyat 0\'dan büyük olmalı.', variant: 'destructive' });
    // Require an explicit integer for the limits — an empty field must NOT silently become 0 (unlimited).
    if (maxUsers.trim() === '' || maxElevators.trim() === '' ||
        !Number.isInteger(mu) || !Number.isInteger(me) || mu < 0 || me < 0) {
      return toast({ title: 'Kullanıcı/asansör limiti tam sayı olmalı (0 = sınırsız).', variant: 'destructive' });
    }

    try {
      await update.mutateAsync({
        id: plan.id,
        name: name.trim(),
        description: description.trim() || null,
        priceMonthly: pm,
        priceYearly: py,
        maxUsers: mu,
        maxElevators: me,
        isActive,
      });
      toast({ title: 'Plan güncellendi', description: name.trim() });
      onClose();
    } catch (err) {
      toast({ title: 'Güncellenemedi', description: errorMessage(err, 'İşlem başarısız.'), variant: 'destructive' });
    }
  }

  return (
    <Dialog open onOpenChange={(o) => !o && !update.isPending && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Tags className="h-4 w-4" /> Plan Düzenle
            {plan.tier && <Pill className={TIER_CLASS[plan.tier] ?? 'bg-slate-500/15 text-slate-400 border-slate-500/30'}>{plan.tier}</Pill>}
          </DialogTitle>
        </DialogHeader>
        <div className="space-y-4 py-2">
          <div className="space-y-1.5">
            <Label>Plan Adı *</Label>
            <Input value={name} onChange={(e) => setName(e.target.value)} className="h-10" />
          </div>
          <div className="space-y-1.5">
            <Label>Açıklama</Label>
            <Input value={description} onChange={(e) => setDescription(e.target.value)} className="h-10" />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label>Aylık Fiyat (net ₺) *</Label>
              <Input type="number" min={0} step="0.01" value={priceMonthly} onChange={(e) => setPriceMonthly(e.target.value)} className="h-10" />
            </div>
            <div className="space-y-1.5">
              <Label>Yıllık Fiyat (toplam net ₺) *</Label>
              <Input type="number" min={0} step="0.01" value={priceYearly} onChange={(e) => setPriceYearly(e.target.value)} className="h-10" />
            </div>
            <div className="space-y-1.5">
              <Label>Maks. Kullanıcı (0=∞)</Label>
              <Input type="number" min={0} step="1" value={maxUsers} onChange={(e) => setMaxUsers(e.target.value)} className="h-10" />
            </div>
            <div className="space-y-1.5">
              <Label>Maks. Asansör (0=∞)</Label>
              <Input type="number" min={0} step="1" value={maxElevators} onChange={(e) => setMaxElevators(e.target.value)} className="h-10" />
            </div>
          </div>
          <label className="flex items-center gap-3 p-3 rounded-lg border cursor-pointer">
            <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} className="w-4 h-4" />
            <span className="text-sm font-medium">Satışta (aktif)</span>
          </label>
          <p className="text-xs text-muted-foreground">
            Fiyatlar KDV hariç net TL'dir. Kademe (tier) ve iyzico kodları CRM'den değiştirilemez.
            Değişiklik mevcut aboneleri etkilemez; yeni satış/yenileme/yükseltmede geçerli olur.
          </p>
        </div>
        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose} disabled={update.isPending}>İptal</Button>
          <Button onClick={save} disabled={update.isPending}>
            {update.isPending && <Loader2 className="h-4 w-4 animate-spin mr-2" />} Güncelle
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── SMS package add/edit dialog ─────────────────────────────────────────────

function SmsPackageDialog({ pkg, onClose }: { pkg?: SmsPackage; onClose: () => void }) {
  const { toast } = useToast();
  const create = useCreateSmsPackage();
  const update = useUpdateSmsPackage();
  const isEdit = !!pkg;
  const [name, setName] = useState(pkg?.name ?? '');
  const [smsCount, setSmsCount] = useState(pkg ? String(pkg.smsCount) : '');
  const [price, setPrice] = useState(pkg ? String(pkg.price) : '');
  const [isActive, setIsActive] = useState(pkg?.isActive ?? true);
  const pending = create.isPending || update.isPending;

  async function save() {
    const sc = Number(smsCount), pr = Number(price);
    if (!name.trim()) return toast({ title: 'Paket adı zorunludur.', variant: 'destructive' });
    if (!Number.isInteger(sc) || sc <= 0) return toast({ title: 'SMS adedi pozitif tam sayı olmalı.', variant: 'destructive' });
    if (!(pr > 0)) return toast({ title: 'Fiyat 0\'dan büyük olmalı.', variant: 'destructive' });

    try {
      if (isEdit && pkg) {
        await update.mutateAsync({ id: pkg.id, name: name.trim(), smsCount: sc, price: pr, isActive });
        toast({ title: 'Paket güncellendi', description: name.trim() });
      } else {
        await create.mutateAsync({ name: name.trim(), smsCount: sc, price: pr });
        toast({ title: 'Paket oluşturuldu', description: name.trim() });
      }
      onClose();
    } catch (err) {
      toast({ title: 'İşlem başarısız', description: errorMessage(err, 'İşlem başarısız.'), variant: 'destructive' });
    }
  }

  return (
    <Dialog open onOpenChange={(o) => !o && !pending && onClose()}>
      <DialogContent className="sm:max-w-sm">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <MessageSquare className="h-4 w-4" /> {isEdit ? 'SMS Paketi Düzenle' : 'Yeni SMS Paketi'}
          </DialogTitle>
        </DialogHeader>
        <div className="space-y-4 py-2">
          <div className="space-y-1.5">
            <Label>Paket Adı *</Label>
            <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="Ör: 1000 SMS Paketi" className="h-10" />
          </div>
          <div className="space-y-1.5">
            <Label>SMS Adedi *</Label>
            <Input type="number" min={1} step="1" value={smsCount} onChange={(e) => setSmsCount(e.target.value)} placeholder="1000" className="h-10" />
          </div>
          <div className="space-y-1.5">
            <Label>Fiyat (net ₺) *</Label>
            <Input type="number" min={0} step="0.01" value={price} onChange={(e) => setPrice(e.target.value)} placeholder="179" className="h-10" />
          </div>
          {isEdit && (
            <label className="flex items-center gap-3 p-3 rounded-lg border cursor-pointer">
              <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} className="w-4 h-4" />
              <span className="text-sm font-medium">Satışta (aktif)</span>
            </label>
          )}
        </div>
        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose} disabled={pending}>İptal</Button>
          <Button onClick={save} disabled={pending}>
            {pending && <Loader2 className="h-4 w-4 animate-spin mr-2" />} {isEdit ? 'Güncelle' : 'Oluştur'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── SMS package delete confirm ──────────────────────────────────────────────

function DeleteSmsDialog({ pkg, onClose }: { pkg: SmsPackage; onClose: () => void }) {
  const { toast } = useToast();
  const del = useDeleteSmsPackage();

  async function confirm() {
    try {
      await del.mutateAsync(pkg.id);
      toast({ title: 'Paket satıştan kaldırıldı', description: pkg.name });
      onClose();
    } catch (err) {
      toast({ title: 'İşlem başarısız', description: errorMessage(err, 'İşlem başarısız.'), variant: 'destructive' });
    }
  }

  return (
    <Dialog open onOpenChange={(o) => !o && !del.isPending && onClose()}>
      <DialogContent className="sm:max-w-sm">
        <DialogHeader>
          <DialogTitle>Paketi satıştan kaldır</DialogTitle>
        </DialogHeader>
        <p className="text-sm text-muted-foreground py-1">
          <span className="font-medium text-foreground">{pkg.name}</span> pasifleştirilecek (soft delete).
          Satın alma geçmişi korunur; tekrar satışa açmak için düzenleyip "Aktif" yapabilirsin.
        </p>
        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose} disabled={del.isPending}>Vazgeç</Button>
          <Button onClick={confirm} disabled={del.isPending} className="bg-red-600 hover:bg-red-700 text-white">
            {del.isPending && <Loader2 className="h-4 w-4 animate-spin mr-2" />} Satıştan Kaldır
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Page ────────────────────────────────────────────────────────────────────

type Tab = 'plans' | 'sms';

export function PricingAdminPage() {
  const [tab, setTab] = useState<Tab>('plans');
  const plans = usePricingPlans();
  const packages = useSmsPackages();

  const [editPlan, setEditPlan] = useState<PricingPlan | null>(null);
  const [smsDialog, setSmsDialog] = useState<{ mode: 'create' } | { mode: 'edit'; pkg: SmsPackage } | null>(null);
  const [deletePkg, setDeletePkg] = useState<SmsPackage | null>(null);

  const active = tab === 'plans' ? plans : packages;

  return (
    <div className="p-6 space-y-6 max-w-4xl mx-auto">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-foreground">Fiyat Yönetimi</h1>
          <p className="text-sm text-muted-foreground mt-1">
            EMS abonelik planları ve SMS paketleri · değişiklikler anında geçerli olur
          </p>
        </div>
        <div className="flex items-center gap-2">
          {tab === 'sms' && (
            <Button size="sm" onClick={() => setSmsDialog({ mode: 'create' })}>
              <Plus className="h-4 w-4 mr-1.5" /> Yeni Paket
            </Button>
          )}
          <Button variant="outline" size="sm" onClick={() => active.refetch()} disabled={active.isFetching}>
            <RefreshCw className={`h-4 w-4 mr-1.5 ${active.isFetching ? 'animate-spin' : ''}`} /> Yenile
          </Button>
        </div>
      </div>

      {/* Tabs */}
      <div className="flex flex-wrap gap-1 rounded-lg border border-border/50 bg-muted/30 p-1">
        {([['plans', 'Abonelik Planları', Tags], ['sms', 'SMS Paketleri', MessageSquare]] as const).map(([v, label, Icon]) => (
          <button
            key={v}
            onClick={() => setTab(v)}
            className={cn(
              'flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
              tab === v ? 'bg-primary text-primary-foreground shadow-sm' : 'text-muted-foreground hover:text-foreground hover:bg-accent'
            )}
          >
            <Icon className="h-4 w-4" /> {label}
          </button>
        ))}
      </div>

      {/* Stale-data warning: a background refetch failed but we still have the last-good list. */}
      {active.isError && active.data && (
        <p className="flex items-center gap-1.5 text-xs text-amber-400 -mt-2">
          <AlertTriangle className="h-3.5 w-3.5 flex-shrink-0" />
          Yenileme başarısız — liste son başarılı veriyi gösteriyor. {errorMessage(active.error, '')}
        </p>
      )}

      {/* Content */}
      {active.isLoading ? (
        <div className="space-y-3">
          {Array.from({ length: 3 }).map((_, i) => (
            <Card key={i} className="border-border/50"><CardContent className="p-5 space-y-3">
              <Skeleton className="h-5 w-1/3" /><Skeleton className="h-4 w-2/3" />
            </CardContent></Card>
          ))}
        </div>
      ) : active.isError && !active.data ? (
        // Only replace the screen with the error card when nothing is loaded yet; a failed background
        // "Yenile" keeps the last-good list visible with an inline warning (see banner above the list).
        <Card className="border-red-500/30"><CardContent className="p-8 text-center text-red-400">
          <AlertTriangle className="h-8 w-8 mx-auto mb-3" />
          <p className="font-medium">Fiyatlar yüklenemedi.</p>
          <p className="text-sm text-muted-foreground mt-1">{errorMessage(active.error, 'Liftdesk fiyat servisine bağlanılamadı.')}</p>
        </CardContent></Card>
      ) : tab === 'plans' ? (
        <PlansList plans={plans.data ?? []} onEdit={setEditPlan} />
      ) : (
        <SmsList
          packages={packages.data ?? []}
          onEdit={(pkg) => setSmsDialog({ mode: 'edit', pkg })}
          onDelete={setDeletePkg}
        />
      )}

      {/* Dialogs */}
      {editPlan && <PlanDialog plan={editPlan} onClose={() => setEditPlan(null)} />}
      {smsDialog?.mode === 'create' && <SmsPackageDialog onClose={() => setSmsDialog(null)} />}
      {smsDialog?.mode === 'edit' && <SmsPackageDialog pkg={smsDialog.pkg} onClose={() => setSmsDialog(null)} />}
      {deletePkg && <DeleteSmsDialog pkg={deletePkg} onClose={() => setDeletePkg(null)} />}
    </div>
  );
}

function EmptyState({ text }: { text: string }) {
  return (
    <Card className="border-border/50"><CardContent className="p-12 text-center text-muted-foreground">
      <Inbox className="h-10 w-10 mx-auto mb-3 opacity-40" />
      <p className="font-medium text-foreground">Kayıt yok</p>
      <p className="text-sm mt-1">{text}</p>
    </CardContent></Card>
  );
}

function PlansList({ plans, onEdit }: { plans: PricingPlan[]; onEdit: (p: PricingPlan) => void }) {
  if (plans.length === 0) return <EmptyState text="Liftdesk tarafında tanımlı abonelik planı bulunamadı." />;
  return (
    <div className="space-y-4">
      {plans.map((p) => (
        <Card key={p.id} className={cn('border-border/50', !p.isActive && 'opacity-60')}>
          <CardContent className="p-4 sm:p-5">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0 space-y-1.5">
                <div className="flex flex-wrap items-center gap-2">
                  {p.tier && <Pill className={TIER_CLASS[p.tier] ?? 'bg-slate-500/15 text-slate-400 border-slate-500/30'}>{p.tier}</Pill>}
                  <span className="text-base font-semibold text-foreground">{p.name}</span>
                  <ActivePill active={p.isActive} />
                </div>
                {p.description && <p className="text-sm text-muted-foreground">{p.description}</p>}
                <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm pt-1">
                  <span className="text-foreground font-medium">{formatTL(p.priceMonthly)}<span className="text-muted-foreground font-normal text-xs"> / ay</span></span>
                  <span className="text-foreground font-medium">{formatTL(p.priceYearly)}<span className="text-muted-foreground font-normal text-xs"> / yıl</span></span>
                  <span className="flex items-center gap-1 text-muted-foreground text-xs"><Users className="h-3.5 w-3.5" />{limitLabel(p.maxUsers)} kullanıcı</span>
                  <span className="flex items-center gap-1 text-muted-foreground text-xs"><Building2 className="h-3.5 w-3.5" />{limitLabel(p.maxElevators)} asansör</span>
                </div>
              </div>
              <Button variant="outline" size="sm" onClick={() => onEdit(p)}>
                <Pencil className="h-4 w-4 mr-1.5" /> Düzenle
              </Button>
            </div>
          </CardContent>
        </Card>
      ))}
    </div>
  );
}

function SmsList({
  packages, onEdit, onDelete,
}: { packages: SmsPackage[]; onEdit: (p: SmsPackage) => void; onDelete: (p: SmsPackage) => void }) {
  if (packages.length === 0) return <EmptyState text="Henüz SMS paketi tanımlanmamış. 'Yeni Paket' ile ekleyebilirsin." />;
  return (
    <div className="space-y-3">
      {packages.map((p) => (
        <Card key={p.id} className={cn('border-border/50', !p.isActive && 'opacity-60')}>
          <CardContent className="p-4 flex flex-wrap items-center justify-between gap-3">
            <div className="flex flex-wrap items-center gap-2 min-w-0">
              <span className="text-sm font-semibold text-foreground">{p.name}</span>
              <ActivePill active={p.isActive} />
              <span className="text-xs text-muted-foreground">{p.smsCount.toLocaleString('tr-TR')} SMS</span>
            </div>
            <div className="flex items-center gap-3">
              <span className="text-sm font-medium text-foreground">{formatTL(p.price)}</span>
              <Button variant="outline" size="sm" onClick={() => onEdit(p)}>
                <Pencil className="h-4 w-4 sm:mr-1.5" /><span className="hidden sm:inline">Düzenle</span>
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={() => onDelete(p)}
                className="border-red-500/30 text-red-400 hover:bg-red-500/10 hover:text-red-300"
              >
                <Trash2 className="h-4 w-4" />
              </Button>
            </div>
          </CardContent>
        </Card>
      ))}
    </div>
  );
}
