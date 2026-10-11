import { useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  Megaphone, RefreshCw, AlertTriangle, Info, Loader2, PlayCircle, RotateCcw, SkipForward, ShieldCheck, Send, Unplug, Link2, Copy, Check, Clock,
} from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '@/components/ui/dialog';
import { cn } from '@/lib/utils';
import { useToast } from '@/hooks/use-toast';
import {
  useSocialAccounts, useSocialPublisherConfig, useMetaConnect, useMetaPending, useMetaComplete, useXConnect,
  useUpdateSocialAccount, useDeleteSocialAccount, useVerifySocialAccount, useTestSocialPost,
  usePublisherStatus, usePublishJobs, useRunPublisher, useRetryPublishJob, useSkipPublishJob,
  type SocialAccount, type SocialPublishJob,
} from '@/api/socialPublisher';
import { SocialNav, ProviderIcon, JobStatusPill, fmtDateTime, PROVIDER_LABEL } from './socialShared';

const LANG_LABEL: Record<string, string> = { tr: 'Türkçe (tr)', en: 'English US (en)', 'en-GB': 'English UK (en-GB)' };

const ACCOUNT_STATUS: Record<string, { label: string; className: string }> = {
  Active:   { label: 'Aktif',         className: 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30' },
  Expiring: { label: 'Süresi dolacak', className: 'bg-amber-500/15 text-amber-300 border-amber-500/30' },
  Expired:  { label: 'Süresi doldu',  className: 'bg-red-500/15 text-red-400 border-red-500/30' },
  Revoked:  { label: 'Token geçersiz', className: 'bg-red-500/15 text-red-400 border-red-500/30' },
  Error:    { label: 'Hata',          className: 'bg-red-500/15 text-red-400 border-red-500/30' },
};

function errorMessage(err: unknown, fallback: string): string {
  if (err && typeof err === 'object' && 'response' in err && err.response && typeof err.response === 'object' && 'data' in err.response) {
    const data = (err.response as { data?: { message?: string; errors?: string[] } }).data;
    if (data?.errors?.length) return data.errors[0];
    if (data?.message) return data.message;
  }
  return fallback;
}

function Pill({ className, children, title }: { className: string; children: React.ReactNode; title?: string }) {
  return <span title={title} className={`inline-flex items-center gap-1 rounded-full border px-2.5 py-0.5 text-xs font-semibold ${className}`}>{children}</span>;
}

function fmtDay(iso: string | null | undefined): string {
  if (!iso) return '—';
  return new Intl.DateTimeFormat('tr-TR', { weekday: 'short', day: '2-digit', month: 'short', timeZone: 'UTC' }).format(new Date(iso));
}

function CopyField({ label, value }: { label: string; value: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <div className="flex items-center gap-2 text-xs">
      <span className="text-muted-foreground w-40 shrink-0">{label}</span>
      <code className="flex-1 truncate rounded bg-muted/40 px-2 py-1 font-mono text-[11px]" title={value}>{value}</code>
      <Button type="button" variant="ghost" size="sm" className="h-7 px-2" onClick={async () => {
        try { await navigator.clipboard.writeText(value); setCopied(true); setTimeout(() => setCopied(false), 1500); } catch { /* clipboard yoksa sessiz */ }
      }}>
        {copied ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
      </Button>
    </div>
  );
}

// ── Hesap kartı ──────────────────────────────────────────────────────────────

function AccountCard({ account, languages }: { account: SocialAccount; languages: string[] }) {
  const { toast } = useToast();
  const update = useUpdateSocialAccount();
  const remove = useDeleteSocialAccount();
  const verify = useVerifySocialAccount();
  const test = useTestSocialPost();
  const [testOpen, setTestOpen] = useState(false);
  const [testText, setTestText] = useState('Liftdesk — test gönderisi. Asansör bakım yönetimi tek akışta: bakımdan tahsilata.');
  const [testImage, setTestImage] = useState('');
  const [deleteOpen, setDeleteOpen] = useState(false);
  const busy = update.isPending || remove.isPending || verify.isPending || test.isPending;
  const st = ACCOUNT_STATUS[account.status] ?? { label: account.status, className: 'bg-slate-500/15 text-slate-400 border-slate-500/30' };

  async function setLanguage(value: string) {
    try { await update.mutateAsync({ id: account.id, language: value }); toast({ title: 'Dil eşlemesi güncellendi' }); }
    catch (err) { toast({ title: 'Kaydedilemedi', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function toggleEnabled() {
    try { await update.mutateAsync({ id: account.id, isEnabled: !account.isEnabled }); }
    catch (err) { toast({ title: 'Kaydedilemedi', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function onVerify() {
    try {
      const a = await verify.mutateAsync(account.id);
      toast({ title: a.status === 'Active' ? 'Bağlantı sağlıklı' : `Durum: ${ACCOUNT_STATUS[a.status]?.label ?? a.status}`, description: a.lastError ?? a.externalName ?? undefined, variant: a.status === 'Active' ? undefined : 'destructive' });
    } catch (err) { toast({ title: 'Doğrulanamadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function onTest() {
    try {
      const r = await test.mutateAsync({ id: account.id, text: testText.trim(), imageUrl: testImage.trim() || undefined });
      toast({ title: 'Test gönderisi yayınlandı', description: `Platform id: ${r.platformPostId}` });
      setTestOpen(false);
    } catch (err) { toast({ title: 'Test gönderisi başarısız', description: errorMessage(err, 'Platform reddetti.'), variant: 'destructive' }); }
  }
  async function onDelete() {
    try { await remove.mutateAsync(account.id); toast({ title: 'Bağlantı kaldırıldı', description: 'Token silindi.' }); setDeleteOpen(false); }
    catch (err) { toast({ title: 'Kaldırılamadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }

  return (
    <Card className={cn(!account.isEnabled && 'opacity-70')}>
      <CardContent className="p-4 space-y-3">
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div className="flex items-center gap-2 min-w-0">
            <ProviderIcon provider={account.provider} className="h-5 w-5 text-muted-foreground shrink-0" />
            <div className="min-w-0">
              <div className="font-semibold text-foreground truncate">{account.label}</div>
              <div className="text-xs text-muted-foreground truncate">
                {PROVIDER_LABEL[account.provider] ?? account.provider} · {account.externalName ?? account.externalId}
                {account.provider === 'Instagram' && account.parentExternalId && <span title="Bağlı Facebook Page id"> · Page {account.parentExternalId}</span>}
              </div>
            </div>
          </div>
          <div className="flex items-center gap-1.5">
            <Pill className={st.className} title={account.lastError ?? undefined}>{st.label}</Pill>
            {!account.isEnabled && <Pill className="border-border bg-muted/40 text-muted-foreground">Devre dışı</Pill>}
          </div>
        </div>

        {account.lastError && account.status !== 'Active' && (
          <p className="text-xs text-red-400 flex items-start gap-1"><AlertTriangle className="h-3.5 w-3.5 shrink-0 mt-0.5" /> {account.lastError}</p>
        )}

        <div className="grid gap-2 sm:grid-cols-2 text-xs">
          <label className="space-y-1">
            <span className="text-muted-foreground">Yayınlanacak dil varyantı</span>
            <select value={account.language ?? ''} disabled={busy} onChange={e => setLanguage(e.target.value)}
              className="w-full h-9 rounded-md border border-input bg-transparent px-2 text-sm">
              <option value="">— eşleme yok (yayınlanmaz)</option>
              {languages.map(l => <option key={l} value={l}>{LANG_LABEL[l] ?? l}</option>)}
            </select>
          </label>
          <div className="space-y-1">
            <span className="text-muted-foreground">Token</span>
            <div className="h-9 flex items-center text-sm">
              {account.provider === 'X'
                ? <span title={account.tokenExpiresAt ? `Erişim token'ı ${fmtDateTime(account.tokenExpiresAt)} — yenileme token'ı ile otomatik yenilenir` : undefined}>
                    {account.hasRefreshToken ? 'Otomatik yenilenir' : 'Yenileme token\'ı yok'}
                  </span>
                : account.tokenExpiresAt
                  ? <span>Bitiş {fmtDateTime(account.tokenExpiresAt)}</span>
                  : <span>Uzun ömürlü sayfa token'ı</span>}
            </div>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
          <span>Son doğrulama: {fmtDateTime(account.lastVerifiedAt)}</span>
          {account.connectedBy && <span>· Bağlayan: {account.connectedBy}</span>}
        </div>

        <div className="flex flex-wrap gap-2">
          <Button type="button" variant="outline" size="sm" disabled={busy} onClick={onVerify}>
            {verify.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <ShieldCheck className="h-3.5 w-3.5" />} Doğrula
          </Button>
          <Button type="button" variant="outline" size="sm" disabled={busy || !account.isEnabled} onClick={() => setTestOpen(true)}>
            <Send className="h-3.5 w-3.5" /> Test gönderisi
          </Button>
          <Button type="button" variant="ghost" size="sm" disabled={busy} onClick={toggleEnabled}>
            {account.isEnabled ? 'Devre dışı bırak' : 'Etkinleştir'}
          </Button>
          <Button type="button" variant="ghost" size="sm" disabled={busy} className="text-red-400 hover:text-red-300 ml-auto" onClick={() => setDeleteOpen(true)}>
            <Unplug className="h-3.5 w-3.5" /> Bağlantıyı kaldır
          </Button>
        </div>

        <Dialog open={testOpen} onOpenChange={(o) => !o && !busy && setTestOpen(false)}>
          <DialogContent className="sm:max-w-md">
            <DialogHeader>
              <DialogTitle>Test gönderisi — {account.label}</DialogTitle>
              <DialogDescription>
                Bu, hesaba <b>gerçek ve herkese açık</b> bir gönderi atar; sonra platformdan elle silebilirsiniz.
                {account.provider === 'Instagram' && ' Instagram görselsiz gönderi kabul etmez: herkese açık bir görsel URL\'si zorunlu.'}
              </DialogDescription>
            </DialogHeader>
            <div className="space-y-2">
              <Textarea rows={3} value={testText} onChange={e => setTestText(e.target.value)} maxLength={account.provider === 'X' ? 280 : 2000} />
              {account.provider === 'X' && <div className="text-right text-[11px] text-muted-foreground tabular-nums">{testText.length}/280</div>}
              {account.provider !== 'X' && (
                <Input value={testImage} onChange={e => setTestImage(e.target.value)} placeholder={account.provider === 'Instagram' ? 'Görsel URL (zorunlu, https://…)' : 'Görsel URL (opsiyonel)'} />
              )}
            </div>
            <DialogFooter className="gap-2">
              <Button type="button" variant="outline" size="sm" disabled={busy} onClick={() => setTestOpen(false)}>Vazgeç</Button>
              <Button type="button" size="sm" disabled={busy || !testText.trim() || (account.provider === 'Instagram' && !testImage.trim())} onClick={onTest}>
                {test.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />} Yayınla
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>

        <Dialog open={deleteOpen} onOpenChange={(o) => !o && !busy && setDeleteOpen(false)}>
          <DialogContent className="sm:max-w-md">
            <DialogHeader>
              <DialogTitle>Bağlantı kaldırılsın mı?</DialogTitle>
              <DialogDescription>Token CRM'den silinir; bu hesabın bekleyen yayın işleri atlanır. Yeniden bağlamak için OAuth akışı tekrarlanır.</DialogDescription>
            </DialogHeader>
            <DialogFooter className="gap-2">
              <Button type="button" variant="outline" size="sm" disabled={busy} onClick={() => setDeleteOpen(false)}>Vazgeç</Button>
              <Button type="button" variant="destructive" size="sm" disabled={busy} onClick={onDelete}>
                {remove.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Unplug className="h-4 w-4" />} Kaldır
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </CardContent>
    </Card>
  );
}

// ── Meta sayfa seçimi (OAuth dönüşü) ─────────────────────────────────────────

function MetaCompleteDialog({ state, languages, onClose }: { state: string; languages: string[]; onClose: () => void }) {
  const { toast } = useToast();
  const { data, isLoading, isError, error } = useMetaPending(state);
  const complete = useMetaComplete();
  const [pageId, setPageId] = useState('');
  const [language, setLanguage] = useState('tr');
  const [fb, setFb] = useState(true);
  const [ig, setIg] = useState(true);
  const page = data?.pages.find(p => p.pageId === pageId) ?? data?.pages[0];

  useEffect(() => { if (data?.pages.length && !pageId) setPageId(data.pages[0].pageId); }, [data, pageId]);
  useEffect(() => { if (page && !page.instagramId) setIg(false); }, [page]);

  async function submit() {
    if (!page) return;
    try {
      const created = await complete.mutateAsync({ state, pageId: page.pageId, language: language || null, connectFacebook: fb, connectInstagram: ig && !!page.instagramId });
      toast({ title: 'Hesap bağlandı', description: created.map(a => a.label).join(' · ') });
      onClose();
    } catch (err) { toast({ title: 'Bağlanamadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }

  return (
    <Dialog open onOpenChange={(o) => !o && !complete.isPending && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Meta — sayfa seçimi</DialogTitle>
          <DialogDescription>Yayın yapılacak Facebook sayfasını ve ona bağlı Instagram Business hesabını seçin. Tek sayfa token'ı iki platformu da kapsar.</DialogDescription>
        </DialogHeader>
        {isLoading ? <div className="text-sm text-muted-foreground flex items-center gap-2"><Loader2 className="h-4 w-4 animate-spin" /> Sayfalar alınıyor…</div>
        : isError || !data ? <p className="text-sm text-red-400">{errorMessage(error, 'Bağlantı isteği bulunamadı ya da süresi doldu. Yeniden başlatın.')}</p>
        : data.pages.length === 0 ? <p className="text-sm text-amber-400">Bu Meta hesabının yönettiği sayfa bulunamadı. Sayfa yöneticisi olan hesapla giriş yapın ve <code>pages_show_list</code> iznini verin.</p>
        : (
          <div className="space-y-3 text-sm">
            <label className="block space-y-1">
              <span className="text-xs text-muted-foreground">Facebook sayfası</span>
              <select value={page?.pageId ?? ''} onChange={e => setPageId(e.target.value)} className="w-full h-9 rounded-md border border-input bg-transparent px-2 text-sm">
                {data.pages.map(p => <option key={p.pageId} value={p.pageId}>{p.name}{p.instagramUsername ? ` · @${p.instagramUsername}` : ' · (Instagram bağlı değil)'}</option>)}
              </select>
            </label>
            <div className="flex flex-wrap gap-4">
              <label className="inline-flex items-center gap-2"><input type="checkbox" checked={fb} onChange={e => setFb(e.target.checked)} /> Facebook sayfasını bağla</label>
              <label className={cn('inline-flex items-center gap-2', !page?.instagramId && 'opacity-50')}>
                <input type="checkbox" checked={ig && !!page?.instagramId} disabled={!page?.instagramId} onChange={e => setIg(e.target.checked)} />
                Instagram'ı bağla {page?.instagramUsername ? `(@${page.instagramUsername})` : '(sayfaya bağlı IG yok)'}
              </label>
            </div>
            <label className="block space-y-1">
              <span className="text-xs text-muted-foreground">Dil varyantı (hangi metin bu hesaplara gidecek)</span>
              <select value={language} onChange={e => setLanguage(e.target.value)} className="w-full h-9 rounded-md border border-input bg-transparent px-2 text-sm">
                <option value="">— sonra eşle</option>
                {languages.map(l => <option key={l} value={l}>{LANG_LABEL[l] ?? l}</option>)}
              </select>
            </label>
            {data.userTokenExpiresAt && <p className="text-[11px] text-muted-foreground">Kullanıcı token'ı {fmtDateTime(data.userTokenExpiresAt)}'e kadar; sayfa token'ları uzun ömürlüdür.</p>}
          </div>
        )}
        <DialogFooter className="gap-2">
          <Button type="button" variant="outline" size="sm" disabled={complete.isPending} onClick={onClose}>Vazgeç</Button>
          <Button type="button" size="sm" disabled={complete.isPending || !page || (!fb && !(ig && page.instagramId))} onClick={submit}>
            {complete.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Link2 className="h-4 w-4" />} Bağla
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Sayfa ────────────────────────────────────────────────────────────────────

export function SocialPublisherPage() {
  const { toast } = useToast();
  const [params, setParams] = useSearchParams();
  const [metaState, setMetaState] = useState<string | null>(null);
  const [jobFilter, setJobFilter] = useState('');

  const { data: config } = useSocialPublisherConfig();
  const { data: accounts = [], isLoading: accountsLoading } = useSocialAccounts();
  const { data: status, refetch: refetchStatus, isFetching: statusFetching } = usePublisherStatus();
  const { data: jobs = [] } = usePublishJobs({ status: jobFilter, limit: 100 });
  const metaConnect = useMetaConnect();
  const xConnect = useXConnect();
  const run = useRunPublisher();
  const retry = useRetryPublishJob();
  const skip = useSkipPublishJob();

  // OAuth dönüşü: ?meta=state → sayfa seçimi; ?connected=x → bilgi; ?error= → hata. Parametreler tüketilir.
  useEffect(() => {
    const meta = params.get('meta'); const connected = params.get('connected'); const error = params.get('error');
    if (!meta && !connected && !error) return;
    if (meta) setMetaState(meta);
    if (connected === 'x') toast({ title: 'X hesabı bağlandı', description: 'Şimdi dil eşlemesini seçin.' });
    if (error) toast({ title: 'Bağlantı başarısız', description: error, variant: 'destructive' });
    setParams({}, { replace: true });
  }, [params, setParams, toast]);

  const languages = config?.languages ?? ['tr', 'en', 'en-GB'];

  async function connect(kind: 'meta' | 'x') {
    try {
      const r = kind === 'meta' ? await metaConnect.mutateAsync() : await xConnect.mutateAsync();
      window.location.href = r.url; // tam sayfa yönlendirme — platform geri CRM'e döner
    } catch (err) { toast({ title: 'Başlatılamadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }

  async function onRun() {
    try {
      const s = await run.mutateAsync();
      toast({
        title: s.ran ? 'Döngü koştu' : 'Döngü koşmadı',
        description: s.ran
          ? `gönderi ${s.postsSeen} · yeni iş ${s.jobsCreated} · yayınlandı ${s.jobsSucceeded} · geçici hata ${s.jobsFailed} · bloke ${s.jobsBlocked} · damga ${s.stamped}`
          : (s.notes[0] ?? s.error ?? ''),
        variant: s.error ? 'destructive' : undefined,
      });
    } catch (err) { toast({ title: 'Çalıştırılamadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }

  async function onRetry(j: SocialPublishJob) {
    try { await retry.mutateAsync(j.id); toast({ title: 'Yeniden kuyruğa alındı', description: `${j.accountLabel} · ${j.topic ?? ''}` }); }
    catch (err) { toast({ title: 'Olmadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function onSkip(j: SocialPublishJob) {
    try { await skip.mutateAsync(j.id); toast({ title: 'Atlandı', description: 'Bu platformda yayınlanmayacak; diğerleri tamamsa damga atılır.' }); }
    catch (err) { toast({ title: 'Olmadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }

  const sortedWarnings = useMemo(() => {
    const order: Record<string, number> = { error: 0, warning: 1, info: 2 };
    return [...(status?.warnings ?? [])].sort((a, b) => (order[a.level] ?? 3) - (order[b.level] ?? 3));
  }, [status]);

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-foreground flex items-center gap-2"><Megaphone className="h-6 w-6" /> Sosyal Medya</h1>
          <p className="text-sm text-muted-foreground mt-1">Onaylanan gönderiler yayın günü, {status?.publishHourLocal ?? config?.publishHourLocal ?? 10}:00 ({status?.timeZone ?? 'Europe/Istanbul'}) sonrasında eşlenmiş hesaplara çıkar; sonra EMS'te "Yayınlandı" damgalanır.</p>
        </div>
        <div className="flex items-center gap-2">
          <SocialNav />
          <Button variant="outline" size="sm" onClick={() => refetchStatus()} disabled={statusFetching}><RefreshCw className={cn('h-4 w-4', statusFetching && 'animate-spin')} /></Button>
        </div>
      </div>

      {/* Uyarılar */}
      {sortedWarnings.length > 0 && (
        <div className="space-y-1.5">
          {sortedWarnings.map((w, i) => (
            <div key={i} className={cn('flex items-start gap-2 rounded-md border px-3 py-2 text-xs',
              w.level === 'error' ? 'border-red-500/40 bg-red-500/5 text-red-300' : w.level === 'warning' ? 'border-amber-500/40 bg-amber-500/5 text-amber-200' : 'border-border bg-muted/20 text-muted-foreground')}>
              {w.level === 'info' ? <Info className="h-3.5 w-3.5 shrink-0 mt-0.5" /> : <AlertTriangle className="h-3.5 w-3.5 shrink-0 mt-0.5" />}
              <span>{w.message}</span>
            </div>
          ))}
        </div>
      )}

      {/* Hesaplar */}
      <section className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="text-base font-semibold text-foreground">Bağlı hesaplar</h2>
          <div className="flex gap-2">
            <Button size="sm" variant="outline" disabled={!config?.metaConfigured || metaConnect.isPending} onClick={() => connect('meta')}
              title={config?.metaConfigured ? 'Facebook sayfası + Instagram Business (tek Meta uygulaması)' : 'Social:Meta:AppId / AppSecret ayarlanmalı'}>
              {metaConnect.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Link2 className="h-4 w-4" />} Meta bağla (Facebook + Instagram)
            </Button>
            <Button size="sm" variant="outline" disabled={!config?.xConfigured || xConnect.isPending} onClick={() => connect('x')}
              title={config?.xConfigured ? 'OAuth 2.0 PKCE' : 'Social:X:ClientId ayarlanmalı'}>
              {xConnect.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Link2 className="h-4 w-4" />} X bağla
            </Button>
          </div>
        </div>
        {accountsLoading ? <div className="text-sm text-muted-foreground flex items-center gap-2"><Loader2 className="h-4 w-4 animate-spin" /> Yükleniyor…</div>
        : accounts.length === 0 ? (
          <Card><CardContent className="p-6 text-sm text-muted-foreground text-center">
            Henüz bağlı hesap yok. Meta ve X uygulamaları Railway env'inde tanımlıysa yukarıdaki düğmelerle bağlayın; geri dönüş adresleri aşağıda.
          </CardContent></Card>
        ) : (
          <div className="grid gap-3 lg:grid-cols-2">
            {accounts.map(a => <AccountCard key={a.id} account={a} languages={languages} />)}
          </div>
        )}
      </section>

      {/* Yapılandırma */}
      {config && (
        <Card>
          <CardContent className="p-4 space-y-3">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <h2 className="text-base font-semibold text-foreground">Yayın ayarları</h2>
              <Button size="sm" variant="outline" disabled={run.isPending} onClick={onRun} title="Zamanlayıcıyı beklemeden bir döngü koştur (normal kurallarla)">
                {run.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <PlayCircle className="h-4 w-4" />} Şimdi çalıştır
              </Button>
            </div>
            <div className="grid gap-x-6 gap-y-1 sm:grid-cols-2 lg:grid-cols-4 text-xs">
              <div><span className="text-muted-foreground">Yayın saati</span><div className="text-sm font-medium">{config.publishHourLocal}:00 {config.timeZone}</div></div>
              <div><span className="text-muted-foreground">Kontrol sıklığı</span><div className="text-sm font-medium">{config.intervalMinutes} dk</div></div>
              <div><span className="text-muted-foreground">Gecikme tavanı</span><div className="text-sm font-medium">{config.maxLateDays} gün (sonrası bayat, yayınlanmaz)</div></div>
              <div><span className="text-muted-foreground">Publisher</span><div className="text-sm font-medium">{config.publisherEnabled ? 'Açık' : 'Kapalı'}</div></div>
            </div>
            <div className="space-y-1.5 pt-1">
              <CopyField label="Meta redirect URI" value={config.metaRedirectUri} />
              <CopyField label="X redirect URI" value={config.xRedirectUri} />
              <CopyField label="Görsel kök adresi" value={config.publicApiBaseUrl} />
            </div>
            <p className="text-[11px] text-muted-foreground">
              Env: <code>Social__Meta__AppId</code>, <code>Social__Meta__AppSecret</code>, <code>Social__X__ClientId</code>, <code>Social__X__ClientSecret</code>, <code>Social__TokenKey</code>,
              <code> SocialPosts__PublicApiBaseUrl</code>, <code>SocialPublisher__PublishHourLocal</code>, <code>SocialPublisher__MaxLateDays</code>, <code>SocialPublisher__IntervalMinutes</code>. Redirect URI'lar geliştirici konsollarına birebir girilir.
            </p>
            {status?.lastRun && (
              <div className="text-xs text-muted-foreground flex flex-wrap items-center gap-x-3 gap-y-1 border-t border-border pt-2">
                <span className="inline-flex items-center gap-1"><Clock className="h-3.5 w-3.5" /> Son döngü {fmtDateTime(status.lastRun.finishedAt)}</span>
                {status.lastRun.ran
                  ? <span>gönderi {status.lastRun.postsSeen} · yeni iş {status.lastRun.jobsCreated} · yayınlandı {status.lastRun.jobsSucceeded} · geçici hata {status.lastRun.jobsFailed} · bloke {status.lastRun.jobsBlocked} · damga {status.lastRun.stamped}</span>
                  : <span>koşmadı: {status.lastRun.notes[0] ?? status.lastRun.error}</span>}
                {status.lastRun.error && <span className="text-red-400">{status.lastRun.error}</span>}
              </div>
            )}
          </CardContent>
        </Card>
      )}

      {/* Yayın günlüğü */}
      <section className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="text-base font-semibold text-foreground">Yayın günlüğü</h2>
          <div className="flex flex-wrap gap-1">
            {[['', 'Tümü'], ['Pending', 'Bekliyor'], ['Failed', 'Yeniden denenecek'], ['Blocked', 'Bloke'], ['Succeeded', 'Yayınlandı'], ['Skipped', 'Atlandı']].map(([v, l]) => (
              <button key={v} type="button" onClick={() => setJobFilter(v)}
                className={cn('rounded-full border px-2.5 py-0.5 text-xs font-medium', jobFilter === v ? 'bg-primary text-primary-foreground border-primary' : 'border-border text-muted-foreground hover:text-foreground')}>
                {l}{status && v === 'Blocked' && status.counts.blocked > 0 ? ` (${status.counts.blocked})` : ''}
              </button>
            ))}
          </div>
        </div>
        <Card>
          <CardContent className="p-0">
            {jobs.length === 0 ? (
              <div className="py-10 text-center text-sm text-muted-foreground">Kayıt yok. İşler, onaylı gönderinin yayın günü saati gelince açılır.</div>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="text-left text-xs text-muted-foreground border-b border-border">
                      <th className="px-4 py-2.5 font-medium">Yayın günü</th>
                      <th className="px-4 py-2.5 font-medium">Gönderi</th>
                      <th className="px-4 py-2.5 font-medium">Hesap</th>
                      <th className="px-4 py-2.5 font-medium">Durum</th>
                      <th className="px-4 py-2.5 font-medium">Deneme</th>
                      <th className="px-4 py-2.5 font-medium">Sonuç</th>
                      <th className="px-4 py-2.5 font-medium text-right">İşlem</th>
                    </tr>
                  </thead>
                  <tbody>
                    {jobs.map(j => (
                      <tr key={j.id} className="border-b border-border/50 last:border-0 align-top">
                        <td className="px-4 py-2.5 whitespace-nowrap tabular-nums">{fmtDay(j.scheduledFor)}</td>
                        <td className="px-4 py-2.5 max-w-[320px]"><div className="truncate" title={j.topic ?? j.postId}>{j.topic ?? j.postId}</div><div className="text-[11px] text-muted-foreground">{j.kind} · {j.language}</div></td>
                        <td className="px-4 py-2.5"><div className="flex items-center gap-1.5"><ProviderIcon provider={j.provider} className="text-muted-foreground" /><span>{j.accountLabel}</span></div><div className="text-[11px] text-muted-foreground">{j.accountName}</div></td>
                        <td className="px-4 py-2.5"><JobStatusPill status={j.status} />{j.status === 'Failed' && j.nextAttemptAt && <div className="text-[11px] text-muted-foreground mt-1">sonraki: {fmtDateTime(j.nextAttemptAt)}</div>}</td>
                        <td className="px-4 py-2.5 tabular-nums">{j.attempts}</td>
                        <td className="px-4 py-2.5 max-w-[360px] text-xs">
                          {j.status === 'Succeeded'
                            ? <span className="text-muted-foreground">{fmtDateTime(j.finishedAt)} · <span className="font-mono">#{j.platformPostId}</span></span>
                            : j.lastError ? <span className="text-red-400 break-words" title={j.lastError}>{j.lastError}</span> : <span className="text-muted-foreground">—</span>}
                        </td>
                        <td className="px-4 py-2.5 text-right whitespace-nowrap">
                          {(j.status === 'Failed' || j.status === 'Blocked' || j.status === 'Skipped') && (
                            <Button size="sm" variant="outline" className="mr-1" disabled={retry.isPending} onClick={() => onRetry(j)} title="Hemen yeniden kuyruğa al"><RotateCcw className="h-3.5 w-3.5" /></Button>
                          )}
                          {(j.status === 'Failed' || j.status === 'Blocked' || j.status === 'Pending') && (
                            <Button size="sm" variant="ghost" disabled={skip.isPending} onClick={() => onSkip(j)} title="Bu platformda yayınlanmasın; damgayı engellemesin"><SkipForward className="h-3.5 w-3.5" /></Button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </CardContent>
        </Card>
      </section>

      {metaState && <MetaCompleteDialog state={metaState} languages={languages} onClose={() => setMetaState(null)} />}
    </div>
  );
}
