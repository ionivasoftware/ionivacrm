import { useState } from 'react';
import {
  AlertTriangle, ArrowRight, BadgeCheck, CalendarClock, Check, Info, Loader2, Package, RefreshCw,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from '@/components/ui/dialog';
import {
  useCustomerPlan, useUpdateCustomerPlan,
  type CompanyAvailablePlan, type CompanyPlan,
} from '@/api/customers';
import { useToast } from '@/hooks/use-toast';
import { useAuthStore } from '@/stores/authStore';
import { cn } from '@/lib/utils';

// ── Config ──────────────────────────────────────────────────────────────────

const STATUS_LABEL: Record<string, { label: string; className: string }> = {
  Trialing:       { label: 'Deneme',          className: 'bg-blue-500/15 text-blue-400 border-blue-500/30' },
  Active:         { label: 'Aktif',           className: 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30' },
  PendingPayment: { label: 'Ödeme Bekliyor',  className: 'bg-amber-500/15 text-amber-400 border-amber-500/30' },
  Cancelled:      { label: 'İptal',           className: 'bg-red-500/15 text-red-400 border-red-500/30' },
  Expired:        { label: 'Süresi Doldu',    className: 'bg-rose-500/15 text-rose-400 border-rose-500/30' },
};

const PERIOD_LABEL: Record<string, string> = { Monthly: 'Aylık', Yearly: 'Yıllık' };

type PeriodChoice = 'keep' | 'Monthly' | 'Yearly';

// ── Helpers ─────────────────────────────────────────────────────────────────

function formatDate(iso: string | null) {
  if (!iso) return '—';
  return new Date(iso).toLocaleDateString('tr-TR', { day: '2-digit', month: '2-digit', year: 'numeric' });
}

function formatPrice(value: number) {
  return value.toLocaleString('tr-TR', { style: 'currency', currency: 'TRY', minimumFractionDigits: 0 });
}

function extractApiError(err: unknown): string {
  return (
    (err as { response?: { data?: { errors?: string[] } } })?.response?.data?.errors?.[0] ??
    (err as { response?: { data?: { message?: string } } })?.response?.data?.message ??
    (err as Error)?.message ??
    'Bilinmeyen hata'
  );
}

// ── Confirm dialog ────────────────────────────────────────────────────────────

function ConfirmPlanChangeDialog({
  customerId,
  plan,
  target,
  onClose,
}: {
  customerId: string;
  plan: CompanyPlan;
  target: CompanyAvailablePlan;
  onClose: () => void;
}) {
  const { toast } = useToast();
  const updatePlan = useUpdateCustomerPlan(customerId);
  const [period, setPeriod] = useState<PeriodChoice>('keep');

  const current = plan.current;
  const isDowngrade =
    !!current &&
    ['Standart', 'Pro', 'Prime'].indexOf(target.tier) < ['Standart', 'Pro', 'Prime'].indexOf(current.tier);

  async function handleConfirm() {
    try {
      await updatePlan.mutateAsync({
        planId: target.planId,
        billingPeriod: period === 'keep' ? undefined : period,
      });
      toast({
        title: 'Paket değiştirildi',
        description: `${current?.name ?? 'Tanımsız'} → ${target.name}`,
      });
      onClose();
    } catch (err) {
      toast({ title: 'Paket değiştirilemedi', description: extractApiError(err), variant: 'destructive' });
    }
  }

  const busy = updatePlan.isPending;

  return (
    <Dialog open onOpenChange={(open) => !open && !busy && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Package className="h-5 w-5" /> Paketi Değiştir
          </DialogTitle>
          <DialogDescription>
            Bu değişiklik <span className="font-semibold text-foreground">anında</span> geçerli olur ve
            firmanın özellik erişimini belirler.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          {/* Before → after */}
          <div className="flex items-center gap-3 rounded-lg border border-border p-3">
            <div className="min-w-0 flex-1">
              <p className="text-xs text-muted-foreground">Mevcut</p>
              <p className="font-medium text-foreground truncate">{current?.name ?? 'Paket tanımsız'}</p>
            </div>
            <ArrowRight className="h-4 w-4 text-muted-foreground flex-shrink-0" />
            <div className="min-w-0 flex-1">
              <p className="text-xs text-muted-foreground">Yeni</p>
              <p className="font-medium text-primary truncate">{target.name}</p>
              <p className="text-xs text-muted-foreground">
                {formatPrice(target.priceMonthly)}/ay · {formatPrice(target.priceYearly)}/yıl
              </p>
            </div>
          </div>

          {/* Billing period */}
          <div className="space-y-1.5">
            <p className="text-xs font-medium text-muted-foreground">Ödeme dönemi</p>
            <div className="flex flex-wrap gap-2">
              {([
                { value: 'keep' as PeriodChoice, label: current?.billingPeriod
                    ? `Değiştirme (${PERIOD_LABEL[current.billingPeriod] ?? current.billingPeriod})`
                    : 'Değiştirme' },
                { value: 'Monthly' as PeriodChoice, label: 'Aylık' },
                { value: 'Yearly' as PeriodChoice, label: 'Yıllık' },
              ]).map((opt) => (
                <button
                  key={opt.value}
                  type="button"
                  onClick={() => setPeriod(opt.value)}
                  className={cn(
                    'rounded-md border px-3 py-1.5 text-sm transition-colors',
                    period === opt.value
                      ? 'border-primary bg-primary/10 text-foreground'
                      : 'border-border text-muted-foreground hover:text-foreground'
                  )}
                >
                  {opt.label}
                </button>
              ))}
            </div>
          </div>

          {/* Always-true consequence: the licence period is untouched */}
          <div className="flex items-start gap-2 rounded-lg border border-blue-500/30 bg-blue-500/5 p-3">
            <Info className="h-4 w-4 text-blue-400 flex-shrink-0 mt-0.5" />
            <p className="text-xs text-blue-300">
              Paket değişikliği lisans <span className="font-semibold">süresini ve durumunu değiştirmez</span>.
              {current?.status === 'Expired' && ' Bu firmanın süresi dolmuş — paket değişse de görüntüleme modunda kalır; ayrıca “Süre Uzat” gerekir.'}
            </p>
          </div>

          {/* Downgrade note — reassure, since it sounds destructive but is not */}
          {isDowngrade && (
            <div className="flex items-start gap-2 rounded-lg border border-amber-500/30 bg-amber-500/5 p-3">
              <AlertTriangle className="h-4 w-4 text-amber-400 flex-shrink-0 mt-0.5" />
              <p className="text-xs text-amber-300">
                Paket düşürülüyor. Veri silinmez ve kullanıcılar kilitlenmez; yalnız üst kademeye ait
                ekranlar kapanır.
              </p>
            </div>
          )}

          {/* Revenue risk — the tenant keeps being charged the old amount by iyzico */}
          {plan.warning && (
            <div className="flex items-start gap-2 rounded-lg border border-red-500/30 bg-red-500/5 p-3">
              <AlertTriangle className="h-4 w-4 text-red-400 flex-shrink-0 mt-0.5" />
              <p className="text-xs text-red-300">{plan.warning}</p>
            </div>
          )}
        </div>

        <DialogFooter>
          <Button variant="ghost" onClick={onClose} disabled={busy}>İptal</Button>
          <Button onClick={handleConfirm} disabled={busy}>
            {busy
              ? <><Loader2 className="h-4 w-4 mr-1.5 animate-spin" />Uygulanıyor...</>
              : <><Check className="h-4 w-4 mr-1.5" />Paketi Değiştir</>}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Main tab ──────────────────────────────────────────────────────────────────

export function PlanTab({ customerId }: { customerId: string }) {
  const { user } = useAuthStore();
  const isSuperAdmin = user?.isSuperAdmin ?? false;
  const { data, isLoading, isFetching, isError, error, refetch } = useCustomerPlan(customerId, true);
  const [target, setTarget] = useState<CompanyAvailablePlan | null>(null);

  if (isLoading) {
    return (
      <div className="space-y-3">
        <Skeleton className="h-24 rounded-lg" />
        <Skeleton className="h-40 rounded-lg" />
      </div>
    );
  }

  if (isError || !data) {
    return (
      <div className="flex flex-col items-center justify-center py-16 text-center gap-2">
        <AlertTriangle className="h-9 w-9 text-muted-foreground/40" />
        <p className="text-sm font-medium text-muted-foreground">Paket bilgisi alınamadı</p>
        <p className="text-xs text-muted-foreground/70 max-w-sm">{extractApiError(error)}</p>
      </div>
    );
  }

  const current = data.current;
  const statusCfg = current ? STATUS_LABEL[current.status] : undefined;

  return (
    <div className="space-y-4">
      {/* Header actions */}
      <div className="flex items-center justify-between gap-2">
        <p className="text-sm text-muted-foreground">
          Paket kademesi firmanın özellik erişimini belirler; değişiklik anında geçerli olur.
        </p>
        <Button variant="outline" size="sm" onClick={() => refetch()} disabled={isFetching}>
          <RefreshCw className={cn('h-4 w-4 mr-1.5', isFetching && 'animate-spin')} /> Yenile
        </Button>
      </div>

      {/* iyzico / operator warning */}
      {data.warning && (
        <div className="flex items-start gap-2 rounded-lg border border-amber-500/30 bg-amber-500/5 p-3">
          <AlertTriangle className="h-4 w-4 text-amber-400 flex-shrink-0 mt-0.5" />
          <p className="text-xs text-amber-300">{data.warning}</p>
        </div>
      )}

      {/* Current plan */}
      <div className="rounded-lg border border-border p-4">
        <p className="flex items-center gap-1.5 text-xs font-semibold text-muted-foreground uppercase tracking-wider mb-3">
          <BadgeCheck className="h-3.5 w-3.5" /> Mevcut Paket
        </p>

        {current ? (
          <div className="flex flex-wrap items-start gap-x-8 gap-y-3">
            <div>
              <p className="text-xl font-bold text-foreground">{current.name}</p>
              <div className="mt-1 flex flex-wrap items-center gap-2">
                <span className="inline-flex items-center rounded-full border px-2 py-0.5 text-xs font-semibold border-primary/40 bg-primary/10 text-primary">
                  {current.tier}
                </span>
                {statusCfg && (
                  <span className={cn('inline-flex items-center rounded-full border px-2 py-0.5 text-xs font-semibold', statusCfg.className)}>
                    {statusCfg.label}
                  </span>
                )}
              </div>
            </div>
            <div className="text-sm">
              <p className="text-xs text-muted-foreground">Ödeme dönemi</p>
              <p className="font-medium text-foreground">
                {current.billingPeriod ? PERIOD_LABEL[current.billingPeriod] ?? current.billingPeriod : '—'}
                {current.autoRenew && <span className="ml-1.5 text-xs text-emerald-400">otomatik yenileme açık</span>}
              </p>
            </div>
            <div className="text-sm">
              <p className="text-xs text-muted-foreground flex items-center gap-1">
                <CalendarClock className="h-3 w-3" /> Lisans
              </p>
              <p className="font-medium text-foreground tabular-nums">
                {formatDate(current.startDate)} – {formatDate(current.endDate)}
              </p>
            </div>
          </div>
        ) : (
          <div className="space-y-1">
            <p className="font-medium text-foreground">Paket tanımsız</p>
            <p className="text-sm text-muted-foreground">
              Bu firmanın abonelik kaydı yok. Paket değiştirmeden önce “Süre Uzat” ile lisans süresi
              tanımlanmalı.
            </p>
          </div>
        )}
      </div>

      {/* Available plans */}
      <div className="space-y-2">
        <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
          Seçilebilir Paketler
        </p>

        {data.availablePlans.length === 0 ? (
          <p className="text-sm text-muted-foreground">Satışta paket bulunmuyor.</p>
        ) : (
          <div className="grid gap-3 sm:grid-cols-3">
            {data.availablePlans.map((p) => {
              const isCurrent = current?.planId === p.planId;
              return (
                <div
                  key={p.planId}
                  className={cn(
                    'rounded-lg border p-4 flex flex-col gap-3',
                    isCurrent ? 'border-primary bg-primary/5' : 'border-border'
                  )}
                >
                  <div>
                    <p className="font-semibold text-foreground">{p.name}</p>
                    <p className="text-xs text-muted-foreground">{p.tier}</p>
                  </div>
                  <div className="text-sm">
                    <p className="font-medium text-foreground tabular-nums">{formatPrice(p.priceMonthly)}<span className="text-xs text-muted-foreground">/ay</span></p>
                    <p className="text-xs text-muted-foreground tabular-nums">{formatPrice(p.priceYearly)}/yıl</p>
                  </div>

                  {isCurrent ? (
                    <span className="mt-auto inline-flex items-center gap-1 text-xs font-medium text-primary">
                      <Check className="h-3.5 w-3.5" /> Kullanımda
                    </span>
                  ) : isSuperAdmin ? (
                    <Button
                      size="sm"
                      variant="outline"
                      className="mt-auto"
                      // No subscription row → Liftdesk would 409; block it here with a clear reason.
                      disabled={!current}
                      title={!current ? 'Önce “Süre Uzat” ile lisans süresi tanımlayın' : undefined}
                      onClick={() => setTarget(p)}
                    >
                      Bu pakete geç
                    </Button>
                  ) : null}
                </div>
              );
            })}
          </div>
        )}

        {!isSuperAdmin && (
          <p className="text-xs text-muted-foreground">
            Paket değiştirme yetkisi yalnız SuperAdmin kullanıcılardadır.
          </p>
        )}
      </div>

      {target && (
        <ConfirmPlanChangeDialog
          customerId={customerId}
          plan={data}
          target={target}
          onClose={() => setTarget(null)}
        />
      )}
    </div>
  );
}
