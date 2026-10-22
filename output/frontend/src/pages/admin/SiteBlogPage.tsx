import { useEffect, useMemo, useState } from 'react';
import {
  Newspaper, Check, X, ExternalLink, Loader2, AlertTriangle, Trash2, Rocket, ListPlus, Eye, Pencil, ChevronLeft, ChevronRight, Info, Clock, Globe, CircleAlert, Ban,
} from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '@/components/ui/dialog';
import { cn } from '@/lib/utils';
import { useToast } from '@/hooks/use-toast';
import {
  useSiteLocales, useSiteBlogPosts, useSiteBlogPost, usePendingSiteBlogCount, useUpdateSiteBlogVariant, useSiteBlogVariantAction, useRejectSiteBlogVariant,
  useApproveAllSiteBlog, useDeleteSiteBlogPost, usePublishStatus, usePublishSite, useTopicRequests, useCreateTopicRequest, useDropTopicRequest,
  SOLUTION_GROUPS, type SiteBlogPost, type SiteBlogVariant, type SiteBlogVariantStatus, type SiteLocale, type TopicRequestStatus,
} from '@/api/siteBlog';
import { MarkdownPreview, countWords, quickChecks } from '@/components/siteBlog/siteBlogMarkdown';

// ── Sabitler ─────────────────────────────────────────────────────────────────

const STATUS_TABS: { value: SiteBlogVariantStatus; label: string }[] = [
  { value: 'Pending', label: 'Bekleyen' }, { value: 'Approved', label: 'Onaylı' }, { value: 'Published', label: 'Yayında' }, { value: 'Rejected', label: 'Reddedilen' },
];
const STATUS_LABEL: Record<string, string> = { Pending: 'Bekliyor', Approved: 'Onaylı', Published: 'Yayında', Rejected: 'Reddedildi', Retired: 'Düşürüldü' };
const STATUS_STYLE: Record<string, string> = {
  Pending: 'bg-yellow-500/15 text-yellow-400 border-yellow-500/30',
  Approved: 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30',
  Published: 'bg-sky-500/15 text-sky-300 border-sky-500/30',
  Rejected: 'bg-red-500/15 text-red-400 border-red-500/30',
  Retired: 'bg-slate-500/15 text-slate-300 border-slate-500/30',
};
const SOURCE_LABEL: Record<string, string> = { Queue: 'Kuyruk', Rotation: 'Rotasyon', Manual: 'Elle' };
const PUBLISH_REASON: Record<string, string> = {
  'not-configured': 'Yayın tetiği bu ortamda tanımlı değil (dev). Onaylar kaydedildi; prod tetiği EMS çeker.',
  'nothing-to-publish': 'Yayın bekleyen yazı yok.',
  'debounced': 'Son tetikten 10 dakika geçmedi; birazdan yeniden deneyin (EMS zaten 30 dakikada bir tetikler).',
  'trigger-failed': 'Railway derleme tetiği başarısız oldu — EMS loglarına bakın.',
};
const TOPIC_STATUS_LABEL: Record<string, string> = { Open: 'Açık', Taken: 'Alındı', Done: 'Bitti', Dropped: 'Silindi' };

function errorMessage(err: unknown, fallback: string): string {
  if (err && typeof err === 'object' && 'response' in err && err.response && typeof err.response === 'object' && 'data' in err.response) {
    const data = (err.response as { data?: { message?: string; errors?: string[] } }).data;
    if (data?.errors?.length) return data.errors.length > 1 ? data.errors.join(' · ') : data.errors[0];
    if (data?.message) return data.message;
  }
  return fallback;
}
/** Doğrulayıcı 400'ünün errors[] listesi ("<locale>:<code>: açıklama") — alan altı uyarı için. */
function serverErrors(err: unknown): string[] {
  if (err && typeof err === 'object' && 'response' in err && err.response && typeof err.response === 'object' && 'data' in err.response) {
    const data = (err.response as { data?: { message?: string; errors?: string[] } }).data;
    if (data?.message === 'SITE_BLOG_VALIDATION' && data.errors?.length) return data.errors;
  }
  return [];
}
function fmtDateTime(iso: string | null | undefined): string {
  return iso ? new Date(iso).toLocaleString('tr-TR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '—';
}
function fmtDay(iso: string): string { return new Date(iso).toLocaleDateString('tr-TR', { day: '2-digit', month: 'short', year: 'numeric' }); }

function StatusPill({ status, title, small }: { status: string; title?: string; small?: boolean }) {
  return <span title={title} className={cn('inline-flex items-center rounded-full border font-semibold', small ? 'px-1.5 py-0 text-[10px]' : 'px-2 py-0.5 text-[11px]', STATUS_STYLE[status] ?? STATUS_STYLE.Retired)}>{STATUS_LABEL[status] ?? status}</span>;
}

function LocaleBadges({ post, locales }: { post: SiteBlogPost; locales: SiteLocale[] }) {
  const order = locales.length ? locales.map(l => l.key) : (post.variants ?? []).map(v => v.locale);
  return (
    <div className="flex flex-wrap gap-1">
      {order.map(key => {
        const v = post.variants?.find(x => x.locale === key);
        if (!v) return <span key={key} className="inline-flex items-center rounded border border-dashed border-border px-1.5 py-0 text-[10px] text-muted-foreground" title="Bu yerel ayarda varyant yok">{key}</span>;
        return <span key={key} title={`${key}: ${STATUS_LABEL[v.status] ?? v.status}`} className={cn('inline-flex items-center rounded border px-1.5 py-0 text-[10px] font-semibold uppercase', STATUS_STYLE[v.status] ?? STATUS_STYLE.Retired)}>{key}</span>;
      })}
    </div>
  );
}

// ── Detay diyaloğu ───────────────────────────────────────────────────────────

interface Draft { title: string; description: string; slug: string; bodyMarkdown: string; relatedPageSlug: string; publishDate: string }
const toDraft = (v: SiteBlogVariant): Draft => ({ title: v.title, description: v.description, slug: v.slug, bodyMarkdown: v.bodyMarkdown ?? '', relatedPageSlug: v.relatedPageSlug ?? '', publishDate: v.publishDate });
const sameDraft = (a: Draft, b: Draft) => a.title === b.title && a.description === b.description && a.slug === b.slug && a.bodyMarkdown === b.bodyMarkdown && a.relatedPageSlug === b.relatedPageSlug && a.publishDate === b.publishDate;

function PostDetailDialog({ id, locales, onClose }: { id: string; locales: SiteLocale[]; onClose: () => void }) {
  const { toast } = useToast();
  const { data: post, isLoading } = useSiteBlogPost(id);
  const update = useUpdateSiteBlogVariant();
  const act = useSiteBlogVariantAction();
  const reject = useRejectSiteBlogVariant();
  const approveAll = useApproveAllSiteBlog();
  const del = useDeleteSiteBlogPost();
  const [tab, setTab] = useState<string>('');
  const [drafts, setDrafts] = useState<Record<string, Draft>>({});
  const [serverErrs, setServerErrs] = useState<string[]>([]);
  const [mode, setMode] = useState<'edit' | 'preview'>('edit');
  const [rejectOpen, setRejectOpen] = useState(false);
  const [rejectReason, setRejectReason] = useState('');
  const [confirm, setConfirm] = useState<null | 'retire' | 'delete'>(null);
  const busy = update.isPending || act.isPending || reject.isPending || approveAll.isPending || del.isPending;

  const order = useMemo(() => {
    const keys = locales.map(l => l.key);
    for (const v of post?.variants ?? []) if (!keys.includes(v.locale)) keys.push(v.locale);
    return keys.filter(k => post?.variants?.some(v => v.locale === k));
  }, [locales, post]);

  useEffect(() => {
    if (!post) return;
    setDrafts(prev => {
      const next: Record<string, Draft> = {};
      for (const v of post.variants ?? []) next[v.id] = prev[v.id] && !sameDraft(prev[v.id], toDraft(v)) && prev[v.id] !== undefined ? prev[v.id] : toDraft(v);
      return next;
    });
    if (!tab || !post.variants?.some(v => v.locale === tab)) setTab(order[0] ?? '');
  }, [post, order, tab]);

  const variant = post?.variants?.find(v => v.locale === tab);
  const locale = locales.find(l => l.key === tab);
  const draft = variant ? drafts[variant.id] : undefined;
  const dirty = !!variant && !!draft && !sameDraft(draft, toDraft(variant));
  const checks = useMemo(() => (draft ? quickChecks(draft, locale) : []), [draft, locale]);
  const words = draft ? countWords(draft.bodyMarkdown) : 0;
  const dirtyCount = (post?.variants ?? []).filter(v => drafts[v.id] && !sameDraft(drafts[v.id], toDraft(v))).length;

  function setField<K extends keyof Draft>(key: K, value: Draft[K]) {
    if (!variant) return;
    setDrafts(d => ({ ...d, [variant.id]: { ...d[variant.id], [key]: value } }));
  }
  function fail(err: unknown, fallback: string) {
    const errs = serverErrors(err);
    setServerErrs(errs);
    toast({ title: 'İşlem başarısız', description: errs.length ? 'Doğrulayıcı hataları alan altında listelendi.' : errorMessage(err, fallback), variant: 'destructive' });
  }

  async function save(): Promise<boolean> {
    if (!variant || !draft || !dirty) return true;
    const orig = toDraft(variant);
    try {
      await update.mutateAsync({
        variantId: variant.id,
        title: draft.title !== orig.title ? draft.title : undefined,
        description: draft.description !== orig.description ? draft.description : undefined,
        slug: draft.slug !== orig.slug && variant.status !== 'Published' ? draft.slug : undefined,
        bodyMarkdown: draft.bodyMarkdown !== orig.bodyMarkdown ? draft.bodyMarkdown : undefined,
        relatedPageSlug: draft.relatedPageSlug !== orig.relatedPageSlug ? draft.relatedPageSlug : undefined,
        publishDate: draft.publishDate !== orig.publishDate ? draft.publishDate : undefined,
      });
      setServerErrs([]);
      return true;
    } catch (err) { fail(err, 'Kaydedilemedi.'); return false; }
  }
  async function onSave() { if (await save()) toast({ title: 'Kaydedildi', description: variant?.status === 'Published' ? 'Yayındaki yazı — değişiklik sonraki yayın tetiğinde siteye gider.' : undefined }); }
  async function onApprove() {
    if (!variant) return;
    if (!(await save())) return;
    try { await act.mutateAsync({ variantId: variant.id, action: 'approve' }); toast({ title: `${tab.toUpperCase()} onaylandı`, description: 'EMS en geç 30 dk içinde yayın tetiğini çeker.' }); }
    catch (err) { fail(err, 'Onaylanamadı.'); }
  }
  async function onApproveAll() {
    if (!post) return;
    if (dirty && !(await save())) return;
    try { await approveAll.mutateAsync(post.id); toast({ title: 'Tüm varyantlar onaylandı' }); }
    catch (err) { fail(err, 'Toplu onay yapılamadı.'); }
  }
  async function onReject() {
    if (!variant) return;
    try { await reject.mutateAsync({ variantId: variant.id, reason: rejectReason.trim() || undefined }); setRejectOpen(false); toast({ title: `${tab.toUpperCase()} reddedildi` }); }
    catch (err) { fail(err, 'Reddedilemedi.'); }
  }
  async function onRetire() {
    if (!variant) return;
    try { await act.mutateAsync({ variantId: variant.id, action: 'retire' }); setConfirm(null); toast({ title: 'Yayından düşürüldü', description: 'Adres sonraki yayında 404 olur.' }); }
    catch (err) { fail(err, 'Düşürülemedi.'); }
  }
  async function onDelete() {
    if (!post) return;
    try { await del.mutateAsync(post.id); setConfirm(null); toast({ title: 'Konu silindi', description: 'Ajan aynı konuyu yeniden yazabilir.' }); onClose(); }
    catch (err) { fail(err, 'Silinemedi.'); }
  }

  const slugLocked = variant?.status === 'Published';
  const editable = !!variant && variant.status !== 'Retired';
  const localErrsForField = (code: string) => checks.filter(c => c.code === code).map(c => c.message);
  const serverErrsForLocale = serverErrs.filter(e => e.startsWith(`${tab}:`)).map(e => e.slice(tab.length + 1));

  return (
    <Dialog open onOpenChange={(o) => !o && !busy && onClose()}>
      <DialogContent className="sm:max-w-5xl max-h-[94vh] overflow-y-auto">
        {!post ? (
          <DialogHeader><DialogTitle>Yazı</DialogTitle><DialogDescription>{isLoading ? 'Yükleniyor…' : 'Bulunamadı.'}</DialogDescription></DialogHeader>
        ) : (
          <>
            <DialogHeader>
              <div className="flex flex-wrap items-center gap-2 mb-1">
                <StatusPill status={post.overallStatus} />
                <span className="text-xs text-muted-foreground">{SOURCE_LABEL[post.source] ?? post.source} · grup <code className="text-[11px]">{post.group}</code> · {fmtDateTime(post.createdAt)}</span>
              </div>
              <DialogTitle className="text-left">{post.topic}</DialogTitle>
              {(post.sourceUrls ?? []).length > 0 && (
                <DialogDescription className="text-left">
                  <span className="inline-flex items-center gap-1 text-amber-300"><CircleAlert className="h-3.5 w-3.5" /> Kaynaklar — onaylamadan AÇ ve KONTROL ET (mevzuat atıfı insanda doğrulanır):</span>
                  <span className="block mt-1 space-y-0.5">
                    {post.sourceUrls!.map(u => <a key={u} href={u} target="_blank" rel="noopener noreferrer" className="flex items-center gap-1 text-primary hover:underline break-all"><ExternalLink className="h-3 w-3 shrink-0" /> {u}</a>)}
                  </span>
                </DialogDescription>
              )}
            </DialogHeader>

            {/* Yerel ayar sekmeleri */}
            <div className="flex flex-wrap items-center gap-1 border-b border-border">
              {order.map(key => {
                const v = post.variants!.find(x => x.locale === key)!;
                const l = locales.find(x => x.key === key);
                const d = drafts[v.id];
                const isDirty = d && !sameDraft(d, toDraft(v));
                return (
                  <button key={key} type="button" onClick={() => { setTab(key); setMode('edit'); }}
                    className={cn('inline-flex items-center gap-1.5 px-3 py-2 text-sm font-medium border-b-2 -mb-px', tab === key ? 'border-primary text-foreground' : 'border-transparent text-muted-foreground hover:text-foreground')}>
                    {l?.displayName ?? key}{isDirty && <span className="text-amber-400" title="Kaydedilmemiş değişiklik">•</span>}
                    <StatusPill status={v.status} small />
                  </button>
                );
              })}
            </div>

            {variant && draft && (
              <div className="space-y-3">
                <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-muted-foreground">
                  <div className="flex flex-wrap gap-x-4 gap-y-1">
                    <a href={variant.url} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 hover:text-primary"><Globe className="h-3.5 w-3.5" /> {variant.url}</a>
                    {variant.approvedAt && <span>Onay: {fmtDateTime(variant.approvedAt)}</span>}
                    {variant.publishedAt && <span>Yayın: {fmtDateTime(variant.publishedAt)}</span>}
                    {variant.liveAt ? <span className="text-emerald-400">Canlı: {fmtDateTime(variant.liveAt)}</span> : variant.publishedAt ? <span className="text-amber-300">Canlı doğrulanmadı</span> : null}
                    {variant.rejectedAt && <span className="text-red-300">Ret: {fmtDateTime(variant.rejectedAt)}{variant.rejectReason ? ` — ${variant.rejectReason}` : ''}</span>}
                  </div>
                  <div className="inline-flex rounded-md border border-border p-0.5">
                    <button type="button" onClick={() => setMode('edit')} className={cn('px-2 py-1 rounded text-xs inline-flex items-center gap-1', mode === 'edit' ? 'bg-background text-foreground shadow-sm' : 'text-muted-foreground')}><Pencil className="h-3 w-3" /> Düzenle</button>
                    <button type="button" onClick={() => setMode('preview')} className={cn('px-2 py-1 rounded text-xs inline-flex items-center gap-1', mode === 'preview' ? 'bg-background text-foreground shadow-sm' : 'text-muted-foreground')}><Eye className="h-3 w-3" /> Ön izleme</button>
                  </div>
                </div>

                {serverErrsForLocale.length > 0 && (
                  <div className="rounded-md border border-red-500/40 bg-red-500/5 px-3 py-2 text-xs text-red-300 space-y-0.5">
                    <div className="font-medium">Liftdesk doğrulayıcısı reddetti:</div>
                    {serverErrsForLocale.map((e, i) => <div key={i}>• {e}</div>)}
                  </div>
                )}

                {mode === 'preview' ? (
                  <div className="rounded-md border border-border bg-background/50 p-5 max-w-3xl">
                    <h1 className="text-2xl font-bold text-foreground mb-1">{draft.title}</h1>
                    <p className="text-sm text-muted-foreground mb-4">{draft.description}</p>
                    <MarkdownPreview body={draft.bodyMarkdown} className="text-sm text-foreground/90" />
                  </div>
                ) : (
                  <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_280px]">
                    <div className="space-y-3 min-w-0">
                      <div className="space-y-1">
                        <Label>Başlık</Label>
                        <Input value={draft.title} disabled={!editable} onChange={e => setField('title', e.target.value)} />
                        {localErrsForField('title_required').map(m => <p key={m} className="text-[11px] text-red-300">{m}</p>)}
                      </div>
                      <div className="space-y-1">
                        <div className="flex items-center justify-between"><Label>Açıklama (meta)</Label><span className={cn('text-[11px] tabular-nums', draft.description.trim().length < 120 || draft.description.trim().length > 160 ? 'text-red-300' : 'text-muted-foreground')}>{draft.description.trim().length}/160</span></div>
                        <Textarea value={draft.description} rows={2} disabled={!editable} onChange={e => setField('description', e.target.value)} />
                        {localErrsForField('description_length').map(m => <p key={m} className="text-[11px] text-red-300">{m}</p>)}
                      </div>
                      <div className="space-y-1">
                        <div className="flex items-center justify-between"><Label>Gövde (Markdown alt kümesi)</Label><span className={cn('text-[11px] tabular-nums', words < 450 || words > 1000 ? 'text-red-300' : 'text-muted-foreground')}>{words} kelime</span></div>
                        <Textarea value={draft.bodyMarkdown} rows={22} disabled={!editable} onChange={e => setField('bodyMarkdown', e.target.value)} className="font-mono text-xs leading-relaxed" spellCheck={false} />
                        <p className="text-[11px] text-muted-foreground">Desteklenen: <code>##</code> / <code>###</code> başlık, paragraf, <code>-</code> ve <code>1.</code> listeleri, <code>&gt;</code> vurgu, **kalın**, *eğik*, [metin](adres). Görsel, tablo, HTML, kod bloğu yok.</p>
                      </div>
                    </div>
                    <div className="space-y-3">
                      <div className="space-y-1">
                        <Label>Slug {slugLocked && <span className="text-[11px] text-muted-foreground">(yayında — kilitli)</span>}</Label>
                        <Input value={draft.slug} disabled={!editable || slugLocked} onChange={e => setField('slug', e.target.value)} className="font-mono text-xs" />
                        {localErrsForField('slug_format').map(m => <p key={m} className="text-[11px] text-red-300">{m}</p>)}
                      </div>
                      <div className="space-y-1">
                        <Label>Bağlı çözüm sayfası</Label>
                        <Input list="solution-groups" value={draft.relatedPageSlug} disabled={!editable} onChange={e => setField('relatedPageSlug', e.target.value)} placeholder="ör. lift-maintenance-contract-software" className="font-mono text-xs" />
                        <datalist id="solution-groups">{SOLUTION_GROUPS.map(g => <option key={g} value={g} />)}</datalist>
                      </div>
                      <div className="space-y-1">
                        <Label>Yayın tarihi</Label>
                        <Input type="date" value={draft.publishDate} disabled={!editable} onChange={e => setField('publishDate', e.target.value)} />
                        {variant.updatedDate && <p className="text-[11px] text-muted-foreground">Güncelleme: {variant.updatedDate}</p>}
                      </div>
                      <div className="rounded-md border border-border p-2 text-xs space-y-1">
                        <div className="font-medium text-foreground">Hızlı kontrol</div>
                        {checks.length === 0 ? <div className="text-emerald-400 inline-flex items-center gap-1"><Check className="h-3.5 w-3.5" /> Kurallara uygun görünüyor</div> : checks.map((c, i) => (
                          <div key={i} className="flex items-start gap-1 text-amber-300"><AlertTriangle className="h-3 w-3 shrink-0 mt-0.5" /><span><code className="text-[10px] opacity-70">{c.code}</code> {c.message}</span></div>
                        ))}
                        <div className="text-muted-foreground pt-1">Nihai karar Liftdesk doğrulayıcısında (kaydederken).</div>
                      </div>
                    </div>
                  </div>
                )}
              </div>
            )}

            <DialogFooter className="gap-2 flex-wrap sm:justify-between">
              <div className="flex flex-wrap gap-2">
                {post.variants?.every(v => v.status !== 'Published' && v.status !== 'Retired') && (
                  <Button type="button" variant="ghost" size="sm" disabled={busy} onClick={() => setConfirm('delete')} className="text-muted-foreground"><Trash2 className="h-4 w-4" /> Konuyu sil</Button>
                )}
                {variant?.status === 'Published' && (
                  <Button type="button" variant="ghost" size="sm" disabled={busy} onClick={() => setConfirm('retire')} className="text-muted-foreground"><Ban className="h-4 w-4" /> Yayından düşür</Button>
                )}
              </div>
              <div className="flex flex-wrap gap-2">
                <Button type="button" variant="outline" size="sm" disabled={busy} onClick={onClose}>Kapat</Button>
                {variant && (variant.status === 'Pending' || variant.status === 'Approved') && (
                  <Button type="button" variant="outline" size="sm" disabled={busy} onClick={() => { setRejectReason(''); setRejectOpen(true); }}><X className="h-4 w-4" /> Reddet</Button>
                )}
                {editable && <Button type="button" variant="secondary" size="sm" disabled={busy || !dirty} onClick={onSave}>{update.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : null} Kaydet{dirtyCount > 1 ? ` (${tab})` : ''}</Button>}
                {variant && (variant.status === 'Pending' || variant.status === 'Rejected' || variant.status === 'Retired') && (
                  <Button type="button" size="sm" disabled={busy} onClick={onApprove}>{act.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />} {tab.toUpperCase()} onayla</Button>
                )}
                {post.variants?.some(v => v.status === 'Pending' || v.status === 'Rejected') && (post.variants?.filter(v => v.status === 'Pending' || v.status === 'Rejected').length ?? 0) > 1 && (
                  <Button type="button" size="sm" variant="default" disabled={busy} onClick={onApproveAll}><Check className="h-4 w-4" /> Hepsini onayla</Button>
                )}
              </div>
            </DialogFooter>

            {rejectOpen && (
              <Dialog open onOpenChange={(o) => !o && setRejectOpen(false)}>
                <DialogContent className="sm:max-w-md">
                  <DialogHeader><DialogTitle>{tab.toUpperCase()} varyantını reddet</DialogTitle><DialogDescription>Sebep ajana geri bildirim olarak kalır; aynı konu yeniden önerilmez.</DialogDescription></DialogHeader>
                  <Textarea value={rejectReason} onChange={e => setRejectReason(e.target.value)} rows={3} maxLength={500} placeholder="Sebep (opsiyonel)" />
                  <DialogFooter><Button variant="outline" size="sm" onClick={() => setRejectOpen(false)}>Vazgeç</Button><Button size="sm" variant="destructive" disabled={busy} onClick={onReject}>Reddet</Button></DialogFooter>
                </DialogContent>
              </Dialog>
            )}
            {confirm && (
              <Dialog open onOpenChange={(o) => !o && setConfirm(null)}>
                <DialogContent className="sm:max-w-md">
                  <DialogHeader>
                    <DialogTitle>{confirm === 'retire' ? 'Yayından düşür' : 'Konuyu sil'}</DialogTitle>
                    <DialogDescription>{confirm === 'retire' ? 'Adres sonraki yayında 404 olur. Nadir kullanın — düzeltme için yazıyı düzenlemek yeterlidir.' : 'Hiçbir varyant yayınlanmadı; konu silinir ve ajan aynı konuyu yeniden yazabilir.'}</DialogDescription>
                  </DialogHeader>
                  <DialogFooter><Button variant="outline" size="sm" onClick={() => setConfirm(null)}>Vazgeç</Button><Button size="sm" variant="destructive" disabled={busy} onClick={confirm === 'retire' ? onRetire : onDelete}>{confirm === 'retire' ? 'Düşür' : 'Sil'}</Button></DialogFooter>
                </DialogContent>
              </Dialog>
            )}
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}

// ── Yayın paneli ─────────────────────────────────────────────────────────────

function PublishPanel() {
  const { toast } = useToast();
  const { data: st, isLoading, isError, error } = usePublishStatus();
  const publish = usePublishSite();
  async function onPublish() {
    try {
      const r = await publish.mutateAsync();
      toast({ title: r.triggered ? 'Yayın tetiği çekildi' : 'Tetik çekilmedi', description: r.triggered ? `${r.awaitingPublish} yazı derlemeye girdi; canlı doğrulama saatlik.` : (PUBLISH_REASON[r.reason ?? ''] ?? r.reason ?? ''), variant: r.triggered || r.reason === 'nothing-to-publish' || r.reason === 'not-configured' ? 'default' : 'destructive' });
    } catch (err) { toast({ title: 'Olmadı', description: errorMessage(err, 'Yayın tetiği çekilemedi.'), variant: 'destructive' }); }
  }
  return (
    <Card><CardContent className="p-3 space-y-2 text-xs">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="font-semibold text-foreground inline-flex items-center gap-1.5"><Rocket className="h-3.5 w-3.5" /> Yayın</div>
        <Button size="sm" variant="outline" disabled={publish.isPending || !st} onClick={onPublish}><Rocket className="h-4 w-4" /> Siteyi şimdi yayınla</Button>
      </div>
      {isLoading && <div className="text-muted-foreground">Yükleniyor…</div>}
      {isError && <div className="text-red-300">{errorMessage(error, 'Yayın durumu alınamadı.')}</div>}
      {st && (
        <>
          <div className="flex flex-wrap gap-x-5 gap-y-1 text-muted-foreground">
            <span>Tetik: {st.triggerConfigured ? <span className="text-emerald-400">tanımlı</span> : <span className="text-amber-300">tanımlı değil (dev ortamı — onaylar prod tetiğini bekler)</span>}</span>
            <span>Son tetik: {fmtDateTime(st.lastTriggeredAt)}{st.lastTriggerOutcome ? ` · ${st.lastTriggerOutcome}` : ''}</span>
            <span>Yayın bekleyen: <span className="text-foreground tabular-nums">{st.awaitingPublish}</span></span>
          </div>
          {(st.warnings ?? []).map((w, i) => <div key={i} className="text-amber-300 inline-flex items-center gap-1"><AlertTriangle className="h-3 w-3" /> {w}</div>)}
          {(st.awaitingLive ?? []).length > 0 && (
            <div>
              <div className="text-muted-foreground mb-1">Canlı doğrulama bekleyen ({st.awaitingLive!.length}) — 2 saati geçen kırmızı: Railway CORP derleme loguna bakın.</div>
              <div className="space-y-0.5">
                {st.awaitingLive!.map(a => (
                  <div key={a.variantId} className={cn('flex flex-wrap items-center gap-2', a.hoursWaiting > 2 ? 'text-red-300' : 'text-muted-foreground')}>
                    <Clock className="h-3 w-3" /><span className="uppercase font-semibold">{a.locale}</span>
                    <a href={a.url} target="_blank" rel="noopener noreferrer" className="hover:underline break-all">{a.slug}</a>
                    <span className="tabular-nums">{a.hoursWaiting.toFixed(1)} sa</span>
                  </div>
                ))}
              </div>
            </div>
          )}
        </>
      )}
    </CardContent></Card>
  );
}

// ── Konu kuyruğu ─────────────────────────────────────────────────────────────

function TopicQueuePanel() {
  const { toast } = useToast();
  const [status, setStatus] = useState<TopicRequestStatus | ''>('Open');
  const { data: items = [], isLoading } = useTopicRequests(status);
  const create = useCreateTopicRequest();
  const drop = useDropTopicRequest();
  const [form, setForm] = useState({ topic: '', group: '', note: '' });
  async function onCreate() {
    if (!form.topic.trim()) { toast({ title: 'Konu gerekli', variant: 'destructive' }); return; }
    try { await create.mutateAsync({ topic: form.topic.trim(), group: form.group || undefined, note: form.note.trim() || undefined }); setForm({ topic: '', group: '', note: '' }); toast({ title: 'Kuyruğa eklendi', description: 'Ajan bir sonraki Pazartesi koşusunda en eski açık konuyu alır.' }); }
    catch (err) { toast({ title: 'Eklenemedi', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function onDrop(id: string) {
    try { await drop.mutateAsync(id); toast({ title: 'Konu silindi' }); }
    catch (err) { toast({ title: 'Silinemedi', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  return (
    <Card><CardContent className="p-3 space-y-3 text-xs">
      <div className="font-semibold text-foreground inline-flex items-center gap-1.5"><ListPlus className="h-3.5 w-3.5" /> Konu kuyruğu</div>
      <div className="grid gap-2 sm:grid-cols-[minmax(0,1fr)_200px_minmax(0,1fr)_auto] items-end">
        <div className="space-y-1"><Label className="text-xs">Konu (Türkçe tek cümle)</Label><Input value={form.topic} onChange={e => setForm(f => ({ ...f, topic: e.target.value }))} placeholder="ör. QR etiketi yıpranınca ne olur" className="h-9" maxLength={300} /></div>
        <div className="space-y-1"><Label className="text-xs">Çözüm grubu (ops.)</Label>
          <select value={form.group} onChange={e => setForm(f => ({ ...f, group: e.target.value }))} className="w-full h-9 rounded-md border border-input bg-transparent px-2 text-sm">
            <option value="">—</option>{SOLUTION_GROUPS.map(g => <option key={g} value={g}>{g}</option>)}
          </select>
        </div>
        <div className="space-y-1"><Label className="text-xs">Not (ops.)</Label><Input value={form.note} onChange={e => setForm(f => ({ ...f, note: e.target.value }))} placeholder="ör. müşteri sorusu" className="h-9" maxLength={1000} /></div>
        <Button size="sm" disabled={create.isPending} onClick={onCreate}><ListPlus className="h-4 w-4" /> Ekle</Button>
      </div>
      <div className="flex flex-wrap items-center gap-1">
        {(['Open', 'Taken', 'Done', 'Dropped'] as TopicRequestStatus[]).map(s => (
          <button key={s} type="button" onClick={() => setStatus(s)} className={cn('rounded-full border px-2.5 py-0.5 text-xs', status === s ? 'bg-primary text-primary-foreground border-primary' : 'border-border text-muted-foreground hover:text-foreground')}>{TOPIC_STATUS_LABEL[s]}</button>
        ))}
      </div>
      {isLoading ? <div className="text-muted-foreground">Yükleniyor…</div> : items.length === 0 ? <div className="text-muted-foreground">Kayıt yok.</div> : (
        <table className="table-stack w-full text-xs">
          <thead><tr className="text-left text-muted-foreground border-b border-border"><th className="py-1 pr-2">Konu</th><th className="py-1 pr-2">Grup</th><th className="py-1 pr-2">Not</th><th className="py-1 pr-2">Tarih</th><th className="py-1"></th></tr></thead>
          <tbody>{items.map(t => (
            <tr key={t.id} className="border-b border-border/40 last:border-0 align-top">
              <td data-label="Konu" data-stack="primary" className="py-1 pr-2">{t.topic}</td><td data-label="Grup" className="py-1 pr-2 font-mono">{t.group ?? '—'}</td><td data-label="Not" className="py-1 pr-2 text-muted-foreground">{t.note ?? ''}</td>
              <td data-label="Tarih" className="py-1 pr-2 whitespace-nowrap text-muted-foreground">{fmtDay(t.createdAt)}</td>
              <td data-stack="actions" data-label="" className="py-1 text-right">{t.status === 'Open' && <button type="button" disabled={drop.isPending} onClick={() => onDrop(t.id)} className="text-muted-foreground hover:text-red-400" title="Kuyruktan çıkar"><Trash2 className="h-3.5 w-3.5" /></button>}</td>
            </tr>
          ))}</tbody>
        </table>
      )}
    </CardContent></Card>
  );
}

// ── Sayfa ────────────────────────────────────────────────────────────────────

export function SiteBlogPage() {
  const [status, setStatus] = useState<SiteBlogVariantStatus>('Pending');
  const [localeFilter, setLocaleFilter] = useState('');
  const [page, setPage] = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [showPublish, setShowPublish] = useState(false);
  const [showQueue, setShowQueue] = useState(false);
  const { data: locales = [] } = useSiteLocales();
  const { data, isLoading, isError, error } = useSiteBlogPosts({ status, locale: localeFilter, page });
  const { data: pending = 0 } = usePendingSiteBlogCount();
  const items = data?.items ?? [];
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-foreground flex items-center gap-2"><Newspaper className="h-6 w-6" /> Blog</h1>
          <p className="text-sm text-muted-foreground mt-1">liftdesk.app blogu. Ajan her Pazartesi bir konuyu tüm yerel ayarlarda yazar ve buraya düşürür; siz okuyup onaylarsınız. Onaylanan yazıyı EMS yayın tetiğiyle (en geç 30 dk) siteye derler. Yerel ayar listesi Liftdesk'ten gelir; yeni ülke eklendiğinde burada sekme olarak görünür.</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" size="sm" onClick={() => setShowPublish(v => !v)}><Rocket className="h-4 w-4" /> Yayın durumu</Button>
          <Button variant="outline" size="sm" onClick={() => setShowQueue(v => !v)}><ListPlus className="h-4 w-4" /> Konu kuyruğu</Button>
        </div>
      </div>

      {showPublish && <PublishPanel />}
      {showQueue && <TopicQueuePanel />}

      <div className="flex flex-wrap items-center gap-2">
        {STATUS_TABS.map(t => (
          <button key={t.value} type="button" onClick={() => { setStatus(t.value); setPage(1); }}
            className={cn('inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-sm font-medium', status === t.value ? 'bg-primary text-primary-foreground border-primary' : 'border-border text-muted-foreground hover:text-foreground')}>
            {t.label}{t.value === 'Pending' && pending > 0 && <span className={cn('rounded-full px-1.5 text-[11px]', status === t.value ? 'bg-primary-foreground/20' : 'bg-muted')}>{pending}</span>}
          </button>
        ))}
        {locales.length > 0 && (
          <select value={localeFilter} onChange={e => { setLocaleFilter(e.target.value); setPage(1); }} className="h-9 rounded-md border border-input bg-transparent px-2 text-sm ml-auto">
            <option value="">Tüm yerel ayarlar</option>
            {locales.map(l => <option key={l.key} value={l.key}>{l.displayName}</option>)}
          </select>
        )}
      </div>

      {isLoading && <div className="text-sm text-muted-foreground flex items-center gap-2"><Loader2 className="h-4 w-4 animate-spin" /> Yükleniyor…</div>}
      {isError && <div className="text-sm text-red-400 flex items-center gap-2"><AlertTriangle className="h-4 w-4" /> {errorMessage(error, 'Yazılar alınamadı.')}</div>}

      {data && (
        <Card><CardContent className="p-0">
          {items.length === 0 ? (
            <div className="py-12 text-center text-sm text-muted-foreground flex flex-col items-center gap-2">
              <Info className="h-6 w-6" />
              {status === 'Pending' ? 'Bekleyen taslak yok. Ajan Pazartesi 06:00 UTC koşar; bekleyen taslak varken yenisini yazmaz.' : 'Kayıt yok.'}
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="table-stack w-full text-sm">
                <thead><tr className="text-left text-xs text-muted-foreground border-b border-border">
                  <th className="px-3 py-2.5 font-medium">Oluşturma</th><th className="px-3 py-2.5 font-medium">Konu</th><th className="px-3 py-2.5 font-medium">Kaynak</th><th className="px-3 py-2.5 font-medium">Yerel ayarlar</th><th className="px-3 py-2.5 font-medium">Durum</th>
                </tr></thead>
                <tbody>{items.map(p => (
                  <tr key={p.id} onClick={() => setSelectedId(p.id)} className="border-b border-border/50 last:border-0 hover:bg-accent/40 cursor-pointer align-top">
                    <td data-label="Oluşturma" className="px-3 py-2.5 text-xs text-muted-foreground whitespace-nowrap">{fmtDay(p.createdAt)}</td>
                    <td data-stack="primary" data-label="Konu" className="px-3 py-2.5 md:max-w-[420px]"><div className="font-medium">{p.topic}</div><div className="text-[11px] text-muted-foreground font-mono truncate">{p.group}{(p.variants ?? []).length ? ` · ${p.variants![0].words} kelime` : ''}</div></td>
                    <td data-label="Kaynak" className="px-3 py-2.5 text-xs"><span className="inline-flex items-center rounded border border-border px-1.5 py-0 text-[11px]">{SOURCE_LABEL[p.source] ?? p.source}</span></td>
                    <td data-label="Yerel ayarlar" className="px-3 py-2.5"><LocaleBadges post={p} locales={locales} /></td>
                    <td data-label="Durum" className="px-3 py-2.5"><StatusPill status={p.overallStatus} /></td>
                  </tr>
                ))}</tbody>
              </table>
            </div>
          )}
        </CardContent></Card>
      )}

      {data && data.totalCount > data.pageSize && (
        <div className="flex items-center justify-between text-sm text-muted-foreground">
          <span>{data.totalCount} kayıt · sayfa {page}/{totalPages}</span>
          <div className="flex gap-2">
            <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage(p => p - 1)}><ChevronLeft className="h-4 w-4" /> Önceki</Button>
            <Button variant="outline" size="sm" disabled={page >= totalPages} onClick={() => setPage(p => p + 1)}>Sonraki <ChevronRight className="h-4 w-4" /></Button>
          </div>
        </div>
      )}

      {selectedId && <PostDetailDialog id={selectedId} locales={locales} onClose={() => setSelectedId(null)} />}
    </div>
  );
}
