import { useEffect, useState } from 'react';
import { Loader2, Save, AlertTriangle, Settings2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { Skeleton } from '@/components/ui/skeleton';
import {
  useCustomerRezervalSettings,
  useUpdateCustomerRezervalSettings,
} from '@/api/customers';
import { useToast } from '@/hooks/use-toast';
import { cn } from '@/lib/utils';
import type { UpdateRezervalReservationSettingRequest } from '@/types';

// ── Form state (all fields optional; null → not loaded yet from API) ─────────

type FormState = {
  isAcceptWithoutPhone: boolean;
  isRequireConfirm: boolean;
  isSendConfirmSameDayReservations: boolean;
  confirmSmsSetting: boolean;
  confirmSmsHour: number;
  reviewSmsSetting: boolean;
  reviewSmsHour: number;
  isEnterAccountClosingInfo: boolean;
  isOtoTableAppoint: boolean;
  isSendRegisterSms: boolean;
  isSendRegisterMinute: number;
  smsTextRegister: string;
  smsTextConfirm: string;
  smsTextReview: string;
  reviewGoogleLink: string;
};

const emptyForm: FormState = {
  isAcceptWithoutPhone: false,
  isRequireConfirm: false,
  isSendConfirmSameDayReservations: false,
  confirmSmsSetting: false,
  confirmSmsHour: 0,
  reviewSmsSetting: false,
  reviewSmsHour: 0,
  isEnterAccountClosingInfo: false,
  isOtoTableAppoint: false,
  isSendRegisterSms: false,
  isSendRegisterMinute: 0,
  smsTextRegister: '',
  smsTextConfirm: '',
  smsTextReview: '',
  reviewGoogleLink: '',
};

export function RezervalSettingsTab({ customerId }: { customerId: string }) {
  const { toast } = useToast();
  const { data, isLoading, error, refetch } = useCustomerRezervalSettings(customerId, true);
  const updateMutation = useUpdateCustomerRezervalSettings(customerId);

  const [form, setForm] = useState<FormState>(emptyForm);

  // Hydrate form once data arrives.
  useEffect(() => {
    if (!data) return;
    setForm({
      isAcceptWithoutPhone:             data.isAcceptWithoutPhone ?? false,
      isRequireConfirm:                 data.isRequireConfirm ?? false,
      isSendConfirmSameDayReservations: data.isSendConfirmSameDayReservations ?? false,
      confirmSmsSetting:                data.confirmSmsSetting ?? false,
      confirmSmsHour:                   data.confirmSmsHour ?? 0,
      reviewSmsSetting:                 data.reviewSmsSetting ?? false,
      reviewSmsHour:                    data.reviewSmsHour ?? 0,
      isEnterAccountClosingInfo:        data.isEnterAccountClosingInfo ?? false,
      isOtoTableAppoint:                data.isOtoTableAppoint ?? false,
      isSendRegisterSms:                data.isSendRegisterSms ?? false,
      isSendRegisterMinute:             data.isSendRegisterMinute ?? 0,
      smsTextRegister:                  data.smsTextRegister ?? '',
      smsTextConfirm:                   data.smsTextConfirm ?? '',
      smsTextReview:                    data.smsTextReview ?? '',
      reviewGoogleLink:                 data.reviewGoogleLink ?? '',
    });
  }, [data]);

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((prev) => ({ ...prev, [key]: value }));
  }

  async function handleSave() {
    const payload: UpdateRezervalReservationSettingRequest = { ...form };
    try {
      const msg = await updateMutation.mutateAsync(payload);
      toast({ title: 'Ayarlar güncellendi', description: msg ?? 'Rezervasyon ayarı kaydedildi.' });
      await refetch();
    } catch (err) {
      const errMsg =
        (err as { response?: { data?: { errors?: string[]; message?: string } } })?.response?.data?.errors?.[0]
        ?? (err as { response?: { data?: { message?: string } } })?.response?.data?.message
        ?? (err as Error)?.message
        ?? 'Bilinmeyen hata';
      toast({ title: 'Güncellenemedi', description: errMsg, variant: 'destructive' });
    }
  }

  if (isLoading) {
    return (
      <div className="space-y-3">
        <Skeleton className="h-24 rounded-lg" />
        <Skeleton className="h-48 rounded-lg" />
        <Skeleton className="h-48 rounded-lg" />
      </div>
    );
  }

  if (!data) {
    return (
      <div className="flex flex-col items-center justify-center py-16 text-center gap-2">
        <AlertTriangle className="h-9 w-9 text-muted-foreground/40" />
        <p className="text-sm font-medium text-muted-foreground">Rezervasyon ayarları alınamadı</p>
        {error != null && (
          <p className="text-xs text-muted-foreground/70 max-w-sm">
            {(error as { response?: { data?: { errors?: string[] } } })?.response?.data?.errors?.[0]
              ?? (error as Error)?.message
              ?? 'Bilinmeyen hata'}
          </p>
        )}
      </div>
    );
  }

  return (
    <div className="space-y-5">
      <div className="flex items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <div className="w-8 h-8 rounded-lg bg-teal-500/10 flex items-center justify-center">
            <Settings2 className="h-4 w-4 text-teal-600" />
          </div>
          <div>
            <p className="text-sm font-medium">RezervAl Rezervasyon Ayarları</p>
            <p className="text-xs text-muted-foreground">
              RezervAl şirket #{data.companyId} · Değişiklikler doğrudan RezervAl üzerine yazılır.
            </p>
          </div>
        </div>
        <Button
          onClick={handleSave}
          disabled={updateMutation.isPending}
          className="h-10 bg-teal-600 hover:bg-teal-700"
        >
          {updateMutation.isPending
            ? <Loader2 className="h-4 w-4 animate-spin mr-2" />
            : <Save className="h-4 w-4 mr-2" />}
          Kaydet
        </Button>
      </div>

      {/* ── Rezervasyon davranışı ── */}
      <Section title="Rezervasyon Davranışı">
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-x-6 gap-y-2">
          <Toggle
            label="Telefonsuz rezervasyon kabul et"
            checked={form.isAcceptWithoutPhone}
            onChange={(v) => set('isAcceptWithoutPhone', v)}
          />
          <Toggle
            label="Rezervasyon teyidi gerekli"
            checked={form.isRequireConfirm}
            onChange={(v) => set('isRequireConfirm', v)}
          />
          <Toggle
            label="Aynı gün teyit SMS'i"
            checked={form.isSendConfirmSameDayReservations}
            onChange={(v) => set('isSendConfirmSameDayReservations', v)}
          />
          <Toggle
            label="Hesap kapatma bilgisi"
            checked={form.isEnterAccountClosingInfo}
            onChange={(v) => set('isEnterAccountClosingInfo', v)}
          />
          <Toggle
            label="Otomatik masa ataması"
            checked={form.isOtoTableAppoint}
            onChange={(v) => set('isOtoTableAppoint', v)}
          />
        </div>
      </Section>

      {/* ── SMS Ayarları ── */}
      <Section title="SMS Ayarları">
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          <div className="space-y-2">
            <Toggle
              label="Teyit SMS'i"
              checked={form.confirmSmsSetting}
              onChange={(v) => set('confirmSmsSetting', v)}
            />
            <NumberField
              label="Kaç saat önce"
              value={form.confirmSmsHour}
              onChange={(v) => set('confirmSmsHour', v)}
              min={0}
              disabled={!form.confirmSmsSetting}
              compact
            />
          </div>
          <div className="space-y-2">
            <Toggle
              label="Değerlendirme SMS'i"
              checked={form.reviewSmsSetting}
              onChange={(v) => set('reviewSmsSetting', v)}
            />
            <NumberField
              label="Kaç saat sonra"
              value={form.reviewSmsHour}
              onChange={(v) => set('reviewSmsHour', v)}
              min={0}
              disabled={!form.reviewSmsSetting}
              compact
            />
          </div>
          <div className="space-y-2">
            <Toggle
              label="Kayıt SMS'i"
              checked={form.isSendRegisterSms}
              onChange={(v) => set('isSendRegisterSms', v)}
            />
            <NumberField
              label="Gecikme (dakika)"
              value={form.isSendRegisterMinute}
              onChange={(v) => set('isSendRegisterMinute', v)}
              min={0}
              disabled={!form.isSendRegisterSms}
              compact
            />
          </div>
        </div>
      </Section>

      {/* ── SMS metinleri ── */}
      <Section
        title="SMS Metinleri"
        hint="{reservationDate}, {userCount}, {formLink} gibi değişkenler kullanılabilir."
      >
        <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
          <div className="space-y-1.5">
            <Label>Kayıt SMS metni</Label>
            <Textarea
              value={form.smsTextRegister}
              onChange={(e) => set('smsTextRegister', e.target.value)}
              rows={5}
            />
          </div>
          <div className="space-y-1.5">
            <Label>Teyit SMS metni</Label>
            <Textarea
              value={form.smsTextConfirm}
              onChange={(e) => set('smsTextConfirm', e.target.value)}
              rows={5}
            />
          </div>
          <div className="space-y-1.5">
            <Label>Değerlendirme SMS metni</Label>
            <Textarea
              value={form.smsTextReview}
              onChange={(e) => set('smsTextReview', e.target.value)}
              rows={5}
            />
          </div>
        </div>
        <div className="space-y-1.5">
          <Label>Google değerlendirme bağlantısı</Label>
          <Input
            type="url"
            placeholder="https://g.page/r/..."
            value={form.reviewGoogleLink}
            onChange={(e) => set('reviewGoogleLink', e.target.value)}
          />
        </div>
      </Section>

      <div className="flex justify-end pt-2">
        <Button
          onClick={handleSave}
          disabled={updateMutation.isPending}
          className="h-10 bg-teal-600 hover:bg-teal-700 min-w-[140px]"
        >
          {updateMutation.isPending
            ? <Loader2 className="h-4 w-4 animate-spin mr-2" />
            : <Save className="h-4 w-4 mr-2" />}
          Kaydet
        </Button>
      </div>
    </div>
  );
}

// ── Presentational helpers ───────────────────────────────────────────────────

function Section({
  title,
  hint,
  children,
}: {
  title: string;
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="rounded-lg border bg-card p-4 space-y-4">
      <div>
        <p className="text-sm font-semibold">{title}</p>
        {hint && <p className="text-xs text-muted-foreground mt-0.5">{hint}</p>}
      </div>
      <div className="space-y-3">{children}</div>
    </div>
  );
}

function Toggle({
  label,
  hint,
  checked,
  onChange,
}: {
  label: string;
  hint?: string;
  checked: boolean;
  onChange: (value: boolean) => void;
}) {
  return (
    <label className="flex items-center justify-between gap-3 cursor-pointer py-1.5">
      <div className="flex-1 min-w-0">
        <p className="text-sm leading-tight">{label}</p>
        {hint && <p className="text-xs text-muted-foreground mt-0.5">{hint}</p>}
      </div>
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        onClick={() => onChange(!checked)}
        className={cn(
          'relative inline-flex h-5 w-9 shrink-0 cursor-pointer rounded-full border border-border/50 !min-h-[22px]',
          'transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring',
          checked ? 'bg-teal-600 border-teal-600' : 'bg-slate-300 dark:bg-slate-600'
        )}
      >
        <span
          className={cn(
            'pointer-events-none inline-block h-4 w-4 transform rounded-full bg-white shadow ring-0 transition-transform mt-[1px]',
            checked ? 'translate-x-[18px]' : 'translate-x-0.5'
          )}
        />
      </button>
    </label>
  );
}

function NumberField({
  label,
  value,
  onChange,
  min,
  disabled,
  hint,
  compact,
}: {
  label: string;
  value: number;
  onChange: (v: number) => void;
  min?: number;
  disabled?: boolean;
  hint?: string;
  compact?: boolean;
}) {
  return (
    <div className="space-y-1.5">
      <Label className="text-xs">{label}</Label>
      <Input
        type="number"
        min={min}
        value={Number.isFinite(value) ? value : 0}
        onChange={(e) => {
          const n = Number(e.target.value);
          onChange(Number.isFinite(n) ? n : 0);
        }}
        disabled={disabled}
        className={cn(compact ? 'h-9' : 'h-10', !compact && 'max-w-[220px]')}
      />
      {hint && <p className="text-xs text-muted-foreground">{hint}</p>}
    </div>
  );
}
