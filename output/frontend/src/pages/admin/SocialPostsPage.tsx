import { useEffect, useMemo, useRef, useState } from 'react';
import {
  Megaphone, RefreshCw, Check, X, Trash2, RotateCcw, ExternalLink, AlertTriangle, Loader2,
  ChevronLeft, ChevronRight, ImagePlus, ImageOff, Calendar, Save, Inbox,
} from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter,
} from '@/components/ui/dialog';
import { cn } from '@/lib/utils';
import { useToast } from '@/hooks/use-toast';
import {
  useSocialPosts, useSocialPost, usePendingSocialPostCount, useSocialPostCard,
  useUpdateSocialPost, useApproveSocialPost, useRejectSocialPost, useDeleteSocialPost, useUploadSocialPostImage,
  SOCIAL_LANGUAGES,
  type SocialPost, type SocialPostStatus, type SocialPostVariant, type SocialPostLanguage,
} from '@/api/socialPosts';
import { SocialNav, PublishJobsBlock } from './socialShared';

// ── Sabitler ─────────────────────────────────────────────────────────────────

const KIND_CONFIG: Record<string, { label: string; className: string }> = {
  News:          { label: 'Haber',  className: 'bg-sky-500/15 text-sky-300 border-sky-500/30' },
  Info:          { label: 'Bilgi',  className: 'bg-teal-500/15 text-teal-300 border-teal-500/30' },
  Product:       { label: 'Ürün',   className: 'bg-violet-500/15 text-violet-300 border-violet-500/30' },
  Holiday:       { label: 'Bayram', className: 'bg-amber-500/15 text-amber-300 border-amber-500/30' },
  Commemoration: { label: 'Anma',   className: 'bg-slate-500/20 text-slate-300 border-slate-500/40' },
};

const STATUS_CONFIG: Record<string, { label: string; className: string }> = {
  Pending:   { label: 'Bekleyen',   className: 'bg-yellow-500/15 text-yellow-400 border-yellow-500/30' },
  Approved:  { label: 'Onaylı',     className: 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30' },
  Rejected:  { label: 'Reddedildi', className: 'bg-red-500/15 text-red-400 border-red-500/30' },
  Published: { label: 'Yayınlandı', className: 'bg-sky-500/15 text-sky-400 border-sky-500/30' },
};

const STATUS_TABS: { value: SocialPostStatus; label: string }[] = [
  { value: 'Pending',   label: 'Bekleyen' },
  { value: 'Approved',  label: 'Onaylı' },
  { value: 'Rejected',  label: 'Reddedilen' },
  { value: 'Published', label: 'Yayınlanan' },
];

const LANG_LABEL: Record<string, string> = { tr: 'Türkçe', en: 'English (US)', 'en-GB': 'English (UK)' };

const LIMITS = { cardTitle: 80, body: 4000, bodyShort: 280, hashtags: 300, topic: 300 } as const;
const MAX_IMAGE_BYTES = 5 * 1024 * 1024;

const EMPTY_TABS: Record<SocialPostStatus, string> = {
  Pending:   'Bekleyen taslak yok. Ajan her gün 05:00 UTC\'de önümüzdeki 4 günün boş slotlarını doldurur.',
  Approved:  'Onaylı gönderi yok. Onaylananlar publisher kuyruğuna girer ve yayın günü paylaşılır.',
  Rejected:  'Reddedilen gönderi yok.',
  Published: 'Henüz yayınlanan gönderi yok (yayınlama ayrı iş — publisher Published durumunu basar).',
};

// ── Yardımcılar ──────────────────────────────────────────────────────────────

function Pill({ className, children, title }: { className: string; children: React.ReactNode; title?: string }) {
  return (
    <span title={title} className={`inline-flex items-center gap-1 rounded-full border px-2.5 py-0.5 text-xs font-semibold ${className}`}>
      {children}
    </span>
  );
}

function KindBadge({ kind }: { kind: string }) {
  const cfg = KIND_CONFIG[kind] ?? { label: kind, className: 'bg-slate-500/15 text-slate-400 border-slate-500/30' };
  return <Pill className={cfg.className}>{cfg.label}</Pill>;
}

function StatusBadge({ status, title }: { status: string; title?: string }) {
  const cfg = STATUS_CONFIG[status] ?? { label: status, className: 'bg-slate-500/15 text-slate-400 border-slate-500/30' };
  return <Pill className={cfg.className} title={title}>{cfg.label}</Pill>;
}

/** Yayın günü saf gün (00:00Z) — UTC ile biçimlendir ki gün kaymasın. */
function fmtDay(iso: string): string {
  return new Intl.DateTimeFormat('tr-TR', { weekday: 'short', day: '2-digit', month: 'short', year: 'numeric', timeZone: 'UTC' })
    .format(new Date(iso));
}
function fmtDateTime(iso: string | null): string {
  if (!iso) return '—';
  return new Date(iso).toLocaleString('tr-TR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

function errorMessage(err: unknown, fallback: string): string {
  if (err && typeof err === 'object' && 'response' in err && err.response && typeof err.response === 'object' && 'data' in err.response) {
    const data = (err.response as { data?: { message?: string; errors?: string[] } }).data;
    if (data?.errors?.length) return data.errors[0];
    if (data?.message) return data.message;
  }
  return fallback;
}

function sourceLinks(sourceUrls: string | null): string[] {
  return (sourceUrls ?? '').split(/\r?\n/).map(s => s.trim()).filter(Boolean);
}

function isEditable(status: string) { return status === 'Pending' || status === 'Rejected'; }

function emptyVariant(language: string): SocialPostVariant {
  return { language, cardTitle: '', body: '', bodyShort: '', hashtags: '' };
}

/** Bir dilin alanları sınır dışında mı — kaydetmeyi/onayı engeller (API de 400 döner). */
function variantProblem(v: SocialPostVariant, lang: string): string | null {
  if (!v.cardTitle.trim()) return `${LANG_LABEL[lang] ?? lang}: kart başlığı boş.`;
  if (v.cardTitle.trim().length > LIMITS.cardTitle) return `${LANG_LABEL[lang] ?? lang}: kart başlığı ${LIMITS.cardTitle} karakteri aşıyor.`;
  if (!v.body.trim()) return `${LANG_LABEL[lang] ?? lang}: gönderi metni boş.`;
  if (v.body.trim().length > LIMITS.body) return `${LANG_LABEL[lang] ?? lang}: gönderi metni ${LIMITS.body} karakteri aşıyor.`;
  if (!v.bodyShort.trim()) return `${LANG_LABEL[lang] ?? lang}: X metni boş.`;
  if (v.bodyShort.trim().length > LIMITS.bodyShort) return `${LANG_LABEL[lang] ?? lang}: X metni ${LIMITS.bodyShort} karakteri aşıyor.`;
  if (v.hashtags.length > LIMITS.hashtags) return `${LANG_LABEL[lang] ?? lang}: etiketler ${LIMITS.hashtags} karakteri aşıyor.`;
  return null;
}

function sameVariant(a: SocialPostVariant | undefined, b: SocialPostVariant): boolean {
  if (!a) return false;
  return a.cardTitle === b.cardTitle && a.body === b.body && a.bodyShort === b.bodyShort && a.hashtags === b.hashtags;
}

function Counter({ value, max }: { value: number; max: number }) {
  const over = value > max;
  return (
    <span className={cn('text-[11px] tabular-nums', over ? 'text-red-400 font-semibold' : 'text-muted-foreground')}>
      {value}/{max}
    </span>
  );
}

// ── Kart önizlemesi ──────────────────────────────────────────────────────────

function CardPreview({ id, lang, version }: { id: string; lang: string; version: string }) {
  const { data: blob, isLoading, isError } = useSocialPostCard(id, lang, version);
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => {
    if (!blob) { setUrl(null); return; }
    const u = URL.createObjectURL(blob);
    setUrl(u);
    return () => URL.revokeObjectURL(u);
  }, [blob]);

  if (isLoading) return <Skeleton className="aspect-square w-full rounded-lg" />;
  if (isError || !url) {
    return (
      <div className="aspect-square w-full rounded-lg border border-dashed border-border flex flex-col items-center justify-center gap-2 text-xs text-muted-foreground p-4 text-center">
        <ImageOff className="h-5 w-5" />
        Bu dilde kart üretilemedi (varyant yok ya da Liftdesk erişilemedi).
      </div>
    );
  }
  return <img src={url} alt="Şablon kart önizlemesi" className="aspect-square w-full rounded-lg border border-border object-cover" />;
}

// ── Detay diyaloğu ───────────────────────────────────────────────────────────

function SocialPostDetailDialog({ id, onClose }: { id: string; onClose: () => void }) {
  const { toast } = useToast();
  const { data: post, isLoading, isError, error } = useSocialPost(id);
  const update  = useUpdateSocialPost();
  const approve = useApproveSocialPost();
  const reject  = useRejectSocialPost();
  const remove  = useDeleteSocialPost();
  const upload  = useUploadSocialPostImage();
  const busy = update.isPending || approve.isPending || reject.isPending || remove.isPending || upload.isPending;

  const [lang, setLang] = useState<SocialPostLanguage>('tr');
  const [topic, setTopic] = useState('');
  const [drafts, setDrafts] = useState<Record<string, SocialPostVariant>>({});
  const [rejectOpen, setRejectOpen] = useState(false);
  const [rejectReason, setRejectReason] = useState('');
  const [deleteOpen, setDeleteOpen] = useState(false);
  const fileInput = useRef<HTMLInputElement>(null);

  // Taslak alanlarını post'tan tohumla — yalnız kayıt değiştiğinde (id+updatedAt); operatörün
  // yazdığını arka plan yenilemesi ezmesin.
  const seededFor = useRef<string | null>(null);
  useEffect(() => {
    if (!post) return;
    const key = `${post.id}:${post.updatedAt}`;
    if (seededFor.current === key) return;
    seededFor.current = key;
    setTopic(post.topic);
    const next: Record<string, SocialPostVariant> = {};
    for (const l of SOCIAL_LANGUAGES) {
      const v = post.variants?.find(x => x.language === l);
      next[l] = v ? { ...v } : emptyVariant(l);
    }
    setDrafts(next);
  }, [post]);

  const editable = !!post && isEditable(post.status);
  const current = drafts[lang] ?? emptyVariant(lang);

  const changedLanguages = useMemo(() => {
    if (!post) return [] as string[];
    return SOCIAL_LANGUAGES.filter(l => {
      const original = post.variants?.find(x => x.language === l);
      const draft = drafts[l];
      if (!draft) return false;
      // Hiç varyantı olmayan dilde boş taslak "değişiklik" sayılmaz.
      if (!original && !draft.cardTitle && !draft.body && !draft.bodyShort && !draft.hashtags) return false;
      return !sameVariant(original, draft);
    });
  }, [post, drafts]);
  const topicChanged = !!post && topic.trim() !== post.topic;
  const dirty = changedLanguages.length > 0 || topicChanged;

  const problem = useMemo(() => {
    if (!post) return null;
    if (topic.trim().length === 0) return 'Konu boş olamaz.';
    if (topic.trim().length > LIMITS.topic) return `Konu ${LIMITS.topic} karakteri aşıyor.`;
    for (const l of changedLanguages) {
      const p = variantProblem(drafts[l], l);
      if (p) return p;
    }
    return null;
  }, [post, topic, drafts, changedLanguages]);

  function setField<K extends keyof SocialPostVariant>(key: K, value: SocialPostVariant[K]) {
    setDrafts(d => ({ ...d, [lang]: { ...(d[lang] ?? emptyVariant(lang)), [key]: value } }));
  }

  function fail(err: unknown, fallback: string) {
    toast({ title: 'İşlem başarısız', description: errorMessage(err, fallback), variant: 'destructive' });
  }

  async function save(): Promise<boolean> {
    if (!post || !dirty) return true;
    if (problem) { toast({ title: 'Kaydedilemedi', description: problem, variant: 'destructive' }); return false; }
    try {
      await update.mutateAsync({
        id: post.id,
        topic: topicChanged ? topic.trim() : undefined,
        variants: changedLanguages.length ? changedLanguages.map(l => ({
          ...drafts[l],
          cardTitle: drafts[l].cardTitle.trim(),
          body: drafts[l].body.trim(),
          bodyShort: drafts[l].bodyShort.trim(),
          hashtags: drafts[l].hashtags.trim(),
        })) : undefined,
      });
      return true;
    } catch (err) {
      fail(err, 'Liftdesk düzenlemeyi kabul etmedi.');
      return false;
    }
  }

  async function onSave() {
    if (await save()) toast({ title: 'Kaydedildi', description: 'Metinler güncellendi; kart başlığı değiştiyse önizleme yenilendi.' });
  }

  async function onApprove() {
    if (!post) return;
    // Düzenlenmiş ama kaydedilmemiş metin varsa önce kaydet — onaylanan metin publisher'a o haliyle gider.
    if (!(await save())) return;
    try {
      await approve.mutateAsync(post.id);
      toast({ title: 'Gönderi onaylandı', description: `${fmtDay(post.scheduledFor)} · ${post.topic}` });
      onClose();
    } catch (err) { fail(err, 'Onaylanamadı.'); }
  }

  async function onReject() {
    if (!post) return;
    try {
      await reject.mutateAsync({ id: post.id, reason: rejectReason.trim() || undefined });
      toast({ title: post.status === 'Approved' ? 'Onay geri alındı' : 'Gönderi reddedildi', description: post.topic });
      setRejectOpen(false);
      onClose();
    } catch (err) { fail(err, 'Reddedilemedi.'); }
  }

  async function onDelete() {
    if (!post) return;
    try {
      await remove.mutateAsync(post.id);
      toast({ title: 'Taslak silindi', description: 'Slot boşaldı; ajan yarınki koşuda yeni taslak üretecek.' });
      setDeleteOpen(false);
      onClose();
    } catch (err) { fail(err, 'Silinemedi.'); }
  }

  async function onPickImage(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!post || !file) return;
    if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type)) {
      toast({ title: 'Desteklenmeyen dosya', description: 'PNG, JPEG veya WebP yükleyin.', variant: 'destructive' }); return;
    }
    if (file.size > MAX_IMAGE_BYTES) {
      toast({ title: 'Dosya çok büyük', description: 'Görsel en fazla 5 MB olabilir.', variant: 'destructive' }); return;
    }
    try {
      await upload.mutateAsync({ id: post.id, file });
      toast({ title: 'Görsel yüklendi', description: 'Publisher şablon kart yerine bu görseli kullanacak.' });
    } catch (err) { fail(err, 'Görsel yüklenemedi.'); }
  }

  async function onClearImage() {
    if (!post) return;
    try {
      await update.mutateAsync({ id: post.id, customImageUrl: '' });
      toast({ title: 'Şablon karta dönüldü' });
    } catch (err) { fail(err, 'Görsel kaldırılamadı.'); }
  }

  const links = post ? sourceLinks(post.sourceUrls) : [];

  return (
    <Dialog open onOpenChange={(o) => !o && !busy && onClose()}>
      <DialogContent className="sm:max-w-4xl max-h-[92vh] overflow-y-auto">
        {isLoading ? (
          <div className="space-y-3 py-4">
            <Skeleton className="h-6 w-2/3" />
            <Skeleton className="h-4 w-full" />
            <Skeleton className="h-48 w-full" />
          </div>
        ) : isError || !post ? (
          <div className="py-8 text-center space-y-3">
            <DialogHeader><DialogTitle className="text-center">Gönderi açılamadı</DialogTitle></DialogHeader>
            <AlertTriangle className="h-8 w-8 mx-auto text-red-400" />
            <p className="text-sm text-muted-foreground">{errorMessage(error, 'Gönderi detayı yüklenemedi. Kayıt silinmiş olabilir.')}</p>
            <Button variant="outline" size="sm" onClick={onClose}>Kapat</Button>
          </div>
        ) : (
          <>
            <DialogHeader>
              <div className="flex flex-wrap items-center gap-2 mb-2">
                <KindBadge kind={post.kind} />
                <StatusBadge status={post.status} />
                <Pill className="border-border bg-muted/40 text-muted-foreground">
                  <Calendar className="h-3 w-3" /> {fmtDay(post.scheduledFor)}
                </Pill>
                {post.holidayName && <Pill className="border-amber-500/30 bg-amber-500/10 text-amber-300">{post.holidayName}</Pill>}
                <span className="text-[11px] text-muted-foreground font-mono ml-auto" title="Slot anahtarı (takvim)">{post.slotKey}</span>
              </div>
              <DialogTitle className="text-left">
                {editable ? (
                  <div className="space-y-1">
                    <Input value={topic} onChange={e => setTopic(e.target.value)} maxLength={LIMITS.topic + 20}
                      className="text-base font-semibold" placeholder="Konu (Türkçe özet)" />
                    <div className="flex justify-end"><Counter value={topic.trim().length} max={LIMITS.topic} /></div>
                  </div>
                ) : post.topic}
              </DialogTitle>
              <DialogDescription className="text-left">
                {post.kind === 'Commemoration'
                  ? 'Anma günü — saygı dili, promosyon yok.'
                  : post.status === 'Approved'
                    ? 'Onaylı metin publisher\'a bu haliyle gider. Düzeltmek için önce "Geri Al".'
                    : post.status === 'Published'
                      ? `Yayınlandı: ${fmtDateTime(post.publishedAt)}`
                      : 'Üç dil ayrı ayrı yayınlanır; her sekmeyi kontrol edin.'}
              </DialogDescription>
            </DialogHeader>

            {post.status === 'Rejected' && (
              <div className="rounded-md border border-red-500/30 bg-red-500/5 px-3 py-2 text-xs">
                <span className="font-medium text-red-400">Reddedildi</span>
                <span className="text-muted-foreground"> · {fmtDateTime(post.rejectedAt)}{post.rejectReason ? ` · ${post.rejectReason}` : ''}</span>
                <span className="text-muted-foreground"> — Slot dolu kalır. Düzenleyip yeniden onaylayabilir ya da silip ajana yeniden yazdırabilirsiniz.</span>
              </div>
            )}

            {(post.status === 'Approved' || post.status === 'Published') && <PublishJobsBlock postId={post.id} />}

            {links.length > 0 && (
              <div className="rounded-md border border-amber-500/40 bg-amber-500/5 px-3 py-2 text-xs space-y-1">
                <div className="flex items-center gap-1.5 font-medium text-amber-300">
                  <AlertTriangle className="h-3.5 w-3.5" /> Kaynaklar — onaylamadan önce aç ve kontrol et; ajan araştırıyor ama son söz insanda.
                </div>
                <ul className="space-y-0.5">
                  {links.map(u => (
                    <li key={u}>
                      <a href={u} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 text-primary hover:underline break-all">
                        <ExternalLink className="h-3 w-3 shrink-0" /> {u}
                      </a>
                    </li>
                  ))}
                </ul>
              </div>
            )}

            {/* Dil sekmeleri */}
            <div className="flex items-center gap-1 border-b border-border">
              {SOCIAL_LANGUAGES.map(l => {
                const has = !!post.variants?.some(v => v.language === l);
                const changed = changedLanguages.includes(l);
                const p = drafts[l] ? variantProblem(drafts[l], l) : null;
                return (
                  <button key={l} type="button" onClick={() => setLang(l)}
                    className={cn('px-3 py-2 text-sm font-medium border-b-2 -mb-px transition-colors inline-flex items-center gap-1.5',
                      lang === l ? 'border-primary text-foreground' : 'border-transparent text-muted-foreground hover:text-foreground')}>
                    {LANG_LABEL[l]}
                    {!has && <span className="text-[10px] text-amber-400" title="Bu dilde varyant yok">eksik</span>}
                    {changed && <span className="h-1.5 w-1.5 rounded-full bg-primary" title="Kaydedilmemiş değişiklik" />}
                    {changed && p && <AlertTriangle className="h-3 w-3 text-red-400" />}
                  </button>
                );
              })}
            </div>

            <div className="grid gap-5 md:grid-cols-[300px_1fr]">
              {/* Görsel */}
              <div className="space-y-2">
                {post.customImageUrl ? (
                  <>
                    <img src={post.customImageUrl} alt="Özel görsel" className="aspect-square w-full rounded-lg border border-border object-cover" />
                    <p className="text-[11px] text-muted-foreground">Özel görsel — publisher şablon kart yerine bunu kullanır (üç dilde aynı).</p>
                  </>
                ) : (
                  <>
                    <CardPreview id={post.id} lang={lang} version={post.updatedAt} />
                    <p className="text-[11px] text-muted-foreground">Şablon kart (1080×1080) — bu dilin kart başlığıyla üretilir.</p>
                  </>
                )}
                {editable && (
                  <div className="flex flex-wrap gap-2">
                    <input ref={fileInput} type="file" accept="image/png,image/jpeg,image/webp" className="hidden" onChange={onPickImage} />
                    <Button type="button" variant="outline" size="sm" disabled={busy} onClick={() => fileInput.current?.click()}>
                      {upload.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <ImagePlus className="h-3.5 w-3.5" />}
                      {post.customImageUrl ? 'Görseli değiştir' : 'Kendi görselini yükle'}
                    </Button>
                    {post.customImageUrl && (
                      <Button type="button" variant="ghost" size="sm" disabled={busy} onClick={onClearImage}>
                        <ImageOff className="h-3.5 w-3.5" /> Şablon karta dön
                      </Button>
                    )}
                  </div>
                )}
                {editable && <p className="text-[11px] text-muted-foreground">PNG / JPEG / WebP, ≤ 5 MB, önerilen 1080×1080 (Instagram kare).</p>}
              </div>

              {/* Metinler */}
              <div className="space-y-3">
                <div className="space-y-1">
                  <div className="flex items-center justify-between">
                    <label className="text-xs font-medium text-muted-foreground">Kart başlığı</label>
                    <Counter value={current.cardTitle.trim().length} max={LIMITS.cardTitle} />
                  </div>
                  <Input value={current.cardTitle} readOnly={!editable} onChange={e => setField('cardTitle', e.target.value)} />
                </div>
                <div className="space-y-1">
                  <div className="flex items-center justify-between">
                    <label className="text-xs font-medium text-muted-foreground">Gönderi metni (Facebook / Instagram, hashtag'siz)</label>
                    <Counter value={current.body.trim().length} max={LIMITS.body} />
                  </div>
                  <Textarea rows={7} value={current.body} readOnly={!editable} onChange={e => setField('body', e.target.value)} className="text-sm" />
                </div>
                <div className="space-y-1">
                  <div className="flex items-center justify-between">
                    <label className="text-xs font-medium text-muted-foreground">X metni (hashtag dâhil, 280 sınırı)</label>
                    <Counter value={current.bodyShort.trim().length} max={LIMITS.bodyShort} />
                  </div>
                  <Textarea rows={4} value={current.bodyShort} readOnly={!editable} onChange={e => setField('bodyShort', e.target.value)}
                    className={cn('text-sm', current.bodyShort.trim().length > LIMITS.bodyShort && 'border-red-500 focus-visible:ring-red-500')} />
                </div>
                <div className="space-y-1">
                  <div className="flex items-center justify-between">
                    <label className="text-xs font-medium text-muted-foreground">Etiketler</label>
                    <Counter value={current.hashtags.length} max={LIMITS.hashtags} />
                  </div>
                  <Input value={current.hashtags} readOnly={!editable} onChange={e => setField('hashtags', e.target.value)} placeholder="#asansör #bakım" />
                </div>
              </div>
            </div>

            {dirty && problem && (
              <p className="text-xs text-red-400 flex items-center gap-1"><AlertTriangle className="h-3.5 w-3.5" /> {problem}</p>
            )}

            <DialogFooter className="gap-2 flex-wrap sm:justify-between">
              <div className="flex gap-2">
                {post.status !== 'Published' && (
                  <Button type="button" variant="ghost" size="sm" disabled={busy} onClick={() => setDeleteOpen(true)}
                    className="text-red-400 hover:text-red-300">
                    <Trash2 className="h-4 w-4" /> Sil — yeniden üretilsin
                  </Button>
                )}
              </div>
              <div className="flex gap-2 flex-wrap justify-end">
                {post.status === 'Approved' && (
                  <Button type="button" variant="outline" size="sm" disabled={busy} onClick={() => { setRejectReason(''); setRejectOpen(true); }}>
                    <RotateCcw className="h-4 w-4" /> Geri Al
                  </Button>
                )}
                {post.status === 'Pending' && (
                  <Button type="button" variant="outline" size="sm" disabled={busy} onClick={() => { setRejectReason(''); setRejectOpen(true); }}>
                    <X className="h-4 w-4" /> Reddet
                  </Button>
                )}
                {editable && dirty && (
                  <Button type="button" variant="secondary" size="sm" disabled={busy || !!problem} onClick={onSave}>
                    {update.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />} Kaydet
                  </Button>
                )}
                {editable && (
                  <Button type="button" size="sm" disabled={busy || (dirty && !!problem)} onClick={onApprove}>
                    {approve.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
                    {dirty ? 'Kaydet ve Onayla' : 'Onayla'}
                  </Button>
                )}
              </div>
            </DialogFooter>

            {/* Ret / Geri Al */}
            <Dialog open={rejectOpen} onOpenChange={(o) => !o && !busy && setRejectOpen(false)}>
              <DialogContent className="sm:max-w-md">
                <DialogHeader>
                  <DialogTitle>{post.status === 'Approved' ? 'Onayı geri al' : 'Gönderiyi reddet'}</DialogTitle>
                  <DialogDescription>
                    {post.status === 'Approved'
                      ? 'Kayıt "Reddedildi" durumuna döner; düzenleyip yeniden onaylayabilirsiniz.'
                      : 'Slot dolu kalır. Ajanın yeniden yazmasını istiyorsanız reddetmek yerine silin.'}
                  </DialogDescription>
                </DialogHeader>
                <Textarea rows={3} value={rejectReason} onChange={e => setRejectReason(e.target.value)} maxLength={500}
                  placeholder="Sebep (opsiyonel) — ör. kaynak zayıf, ton uygun değil" />
                <DialogFooter className="gap-2">
                  <Button type="button" variant="outline" size="sm" disabled={busy} onClick={() => setRejectOpen(false)}>Vazgeç</Button>
                  <Button type="button" variant="destructive" size="sm" disabled={busy} onClick={onReject}>
                    {reject.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <X className="h-4 w-4" />}
                    {post.status === 'Approved' ? 'Geri Al' : 'Reddet'}
                  </Button>
                </DialogFooter>
              </DialogContent>
            </Dialog>

            {/* Sil */}
            <Dialog open={deleteOpen} onOpenChange={(o) => !o && !busy && setDeleteOpen(false)}>
              <DialogContent className="sm:max-w-md">
                <DialogHeader>
                  <DialogTitle>Taslak silinsin mi?</DialogTitle>
                  <DialogDescription>
                    Taslak silinecek ve ajan yarınki koşuda bu slot için YENİ taslak üretecek. Yüklediğiniz özel görsel de kaldırılır.
                  </DialogDescription>
                </DialogHeader>
                <DialogFooter className="gap-2">
                  <Button type="button" variant="outline" size="sm" disabled={busy} onClick={() => setDeleteOpen(false)}>Vazgeç</Button>
                  <Button type="button" variant="destructive" size="sm" disabled={busy} onClick={onDelete}>
                    {remove.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Trash2 className="h-4 w-4" />} Sil
                  </Button>
                </DialogFooter>
              </DialogContent>
            </Dialog>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}

// ── Liste ────────────────────────────────────────────────────────────────────

export function SocialPostsPage() {
  const { toast } = useToast();
  const [status, setStatus] = useState<SocialPostStatus>('Pending');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [page, setPage] = useState(1);
  const pageSize = 20;
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const { data, isLoading, isError, error, refetch, isFetching } = useSocialPosts({ status, from, to, page, pageSize });
  const { data: pendingCount = 0 } = usePendingSocialPostCount(true);
  const approve = useApproveSocialPost();

  const items = data?.items ?? [];
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / pageSize)) : 1;

  function changeStatus(s: SocialPostStatus) { setStatus(s); setPage(1); }

  async function quickApprove(e: React.MouseEvent, post: SocialPost) {
    e.stopPropagation();
    try {
      await approve.mutateAsync(post.id);
      toast({ title: 'Gönderi onaylandı', description: `${fmtDay(post.scheduledFor)} · ${post.topic}` });
    } catch (err) {
      toast({ title: 'Onaylanamadı', description: errorMessage(err, 'Liftdesk işlemi reddetti.'), variant: 'destructive' });
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-foreground flex items-center gap-2">
            <Megaphone className="h-6 w-6" /> Sosyal Medya
          </h1>
          <p className="text-sm text-muted-foreground mt-1">
            Liftdesk hesapları (Facebook · Instagram · X) için ajan taslakları. Üretim otomatik, onay burada, yayınlama ayrı iş.
            Pzt haber · Çar bilgi · Cum ürün · bayram ve anma takvimden.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <SocialNav />
          <Button variant="outline" size="sm" onClick={() => refetch()} disabled={isFetching}>
            <RefreshCw className={cn('h-4 w-4', isFetching && 'animate-spin')} /> Yenile
          </Button>
        </div>
      </div>

      {/* Durum sekmeleri + tarih süzgeci */}
      <div className="flex flex-wrap items-center gap-2">
        {STATUS_TABS.map(t => (
          <button key={t.value} type="button" onClick={() => changeStatus(t.value)}
            className={cn('inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-sm font-medium transition-colors',
              status === t.value ? 'bg-primary text-primary-foreground border-primary' : 'border-border text-muted-foreground hover:text-foreground hover:bg-accent')}>
            {t.label}
            {t.value === 'Pending' && pendingCount > 0 && (
              <span className={cn('inline-flex items-center justify-center rounded-full px-1.5 min-w-[1.25rem] h-5 text-xs font-semibold',
                status === 'Pending' ? 'bg-primary-foreground/20 text-primary-foreground' : 'bg-red-500 text-white')}>
                {pendingCount}
              </span>
            )}
          </button>
        ))}
        <div className="ml-auto flex items-center gap-2 text-sm">
          <span className="text-muted-foreground">Yayın günü</span>
          <input type="date" value={from} max={to || undefined} onChange={e => { setFrom(e.target.value); setPage(1); }}
            className="h-9 rounded-md border border-input bg-transparent px-2 text-sm" />
          <span className="text-muted-foreground">—</span>
          <input type="date" value={to} min={from || undefined} onChange={e => { setTo(e.target.value); setPage(1); }}
            className="h-9 rounded-md border border-input bg-transparent px-2 text-sm" />
          {(from || to) && (
            <Button variant="ghost" size="sm" onClick={() => { setFrom(''); setTo(''); setPage(1); }}>Temizle</Button>
          )}
        </div>
      </div>

      {isLoading && (
        <div className="flex items-center gap-2 text-muted-foreground text-sm"><Loader2 className="h-4 w-4 animate-spin" /> Yükleniyor…</div>
      )}
      {isError && (
        <div className="flex items-center gap-2 text-sm text-red-600 dark:text-red-400">
          <AlertTriangle className="h-4 w-4" /> {errorMessage(error, 'Gönderiler alınamadı.')}
        </div>
      )}

      {data && (
        <Card>
          <CardContent className="p-0">
            {items.length === 0 ? (
              <div className="flex flex-col items-center justify-center gap-2 py-14 text-center text-sm text-muted-foreground">
                <Inbox className="h-8 w-8" />
                <p className="max-w-md">{EMPTY_TABS[status]}</p>
              </div>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="text-left text-xs text-muted-foreground border-b border-border">
                      <th className="px-4 py-2.5 font-medium">Yayın günü</th>
                      <th className="px-4 py-2.5 font-medium">Tür</th>
                      <th className="px-4 py-2.5 font-medium">Konu</th>
                      <th className="px-4 py-2.5 font-medium">Diller</th>
                      <th className="px-4 py-2.5 font-medium">Durum</th>
                      <th className="px-4 py-2.5 font-medium text-right">İşlem</th>
                    </tr>
                  </thead>
                  <tbody>
                    {items.map(p => {
                      const langs = new Set((p.variants ?? []).map(v => v.language));
                      return (
                        <tr key={p.id} onClick={() => setSelectedId(p.id)}
                          className="border-b border-border/50 last:border-0 hover:bg-accent/40 cursor-pointer">
                          <td className="px-4 py-3 whitespace-nowrap tabular-nums">{fmtDay(p.scheduledFor)}</td>
                          <td className="px-4 py-3">
                            <div className="flex flex-wrap items-center gap-1.5">
                              <KindBadge kind={p.kind} />
                              {p.holidayName && <span className="text-xs text-amber-300">{p.holidayName}</span>}
                            </div>
                          </td>
                          <td className="px-4 py-3 max-w-[420px]">
                            <div className="truncate" title={p.topic}>{p.topic}</div>
                            {p.customImageUrl && <div className="text-[11px] text-muted-foreground">özel görsel</div>}
                          </td>
                          <td className="px-4 py-3">
                            <div className="flex gap-1">
                              {SOCIAL_LANGUAGES.map(l => (
                                <span key={l} title={langs.has(l) ? LANG_LABEL[l] : `${LANG_LABEL[l]} — eksik`}
                                  className={cn('rounded border px-1.5 py-0.5 text-[10px] font-mono',
                                    langs.has(l) ? 'border-border text-foreground' : 'border-dashed border-amber-500/40 text-amber-400')}>
                                  {l}
                                </span>
                              ))}
                            </div>
                          </td>
                          <td className="px-4 py-3">
                            <StatusBadge status={p.status} title={p.status === 'Rejected' && p.rejectReason ? p.rejectReason : undefined} />
                          </td>
                          <td className="px-4 py-3 text-right whitespace-nowrap">
                            {p.status === 'Pending' && (
                              <Button size="sm" variant="outline" className="mr-2" disabled={approve.isPending} onClick={(e) => quickApprove(e, p)}>
                                <Check className="h-3.5 w-3.5" /> Onayla
                              </Button>
                            )}
                            <Button size="sm" variant="ghost" onClick={(e) => { e.stopPropagation(); setSelectedId(p.id); }}>Aç</Button>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </CardContent>
        </Card>
      )}

      {data && data.totalCount > pageSize && (
        <div className="flex items-center justify-between text-sm text-muted-foreground">
          <span>{data.totalCount} kayıt · sayfa {page}/{totalPages}</span>
          <div className="flex gap-2">
            <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage(p => p - 1)}><ChevronLeft className="h-4 w-4" /> Önceki</Button>
            <Button variant="outline" size="sm" disabled={page >= totalPages} onClick={() => setPage(p => p + 1)}>Sonraki <ChevronRight className="h-4 w-4" /></Button>
          </div>
        </div>
      )}

      {selectedId && <SocialPostDetailDialog id={selectedId} onClose={() => setSelectedId(null)} />}
    </div>
  );
}
