import { useEffect, useMemo, useRef, useState } from 'react';
import {
  Megaphone, Save, RotateCcw, Upload, Trash2, Copy, Check, Loader2, AlertTriangle, Send, CheckCircle2, Info, ImageOff,
} from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '@/components/ui/dialog';
import { cn } from '@/lib/utils';
import { useToast } from '@/hooks/use-toast';
import {
  useSocialProfiles, useUpdateSocialProfile, useResetSocialProfile, useUploadSocialProfileImage, useDeleteSocialProfileImage,
  useApplySocialProfile, useMarkSocialProfileApplied, type SocialProfile,
} from '@/api/socialProfiles';
import { SocialNav, ProviderIcon, fmtDateTime } from './socialShared';

// ── Alan şeması (anahtarlar sunucudaki SocialProfileDefaults ile birebir) ─────

interface FieldDef { key: string; label: string; help?: string; max?: number; multiline?: boolean; rows?: number; manual?: boolean }

const FACEBOOK_FIELDS: FieldDef[] = [
  { key: 'name', label: 'Sayfa adı', manual: true, help: 'API ile değiştirilemez; Sayfa ayarlarından.' },
  { key: 'username', label: 'Kullanıcı adı (@)', manual: true, help: 'Sayfa → Ayarlar → Kullanıcı adı.' },
  { key: 'category', label: 'Kategori', manual: true, help: 'Sayfa → Hakkında → Kategori.' },
  { key: 'about', label: 'Hakkında (kısa)', max: 255, multiline: true, rows: 3, help: 'Sayfanın üst kısmında görünen kısa tanıtım.' },
  { key: 'description', label: 'Açıklama (uzun)', multiline: true, rows: 12, help: 'Hakkında sekmesindeki tam metin.' },
  { key: 'website', label: 'Web sitesi' },
  { key: 'phone', label: 'Telefon', help: 'Boş bırakılırsa sayfaya basılmaz.' },
  { key: 'email', label: 'E-posta', help: 'Boş bırakılırsa sayfaya basılmaz.' },
  { key: 'address', label: 'Adres', manual: true },
  { key: 'cta', label: 'Eylem düğmesi', manual: true, multiline: true, rows: 2 },
  { key: 'cover_text', label: 'Kapak görseli notu', manual: true, multiline: true, rows: 3, help: 'Tasarımcı / kapak için metin önerisi.' },
];

const INSTAGRAM_FIELDS: FieldDef[] = [
  { key: 'name', label: 'Ad', max: 64 },
  { key: 'username', label: 'Kullanıcı adı' },
  { key: 'category', label: 'Kategori' },
  { key: 'bio', label: 'Bio', max: 150, multiline: true, rows: 4, help: 'Satır sonları korunur; 150 karakter sınırı.' },
  { key: 'website', label: 'Web sitesi' },
  { key: 'contact_email', label: 'İletişim e-postası' },
  { key: 'cta', label: 'Eylem düğmeleri / hesap türü', multiline: true, rows: 2 },
  { key: 'highlights', label: 'Öne çıkanlar önerisi', multiline: true, rows: 2 },
  { key: 'hashtags', label: 'Sabit etiket seti', multiline: true, rows: 2, help: 'Gönderi ajanına da referans.' },
];

const IMAGE_SPECS: Record<string, { profile: string; cover?: string }> = {
  Facebook: { profile: 'Profil fotoğrafı: en az 320×320 px, kare; yuvarlak kırpılır. Logo önerilir.', cover: 'Kapak: 820×312 px (masaüstü), mobilde orta 640×312 güvenli alan. API ile ayarlanamaz, sayfadan elle yüklenir.' },
  Instagram: { profile: 'Profil fotoğrafı: 320×320 px, kare; yuvarlak kırpılır. API ile ayarlanamaz, uygulamadan yüklenir.' },
};

function errorMessage(err: unknown, fallback: string): string {
  if (err && typeof err === 'object' && 'response' in err && err.response && typeof err.response === 'object' && 'data' in err.response) {
    const data = (err.response as { data?: { message?: string; errors?: string[] } }).data;
    if (data?.errors?.length) return data.errors[0];
    if (data?.message) return data.message;
  }
  return fallback;
}

function CopyButton({ value, label = 'Kopyala' }: { value: string; label?: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <Button type="button" variant="ghost" size="sm" className="h-7 px-2 text-xs" disabled={!value} onClick={async () => {
      try { await navigator.clipboard.writeText(value); setCopied(true); setTimeout(() => setCopied(false), 1500); } catch { /* sessiz */ }
    }}>
      {copied ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />} {copied ? 'Kopyalandı' : label}
    </Button>
  );
}

function ImageSlot({ profile, slot, title, spec, aspect }: { profile: SocialProfile; slot: 'profile' | 'cover'; title: string; spec: string; aspect: string }) {
  const { toast } = useToast();
  const upload = useUploadSocialProfileImage();
  const remove = useDeleteSocialProfileImage();
  const input = useRef<HTMLInputElement>(null);
  const url = slot === 'profile' ? profile.profileImageUrl : profile.coverImageUrl;
  const busy = upload.isPending || remove.isPending;

  async function onPick(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]; e.target.value = '';
    if (!file) return;
    if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type)) { toast({ title: 'Desteklenmeyen dosya', description: 'PNG, JPEG veya WebP yükleyin.', variant: 'destructive' }); return; }
    if (file.size > 5 * 1024 * 1024) { toast({ title: 'Dosya çok büyük', description: 'En fazla 5 MB.', variant: 'destructive' }); return; }
    try { await upload.mutateAsync({ provider: profile.provider, language: profile.language, slot, file }); toast({ title: 'Görsel yüklendi' }); }
    catch (err) { toast({ title: 'Yüklenemedi', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function onRemove() {
    try { await remove.mutateAsync({ provider: profile.provider, language: profile.language, slot }); }
    catch (err) { toast({ title: 'Kaldırılamadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }

  return (
    <div className="space-y-1.5">
      <div className="text-xs font-medium text-muted-foreground">{title}</div>
      {url ? (
        <img src={url} alt={title} className={cn('w-full rounded-lg border border-border object-cover bg-muted/20', aspect, slot === 'profile' && 'rounded-full max-w-[160px]')} />
      ) : (
        <div className={cn('w-full rounded-lg border border-dashed border-border flex flex-col items-center justify-center gap-1 text-xs text-muted-foreground p-4 text-center', aspect, slot === 'profile' && 'rounded-full max-w-[160px]')}>
          <ImageOff className="h-5 w-5" /> Görsel yok
        </div>
      )}
      <p className="text-[11px] text-muted-foreground">{spec}</p>
      <div className="flex flex-wrap gap-2">
        <input ref={input} type="file" accept="image/png,image/jpeg,image/webp" className="hidden" onChange={onPick} />
        <Button type="button" variant="outline" size="sm" disabled={busy} onClick={() => input.current?.click()}>
          {upload.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Upload className="h-3.5 w-3.5" />} {url ? 'Değiştir' : 'Yükle'}
        </Button>
        {url && (
          <>
            <a href={url} target="_blank" rel="noopener noreferrer" className="inline-flex items-center h-8 px-3 rounded-md text-xs border border-input hover:bg-accent">Aç / indir</a>
            <Button type="button" variant="ghost" size="sm" disabled={busy} onClick={onRemove} className="text-red-400 hover:text-red-300"><Trash2 className="h-3.5 w-3.5" /></Button>
          </>
        )}
      </div>
    </div>
  );
}

// ── Profil kartı ─────────────────────────────────────────────────────────────

function ProfileCard({ profile }: { profile: SocialProfile }) {
  const { toast } = useToast();
  const update = useUpdateSocialProfile();
  const reset = useResetSocialProfile();
  const apply = useApplySocialProfile();
  const mark = useMarkSocialProfileApplied();
  const [draft, setDraft] = useState<Record<string, string>>(profile.fields);
  const [resetOpen, setResetOpen] = useState(false);
  const [applyOpen, setApplyOpen] = useState(false);
  const seeded = useRef<string | null>(null);

  // Sunucu kaydı değişince (kaydet/sıfırla/başka sekme) taslağı yenile; yazılanı arka plan yenilemesi ezmesin.
  useEffect(() => {
    const key = `${profile.provider}:${profile.updatedAt ?? 'default'}:${profile.isDefault}`;
    if (seeded.current === key) return;
    seeded.current = key;
    setDraft(profile.fields);
  }, [profile]);

  const defs = profile.provider === 'Facebook' ? FACEBOOK_FIELDS : INSTAGRAM_FIELDS;
  const dirty = useMemo(() => defs.some(d => (draft[d.key] ?? '') !== (profile.fields[d.key] ?? '')), [defs, draft, profile.fields]);
  const problem = useMemo(() => {
    for (const d of defs) {
      const v = (draft[d.key] ?? '').trim();
      if (d.max && v.length > d.max) return `${d.label} ${d.max} karakteri aşıyor.`;
      if (d.key === 'website' && v && !/^https?:\/\//i.test(v)) return 'Web sitesi http(s) ile başlamalı.';
    }
    return null;
  }, [defs, draft]);
  const busy = update.isPending || reset.isPending || apply.isPending || mark.isPending;
  const isFacebook = profile.provider === 'Facebook';
  const specs = IMAGE_SPECS[profile.provider] ?? IMAGE_SPECS.Instagram;

  async function onSave() {
    if (problem) { toast({ title: 'Kaydedilemedi', description: problem, variant: 'destructive' }); return; }
    try { await update.mutateAsync({ provider: profile.provider, language: profile.language, fields: draft }); toast({ title: 'Profil metni kaydedildi' }); }
    catch (err) { toast({ title: 'Kaydedilemedi', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function onReset() {
    try { await reset.mutateAsync({ provider: profile.provider, language: profile.language }); toast({ title: 'Varsayılana dönüldü' }); setResetOpen(false); }
    catch (err) { toast({ title: 'Olmadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function onApply() {
    if (dirty) { toast({ title: 'Önce kaydedin', description: 'Kaydedilmemiş değişiklik var.', variant: 'destructive' }); return; }
    try {
      await apply.mutateAsync({ provider: profile.provider, language: profile.language });
      toast({ title: 'Facebook sayfası güncellendi', description: 'Hakkında, açıklama, web sitesi, telefon/e-posta ve varsa profil fotoğrafı basıldı.' });
      setApplyOpen(false);
    } catch (err) { toast({ title: 'Uygulanamadı', description: errorMessage(err, 'Meta reddetti.'), variant: 'destructive' }); setApplyOpen(false); }
  }
  async function onMark() {
    try { await mark.mutateAsync({ provider: profile.provider, language: profile.language }); toast({ title: 'Uygulandı olarak işaretlendi' }); }
    catch (err) { toast({ title: 'Olmadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }

  const allText = defs.map(d => `${d.label}:\n${draft[d.key] ?? ''}`).join('\n\n');

  return (
    <Card>
      <CardContent className="p-4 space-y-4">
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div className="flex items-center gap-2">
            <ProviderIcon provider={profile.provider} className="h-5 w-5 text-muted-foreground" />
            <div>
              <div className="font-semibold text-foreground">{profile.provider === 'Facebook' ? 'Facebook Sayfası' : 'Instagram'}</div>
              <div className="text-xs text-muted-foreground">
                {profile.isDefault ? 'Varsayılan metin (henüz kaydedilmedi)' : `Son düzenleme ${fmtDateTime(profile.updatedAt)}${profile.updatedBy ? ` · ${profile.updatedBy}` : ''}`}
                {profile.connectedAccountLabel && <> · Bağlı: {profile.connectedAccountLabel}</>}
              </div>
            </div>
          </div>
          <div className="flex items-center gap-1.5 text-xs">
            {profile.appliedAt
              ? <span className="inline-flex items-center gap-1 rounded-full border border-emerald-500/30 bg-emerald-500/10 px-2 py-0.5 text-emerald-400"><CheckCircle2 className="h-3 w-3" /> Uygulandı {fmtDateTime(profile.appliedAt)}</span>
              : <span className="inline-flex items-center gap-1 rounded-full border border-border bg-muted/40 px-2 py-0.5 text-muted-foreground">Henüz uygulanmadı</span>}
          </div>
        </div>

        {profile.applyError && (
          <p className="text-xs text-red-400 flex items-start gap-1"><AlertTriangle className="h-3.5 w-3.5 shrink-0 mt-0.5" /> {profile.applyError}</p>
        )}

        <div className={cn('rounded-md border px-3 py-2 text-xs flex items-start gap-2', isFacebook ? 'border-border bg-muted/20 text-muted-foreground' : 'border-amber-500/40 bg-amber-500/5 text-amber-200')}>
          <Info className="h-3.5 w-3.5 shrink-0 mt-0.5" />
          {isFacebook
            ? <span>"Facebook'a uygula" hakkında, açıklama, web sitesi, telefon ve e-postayı sayfaya basar; profil fotoğrafı yüklüyse onu da. Sayfa adı, kullanıcı adı, kategori, eylem düğmesi ve kapak elle ayarlanır. İzin: Facebook Login for Business yapılandırmasında <code>pages_manage_metadata</code> olmalı.</span>
            : <span>Instagram profil bilgileri API ile yazılamaz. Metinleri kopyalayıp Instagram → Profili düzenle'ye yapıştırın, görseli indirip yükleyin, sonra "Uyguladım" işaretleyin.</span>}
        </div>

        <div className="grid gap-5 lg:grid-cols-[220px_1fr]">
          <div className="space-y-4">
            <ImageSlot profile={profile} slot="profile" title="Profil fotoğrafı" spec={specs.profile} aspect="aspect-square" />
            {isFacebook && specs.cover && <ImageSlot profile={profile} slot="cover" title="Kapak fotoğrafı" spec={specs.cover} aspect="aspect-[820/312]" />}
          </div>

          <div className="space-y-3">
            {defs.map(d => {
              const v = draft[d.key] ?? '';
              const over = !!d.max && v.trim().length > d.max;
              const pushed = profile.pushableKeys.includes(d.key);
              return (
                <div key={d.key} className="space-y-1">
                  <div className="flex items-center justify-between gap-2">
                    <label className="text-xs font-medium text-muted-foreground inline-flex items-center gap-1.5">
                      {d.label}
                      {pushed && <span className="rounded border border-sky-500/30 bg-sky-500/10 px-1 text-[10px] text-sky-300" title="Facebook'a uygula ile sayfaya basılır">API</span>}
                      {(d.manual || !isFacebook) && !pushed && <span className="rounded border border-border px-1 text-[10px] text-muted-foreground" title="Platformda elle girilir">elle</span>}
                    </label>
                    <div className="flex items-center gap-1">
                      {d.max && <span className={cn('text-[11px] tabular-nums', over ? 'text-red-400 font-semibold' : 'text-muted-foreground')}>{v.trim().length}/{d.max}</span>}
                      <CopyButton value={v} />
                    </div>
                  </div>
                  {d.multiline
                    ? <Textarea rows={d.rows ?? 3} value={v} onChange={e => setDraft(s => ({ ...s, [d.key]: e.target.value }))} className={cn('text-sm', over && 'border-red-500')} />
                    : <Input value={v} onChange={e => setDraft(s => ({ ...s, [d.key]: e.target.value }))} className={cn(over && 'border-red-500')} />}
                  {d.help && <p className="text-[11px] text-muted-foreground">{d.help}</p>}
                </div>
              );
            })}
          </div>
        </div>

        {dirty && problem && <p className="text-xs text-red-400 flex items-center gap-1"><AlertTriangle className="h-3.5 w-3.5" /> {problem}</p>}

        <div className="flex flex-wrap items-center gap-2 border-t border-border pt-3">
          <Button type="button" size="sm" disabled={busy || !dirty || !!problem} onClick={onSave}>
            {update.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />} Kaydet
          </Button>
          <CopyButton value={allText} label="Tümünü kopyala" />
          <Button type="button" variant="ghost" size="sm" disabled={busy || profile.isDefault} onClick={() => setResetOpen(true)}>
            <RotateCcw className="h-4 w-4" /> Varsayılana dön
          </Button>
          <div className="ml-auto flex gap-2">
            {isFacebook ? (
              <Button type="button" variant="outline" size="sm" disabled={busy || !profile.canApply || dirty} onClick={() => setApplyOpen(true)}
                title={!profile.canApply ? 'Bu dile eşlenmiş etkin bir Facebook sayfası bağlı değil' : dirty ? 'Önce kaydedin' : undefined}>
                {apply.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />} Facebook'a uygula
              </Button>
            ) : null}
            <Button type="button" variant="outline" size="sm" disabled={busy} onClick={onMark} title="Platformda elle girdiğinizi işaretler">
              <CheckCircle2 className="h-4 w-4" /> {isFacebook ? 'Kalanları elle uyguladım' : 'Uyguladım'}
            </Button>
          </div>
        </div>

        <Dialog open={resetOpen} onOpenChange={(o) => !o && !busy && setResetOpen(false)}>
          <DialogContent className="sm:max-w-md">
            <DialogHeader>
              <DialogTitle>Varsayılana dönülsün mü?</DialogTitle>
              <DialogDescription>Düzenlediğiniz metinler ve yüklediğiniz görseller silinir, sunucu varsayılanı geri gelir. Platformdaki profil değişmez.</DialogDescription>
            </DialogHeader>
            <DialogFooter className="gap-2">
              <Button type="button" variant="outline" size="sm" disabled={busy} onClick={() => setResetOpen(false)}>Vazgeç</Button>
              <Button type="button" variant="destructive" size="sm" disabled={busy} onClick={onReset}><RotateCcw className="h-4 w-4" /> Sıfırla</Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>

        <Dialog open={applyOpen} onOpenChange={(o) => !o && !busy && setApplyOpen(false)}>
          <DialogContent className="sm:max-w-md">
            <DialogHeader>
              <DialogTitle>Facebook sayfası güncellensin mi?</DialogTitle>
              <DialogDescription>
                {profile.connectedAccountLabel} sayfasında hakkında, açıklama, web sitesi{(draft.phone ?? '').trim() ? ', telefon' : ''}{(draft.email ?? '').trim() ? ', e-posta' : ''}
                {profile.profileImageUrl ? ' ve profil fotoğrafı' : ''} herkese açık olarak değişir. Boş alanlar basılmaz.
              </DialogDescription>
            </DialogHeader>
            <DialogFooter className="gap-2">
              <Button type="button" variant="outline" size="sm" disabled={busy} onClick={() => setApplyOpen(false)}>Vazgeç</Button>
              <Button type="button" size="sm" disabled={busy} onClick={onApply}>{apply.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />} Uygula</Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </CardContent>
    </Card>
  );
}

// ── Sayfa ────────────────────────────────────────────────────────────────────

export function SocialProfilesPage() {
  const { data: profiles = [], isLoading, isError, error } = useSocialProfiles('tr');
  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-foreground flex items-center gap-2"><Megaphone className="h-6 w-6" /> Sosyal Medya</h1>
          <p className="text-sm text-muted-foreground mt-1">Facebook sayfası ve Instagram hesabının profil metinleri ve görselleri. Varsayılanlar hazır; düzenleyip kaydedin, Facebook'a tek tıkla basın, Instagram'a kopyalayıp girin.</p>
        </div>
        <SocialNav />
      </div>

      {isLoading && <div className="flex items-center gap-2 text-muted-foreground text-sm"><Loader2 className="h-4 w-4 animate-spin" /> Yükleniyor…</div>}
      {isError && <div className="flex items-center gap-2 text-sm text-red-400"><AlertTriangle className="h-4 w-4" /> {errorMessage(error, 'Profiller alınamadı.')}</div>}

      <div className="grid gap-4 2xl:grid-cols-2">
        {profiles.map(p => <ProfileCard key={`${p.provider}:${p.language}`} profile={p} />)}
      </div>
    </div>
  );
}
