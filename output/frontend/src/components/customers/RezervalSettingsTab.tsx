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
  preparationTime: number;
  notSendSmsMinHourId: number;
  notSendSmsMaxHourId: number;
  isEnterAccountClosingInfo: boolean;
  isOtoTableAppoint: boolean;
  isSendReservationSms: boolean;
  isSendNotification: boolean;
  isSendReservationNotification: boolean;
  isSendCancelNotification: boolean;
  isSendConfirmNotification: boolean;
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
  preparationTime: 0,
  notSendSmsMinHourId: 0,
  notSendSmsMaxHourId: 0,
  isEnterAccountClosingInfo: false,
  isOtoTableAppoint: false,
  isSendReservationSms: false,
  isSendNotification: false,
  isSendReservationNotification: false,
  isSendCancelNotification: false,
  isSendConfirmNotification: false,
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
      preparationTime:                  data.preparationTime ?? 0,
      notSendSmsMinHourId:              data.notSendSmsMinHourId ?? 0,
      notSendSmsMaxHourId:              data.notSendSmsMaxHourId ?? 0,
      isEnterAccountClosingInfo:        data.isEnterAccountClosingInfo ?? false,
      isOtoTableAppoint:                data.isOtoTableAppoint ?? false,
      isSendReservationSms:             data.isSendReservationSms ?? false,
      isSendNotification:               data.isSendNotification ?? false,
      isSendReservationNotification:    data.isSendReservationNotification ?? false,
      isSendCancelNotification:         data.isSendCancelNotification ?? false,
      isSendConfirmNotification:        data.isSendConfirmNotification ?? false,
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
        <Toggle
          label="Telefonsuz rezervasyon kabul et"
          hint="Müşteri telefon numarası girmese bile rezervasyon oluşturulabilir."
          checked={form.isAcceptWithoutPhone}
          onChange={(v) => set('isAcceptWithoutPhone', v)}
        />
        <Toggle
          label="Rezervasyon teyidi gerekli"
          hint="Misafir formdan katılım durumunu teyit etmezse rezervasyon iptal sayılır."
          checked={form.isRequireConfirm}
          onChange={(v) => set('isRequireConfirm', v)}
        />
        <Toggle
          label="Aynı gün rezervasyonlara teyit SMS'i gönder"
          checked={form.isSendConfirmSameDayReservations}
          onChange={(v) => set('isSendConfirmSameDayReservations', v)}
        />
        <Toggle
          label="Hesap kapatma bilgisi gir"
          checked={form.isEnterAccountClosingInfo}
          onChange={(v) => set('isEnterAccountClosingInfo', v)}
        />
        <Toggle
          label="Otomatik masa ataması"
          hint="Yeni rezervasyonlar uygun masaya otomatik atanır."
          checked={form.isOtoTableAppoint}
          onChange={(v) => set('isOtoTableAppoint', v)}
        />

        <NumberField
          label="Hazırlık süresi (dakika)"
          value={form.preparationTime}
          onChange={(v) => set('preparationTime', v)}
          min={0}
        />
      </Section>

      {/* ── SMS ── */}
      <Section title="SMS Ayarları">
        <Toggle
          label="Rezervasyon SMS'i gönder"
          hint="Yeni rezervasyon oluştuğunda misafire SMS gönderilir."
          checked={form.isSendReservationSms}
          onChange={(v) => set('isSendReservationSms', v)}
        />
        <Toggle
          label="Teyit SMS'i gönder"
          checked={form.confirmSmsSetting}
          onChange={(v) => set('confirmSmsSetting', v)}
        />
        <NumberField
          label="Teyit SMS'i kaç saat önce"
          value={form.confirmSmsHour}
          onChange={(v) => set('confirmSmsHour', v)}
          min={0}
          disabled={!form.confirmSmsSetting}
        />

        <Toggle
          label="Değerlendirme SMS'i gönder"
          checked={form.reviewSmsSetting}
          onChange={(v) => set('reviewSmsSetting', v)}
        />
        <NumberField
          label="Değerlendirme SMS'i kaç saat sonra"
          value={form.reviewSmsHour}
          onChange={(v) => set('reviewSmsHour', v)}
          min={0}
          disabled={!form.reviewSmsSetting}
        />

        <Toggle
          label="Kayıt SMS'i gönder"
          checked={form.isSendRegisterSms}
          onChange={(v) => set('isSendRegisterSms', v)}
        />
        <NumberField
          label="Kayıt SMS'i gecikmesi (dakika)"
          value={form.isSendRegisterMinute}
          onChange={(v) => set('isSendRegisterMinute', v)}
          min={0}
          disabled={!form.isSendRegisterSms}
        />

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <NumberField
            label="SMS gönderme min. saat ID"
            value={form.notSendSmsMinHourId}
            onChange={(v) => set('notSendSmsMinHourId', v)}
            min={0}
            hint="RezervAl saat aralığı ID'si (ör. 43)"
          />
          <NumberField
            label="SMS gönderme max. saat ID"
            value={form.notSendSmsMaxHourId}
            onChange={(v) => set('notSendSmsMaxHourId', v)}
            min={0}
            hint="RezervAl saat aralığı ID'si (ör. 51)"
          />
        </div>
      </Section>

      {/* ── Bildirimler ── */}
      <Section title="Bildirimler">
        <Toggle
          label="Genel bildirim gönder"
          checked={form.isSendNotification}
          onChange={(v) => set('isSendNotification', v)}
        />
        <Toggle
          label="Yeni rezervasyon bildirimi"
          checked={form.isSendReservationNotification}
          onChange={(v) => set('isSendReservationNotification', v)}
        />
        <Toggle
          label="İptal bildirimi"
          checked={form.isSendCancelNotification}
          onChange={(v) => set('isSendCancelNotification', v)}
        />
        <Toggle
          label="Teyit bildirimi"
          checked={form.isSendConfirmNotification}
          onChange={(v) => set('isSendConfirmNotification', v)}
        />
      </Section>

      {/* ── SMS metinleri ── */}
      <Section
        title="SMS Metinleri"
        hint="{reservationDate}, {userCount}, {formLink} gibi değişkenler kullanılabilir."
      >
        <div className="space-y-1.5">
          <Label>Kayıt SMS metni</Label>
          <Textarea
            value={form.smsTextRegister}
            onChange={(e) => set('smsTextRegister', e.target.value)}
            rows={4}
          />
        </div>
        <div className="space-y-1.5">
          <Label>Teyit SMS metni</Label>
          <Textarea
            value={form.smsTextConfirm}
            onChange={(e) => set('smsTextConfirm', e.target.value)}
            rows={4}
          />
        </div>
        <div className="space-y-1.5">
          <Label>Değerlendirme SMS metni</Label>
          <Textarea
            value={form.smsTextReview}
            onChange={(e) => set('smsTextReview', e.target.value)}
            rows={4}
          />
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
    <label className="flex items-start justify-between gap-3 cursor-pointer group">
      <div className="flex-1 min-w-0">
        <p className="text-sm font-medium">{label}</p>
        {hint && <p className="text-xs text-muted-foreground mt-0.5">{hint}</p>}
      </div>
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        onClick={() => onChange(!checked)}
        className={cn(
          'relative inline-flex h-6 w-11 shrink-0 cursor-pointer rounded-full border-2 border-transparent',
          'transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring',
          checked ? 'bg-teal-600' : 'bg-muted'
        )}
      >
        <span
          className={cn(
            'pointer-events-none inline-block h-5 w-5 transform rounded-full bg-background shadow ring-0 transition-transform',
            checked ? 'translate-x-5' : 'translate-x-0'
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
}: {
  label: string;
  value: number;
  onChange: (v: number) => void;
  min?: number;
  disabled?: boolean;
  hint?: string;
}) {
  return (
    <div className="space-y-1.5">
      <Label>{label}</Label>
      <Input
        type="number"
        min={min}
        value={Number.isFinite(value) ? value : 0}
        onChange={(e) => {
          const n = Number(e.target.value);
          onChange(Number.isFinite(n) ? n : 0);
        }}
        disabled={disabled}
        className="h-10 max-w-[220px]"
      />
      {hint && <p className="text-xs text-muted-foreground">{hint}</p>}
    </div>
  );
}
