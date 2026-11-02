import { useState } from 'react';
import { MailCheck, Send, Loader2, AlertTriangle, Check, X, Eye, Clock, Info, Play, ChevronLeft, ChevronRight, FlaskConical } from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '@/components/ui/dialog';
import { cn } from '@/lib/utils';
import { useToast } from '@/hooks/use-toast';
import { useAuthStore } from '@/stores/authStore';
import {
  useLifecycleStatus, useLifecycleCandidates, useLifecycleMails, useLifecyclePreview, useSendLifecycleMail, useRunLifecycleCycle, useSendLifecycleTest,
  type LifecycleStep, type LifecycleCandidate,
} from '@/api/lifecycleMails';

const STEP_LABEL: Record<string, string> = { Welcome: 'Hoş geldin', CheckIn: 'Nasıl gidiyor', TrialEnded: 'Deneme bitti' };
const STEP_STYLE: Record<string, string> = {
  Welcome: 'bg-sky-500/15 text-sky-300 border-sky-500/30',
  CheckIn: 'bg-violet-500/15 text-violet-300 border-violet-500/30',
  TrialEnded: 'bg-amber-500/15 text-amber-300 border-amber-500/30',
};
const STEPS: LifecycleStep[] = ['Welcome', 'CheckIn', 'TrialEnded'];
const STATUS_LABEL: Record<string, string> = { Demo: 'Demo', Passive: 'Pasif (deneme bitti)', Active: 'Aktif', Lead: 'Lead', Churned: 'Churn' };
const SMTP_SOURCE_LABEL: Record<string, string> = {
  LifecycleMail: 'LifecycleMail__Smtp ayarı',
  EmailCollector: 'destek posta kutusu (EmailCollector IMAP bilgileri, SMTP türetildi)',
  Notifications: 'bildirim SMTP\'si (Notifications__Smtp — destek kutusu tanımlı değil)',
};

function errorMessage(err: unknown, fallback: string): string {
  if (err && typeof err === 'object' && 'response' in err && err.response && typeof err.response === 'object' && 'data' in err.response) {
    const data = (err.response as { data?: { message?: string; errors?: string[] } }).data;
    if (data?.errors?.length) return data.errors[0];
    if (data?.message) return data.message;
  }
  return fallback;
}
const fmt = (iso: string | null | undefined) => iso ? new Date(iso).toLocaleString('tr-TR', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }) : '—';
const fmtDay = (iso: string | null | undefined) => iso ? new Date(iso).toLocaleDateString('tr-TR', { day: '2-digit', month: 'short', year: 'numeric' }) : '—';

function StepPill({ step, small }: { step: string; small?: boolean }) {
  return <span className={cn('inline-flex items-center rounded-full border font-semibold whitespace-nowrap', small ? 'px-1.5 py-0 text-[10px]' : 'px-2 py-0.5 text-[11px]', STEP_STYLE[step] ?? 'bg-slate-500/15 text-slate-300 border-slate-500/30')}>{STEP_LABEL[step] ?? step}</span>;
}

function PreviewDialog({ customerId, step, companyName, onClose }: { customerId: string; step: LifecycleStep; companyName: string; onClose: () => void }) {
  const { data, isLoading, isError, error } = useLifecyclePreview(customerId, step);
  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="sm:max-w-3xl max-h-[94vh] overflow-y-auto">
        <DialogHeader><DialogTitle>{STEP_LABEL[step]} — {companyName}</DialogTitle><DialogDescription>{data?.subject ?? (isLoading ? 'Yükleniyor…' : '')}</DialogDescription></DialogHeader>
        {isError && <div className="text-sm text-red-400">{errorMessage(error, 'Ön izleme alınamadı.')}</div>}
        {data && <iframe title="preview" srcDoc={data.html} sandbox="" className="w-full h-[70vh] rounded border border-border bg-white" />}
        <DialogFooter><Button variant="outline" size="sm" onClick={onClose}>Kapat</Button></DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function TestMailDialog({ languages, onClose }: { languages: string[]; onClose: () => void }) {
  const { toast } = useToast();
  const myEmail = useAuthStore((s) => s.user?.email ?? '');
  const [to, setTo] = useState(myEmail);
  const [step, setStep] = useState<LifecycleStep>('Welcome');
  const [language, setLanguage] = useState(languages.includes('en') ? 'en' : (languages[0] ?? 'en'));
  const [last, setLast] = useState<string | null>(null);
  const test = useSendLifecycleTest();
  const valid = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(to.trim());

  async function onSend() {
    try {
      const r = await test.mutateAsync({ to: to.trim(), step, language });
      setLast(`${STEP_LABEL[step]} [${r.language}] → ${r.to} · gönderen ${r.from ?? '—'}`);
      toast({ title: 'Deneme e-postası gönderildi', description: r.subject ?? '' });
    } catch (err) { toast({ title: 'Gönderilemedi', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2"><FlaskConical className="h-4 w-4" /> Deneme e-postası</DialogTitle>
          <DialogDescription>Seçilen adım örnek verilerle (Apex Lifts Ltd, Jane Carter) verilen adrese gider; konu "[TEST]" ile başlar. Müşteri kaydı tutulmaz — SMTP ve şablon kontrolü için.</DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <div className="space-y-1"><Label>Alıcı</Label><Input type="email" value={to} onChange={e => setTo(e.target.value)} placeholder="ad@firma.com" /></div>
          <div className="space-y-1">
            <Label>Adım</Label>
            <div className="flex flex-wrap gap-1.5">{STEPS.map(s => <button key={s} type="button" onClick={() => setStep(s)} className={cn('rounded-full border px-2.5 py-0.5 text-xs', step === s ? 'bg-primary text-primary-foreground border-primary' : 'border-border text-muted-foreground')}>{STEP_LABEL[s]}</button>)}</div>
          </div>
          <div className="space-y-1">
            <Label>Dil</Label>
            <div className="flex flex-wrap gap-1.5">{(languages.length ? languages : ['en']).map(l => <button key={l} type="button" onClick={() => setLanguage(l)} className={cn('rounded-full border px-2.5 py-0.5 text-xs uppercase', language === l ? 'bg-primary text-primary-foreground border-primary' : 'border-border text-muted-foreground')}>{l}</button>)}</div>
          </div>
          {last && <div className="text-xs text-emerald-400 flex items-center gap-1"><Check className="h-3 w-3" /> {last}</div>}
        </div>
        <DialogFooter>
          <Button variant="outline" size="sm" onClick={onClose}>Kapat</Button>
          <Button size="sm" disabled={!valid || test.isPending} onClick={onSend}>{test.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />} Gönder</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

export function LifecycleMailsPage() {
  const { toast } = useToast();
  const navigate = useNavigate();
  const { data: st } = useLifecycleStatus();
  const { data: candidates = [], isLoading: candLoading } = useLifecycleCandidates();
  const [stepFilter, setStepFilter] = useState<LifecycleStep | ''>('');
  const [page, setPage] = useState(1);
  const { data: log, isLoading: logLoading, isError, error } = useLifecycleMails(stepFilter, page);
  const send = useSendLifecycleMail();
  const run = useRunLifecycleCycle();
  const [preview, setPreview] = useState<{ customerId: string; step: LifecycleStep; companyName: string } | null>(null);
  const [confirmSend, setConfirmSend] = useState<{ c: LifecycleCandidate; step: LifecycleStep; force: boolean } | null>(null);
  const [testOpen, setTestOpen] = useState(false);
  const totalPages = log ? Math.max(1, Math.ceil(log.totalCount / log.pageSize)) : 1;

  async function onRun() {
    try { const r = await run.mutateAsync(); toast({ title: 'Döngü çalıştı', description: `${r.candidates} sırada · ${r.sent} gönderildi · ${r.failed} hata · ${r.deferred} saat dışı ertelendi${r.notes.length ? ` — ${r.notes[0]}` : ''}` }); }
    catch (err) { toast({ title: 'Olmadı', description: errorMessage(err, 'Döngü çalıştırılamadı.'), variant: 'destructive' }); }
  }
  async function onSend() {
    if (!confirmSend) return;
    try { await send.mutateAsync({ customerId: confirmSend.c.customerId, step: confirmSend.step, force: confirmSend.force }); toast({ title: 'Gönderildi', description: `${STEP_LABEL[confirmSend.step]} → ${confirmSend.c.email}` }); setConfirmSend(null); }
    catch (err) { toast({ title: 'Gönderilemedi', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }

  const due = candidates.filter(c => c.dueStep);
  const skipped = candidates.filter(c => !c.dueStep);

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-foreground flex items-center gap-2"><MailCheck className="h-6 w-6" /> Demo E-postaları</h1>
          <p className="text-sm text-muted-foreground mt-1">Türkiye dışında açılan Liftdesk deneme hesaplarına üç otomatik e-posta: kayıt sonrası hoş geldin, deneme ortasında "nasıl gidiyor", deneme bitip satın alınmadıysa hatırlatma. Dil müşterinin ülkesinden seçilir, gönderen firma destek posta kutusudur. Her adım bir müşteriye yalnız bir kez gider; her gönderim müşteri zaman çizelgesine de yazılır.</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" size="sm" onClick={() => setTestOpen(true)} disabled={!!st && !st.smtpConfigured} title="Şablonu örnek verilerle kendinize gönderin"><FlaskConical className="h-4 w-4" /> Deneme e-postası</Button>
          <Button variant="outline" size="sm" disabled={run.isPending} onClick={onRun}><Play className="h-4 w-4" /> Döngüyü şimdi çalıştır</Button>
        </div>
      </div>

      {st && !st.smtpConfigured && (
        <div className="flex items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/5 px-3 py-2 text-xs text-amber-200">
          <AlertTriangle className="h-3.5 w-3.5 shrink-0 mt-0.5" /> <span>SMTP tanımlı değil — gönderen hesap sırayla <code>LifecycleMail__Smtp__Host/Username/Password/From</code>, yoksa destek kutusunun <code>EmailCollector__Imap__Host/Username/Password</code> bilgileri (SMTP sunucusu IMAP'ten türetilir), yoksa <code>Notifications__Smtp__*</code>. İsteğe bağlı <code>LifecycleMail__SupportEmail</code>. Tanımlanana kadar hiçbir şey gönderilmez.</span>
        </div>
      )}
      {st && !st.enabled && <div className="flex items-start gap-2 rounded-md border border-red-500/40 bg-red-500/5 px-3 py-2 text-xs text-red-200"><AlertTriangle className="h-3.5 w-3.5 shrink-0 mt-0.5" /> <span>Otomatik gönderim kapalı (<code>LifecycleMail__Enabled=false</code>).</span></div>}

      {st && (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <Card><CardContent className="p-3 text-xs space-y-1">
            <div className="text-muted-foreground">Gönderen</div>
            <div className="font-medium text-foreground break-all">{st.from ?? '—'}</div>
            {st.smtpSource && <div className={cn('text-[11px]', st.smtpSource === 'Notifications' ? 'text-amber-300' : 'text-muted-foreground')}>Kaynak: {SMTP_SOURCE_LABEL[st.smtpSource] ?? st.smtpSource}</div>}
            <div className="text-muted-foreground">Şablon dilleri: {st.languages.length ? st.languages.join(', ') : '—'} · ülke dili yoksa en</div>
          </CardContent></Card>
          <Card><CardContent className="p-3 text-xs space-y-1">
            <div className="text-muted-foreground">Zamanlama</div>
            <div className="text-foreground">Hoş geldin: kayıt sonrası ilk döngü</div>
            <div className="text-foreground">Nasıl gidiyor: +{st.options.checkInAfterDays} gün · Hatırlatma: bitişten +{st.options.trialEndedAfterDays} gün</div>
            <div className="text-muted-foreground">Yerel saat {st.options.sendHourStart}:00–{st.options.sendHourEnd}:00 · kapsam dışı: {st.options.excludeCountries}</div>
          </CardContent></Card>
          <Card><CardContent className="p-3 text-xs space-y-1">
            <div className="text-muted-foreground">Gönderilen</div>
            <div className="flex flex-wrap gap-2">{STEPS.map(s => <span key={s} className="inline-flex items-center gap-1"><StepPill step={s} small /><span className="tabular-nums text-foreground">{st.sentByStep[s] ?? 0}</span></span>)}</div>
          </CardContent></Card>
          <Card><CardContent className="p-3 text-xs space-y-1">
            <div className="text-muted-foreground">Son döngü</div>
            {st.lastRun ? <div className="text-foreground">{fmt(st.lastRun.finishedAt)} · {st.lastRun.candidates} sırada · {st.lastRun.sent} gönderildi · {st.lastRun.failed} hata · {st.lastRun.deferred} ertelendi</div> : <div className="text-muted-foreground">Bu süreçte henüz koşmadı (saatlik).</div>}
            {st.lastRun?.notes?.length ? <div className="text-amber-300 truncate" title={st.lastRun.notes.join(' | ')}>{st.lastRun.notes[0]}</div> : null}
          </CardContent></Card>
        </div>
      )}

      {/* Adaylar */}
      <Card><CardContent className="p-0">
        <div className="px-3 py-2.5 border-b border-border text-sm font-semibold text-foreground flex items-center gap-2"><Clock className="h-4 w-4" /> Sıradakiler <span className="text-xs font-normal text-muted-foreground">({due.length} gönderilecek · {skipped.length} beklemede/kapsam dışı)</span></div>
        {candLoading ? <div className="p-4 text-sm text-muted-foreground flex items-center gap-2"><Loader2 className="h-4 w-4 animate-spin" /> Yükleniyor…</div> : candidates.length === 0 ? (
          <div className="py-10 text-center text-sm text-muted-foreground flex flex-col items-center gap-2"><Info className="h-6 w-6" /> Son {st?.options.maxAgeDays ?? 14} günde TR dışı deneme hesabı yok. Liftdesk feed'i ülke kodunu gönderdikçe burada görünür.</div>
        ) : (
          <div className="overflow-x-auto">
            <table className="table-stack w-full text-sm">
              <thead><tr className="text-left text-xs text-muted-foreground border-b border-border">
                <th className="px-3 py-2.5 font-medium">Firma</th><th className="px-3 py-2.5 font-medium">E-posta</th><th className="px-3 py-2.5 font-medium">Ülke / dil</th><th className="px-3 py-2.5 font-medium">Durum</th><th className="px-3 py-2.5 font-medium">Kayıt / bitiş</th><th className="px-3 py-2.5 font-medium">Gönderilen</th><th className="px-3 py-2.5 font-medium">Sıradaki</th><th className="px-3 py-2.5 font-medium"></th>
              </tr></thead>
              <tbody>{[...due, ...skipped].map(c => (
                <tr key={c.customerId} className="border-b border-border/50 last:border-0 align-top">
                  <td data-stack="primary" data-label="Firma" className="px-3 py-2.5"><button type="button" className="font-medium hover:underline text-left" onClick={() => navigate(`/customers/${c.customerId}`)}>{c.companyName}</button></td>
                  <td data-label="E-posta" className="px-3 py-2.5 text-xs text-muted-foreground">{c.email ?? <span className="text-amber-400/80">yok</span>}</td>
                  <td data-label="Ülke / dil" className="px-3 py-2.5 text-xs">{c.country ?? '—'} · {c.language}</td>
                  <td data-label="Durum" className="px-3 py-2.5 text-xs">{STATUS_LABEL[c.status] ?? c.status}</td>
                  <td data-label="Kayıt / bitiş" className="px-3 py-2.5 text-xs text-muted-foreground whitespace-nowrap">{fmtDay(c.createdAt)} → {fmtDay(c.expirationDate)}</td>
                  <td data-label="Gönderilen" className="px-3 py-2.5"><div className="flex flex-wrap gap-1">{c.sentSteps.length ? c.sentSteps.map(s => <StepPill key={s} step={s} small />) : <span className="text-xs text-muted-foreground">—</span>}</div></td>
                  <td data-label="Sıradaki" className="px-3 py-2.5 text-xs">
                    {c.dueStep ? <span className="inline-flex items-center gap-1.5"><StepPill step={c.dueStep} />{c.inWindow ? <span className="text-emerald-400">bu döngüde</span> : <span className="text-muted-foreground" title="Müşterinin yerel saatinde gönderim penceresi dışında">saat bekliyor</span>}</span>
                      : <span className="text-muted-foreground">{c.skipReason ?? 'bekliyor'}</span>}
                  </td>
                  <td data-stack="actions" data-label="" className="px-3 py-2.5 text-right whitespace-nowrap">
                    <Button size="sm" variant="ghost" className="h-8" onClick={() => setPreview({ customerId: c.customerId, step: c.dueStep ?? STEPS.find(s => !c.sentSteps.includes(s)) ?? 'Welcome', companyName: c.companyName })} title="Ön izleme"><Eye className="h-3.5 w-3.5" /></Button>
                    {c.dueStep && !c.skipReason && <Button size="sm" variant="outline" className="h-8" disabled={send.isPending} onClick={() => setConfirmSend({ c, step: c.dueStep!, force: false })}><Send className="h-3.5 w-3.5" /> Şimdi gönder</Button>}
                    {!c.dueStep && !c.skipReason && STEPS.some(s => !c.sentSteps.includes(s)) && (
                      <Button size="sm" variant="ghost" className="h-8 text-muted-foreground" disabled={send.isPending} onClick={() => setConfirmSend({ c, step: STEPS.find(s => !c.sentSteps.includes(s))!, force: true })} title="Kuralı beklemeden gönder">Zorla</Button>
                    )}
                  </td>
                </tr>
              ))}</tbody>
            </table>
          </div>
        )}
      </CardContent></Card>

      {/* Geçmiş */}
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-sm font-semibold text-foreground">Gönderim geçmişi</span>
        <button type="button" onClick={() => { setStepFilter(''); setPage(1); }} className={cn('rounded-full border px-2.5 py-0.5 text-xs', stepFilter === '' ? 'bg-primary text-primary-foreground border-primary' : 'border-border text-muted-foreground')}>Tümü</button>
        {STEPS.map(s => <button key={s} type="button" onClick={() => { setStepFilter(s); setPage(1); }} className={cn('rounded-full border px-2.5 py-0.5 text-xs', stepFilter === s ? 'bg-primary text-primary-foreground border-primary' : 'border-border text-muted-foreground')}>{STEP_LABEL[s]}</button>)}
      </div>
      {isError && <div className="text-sm text-red-400 flex items-center gap-2"><AlertTriangle className="h-4 w-4" /> {errorMessage(error, 'Geçmiş alınamadı.')}</div>}
      <Card><CardContent className="p-0">
        {logLoading ? <div className="p-4 text-sm text-muted-foreground flex items-center gap-2"><Loader2 className="h-4 w-4 animate-spin" /> Yükleniyor…</div> : !log || log.items.length === 0 ? (
          <div className="py-10 text-center text-sm text-muted-foreground">Henüz gönderim yok.</div>
        ) : (
          <div className="overflow-x-auto">
            <table className="table-stack w-full text-sm">
              <thead><tr className="text-left text-xs text-muted-foreground border-b border-border">
                <th className="px-3 py-2.5 font-medium">Tarih</th><th className="px-3 py-2.5 font-medium">Firma</th><th className="px-3 py-2.5 font-medium">Adım</th><th className="px-3 py-2.5 font-medium">Konu</th><th className="px-3 py-2.5 font-medium">Alıcı</th><th className="px-3 py-2.5 font-medium">Durum</th><th className="px-3 py-2.5 font-medium">Tetik</th>
              </tr></thead>
              <tbody>{log.items.map(r => (
                <tr key={r.id} className="border-b border-border/50 last:border-0 align-top">
                  <td data-label="Tarih" className="px-3 py-2.5 text-xs text-muted-foreground whitespace-nowrap">{fmt(r.sentAt)}</td>
                  <td data-stack="primary" data-label="Firma" className="px-3 py-2.5"><button type="button" className="font-medium hover:underline text-left" onClick={() => navigate(`/customers/${r.customerId}`)}>{r.companyName}</button><div className="text-[11px] text-muted-foreground">{r.country ?? '—'} · {r.language}{r.customerStatus ? ` · ${STATUS_LABEL[r.customerStatus] ?? r.customerStatus}` : ''}</div></td>
                  <td data-label="Adım" className="px-3 py-2.5"><StepPill step={r.step} /></td>
                  <td data-label="Konu" className="px-3 py-2.5 text-xs">{r.subject}</td>
                  <td data-label="Alıcı" className="px-3 py-2.5 text-xs text-muted-foreground">{r.to}</td>
                  <td data-label="Durum" className="px-3 py-2.5 text-xs">{r.status === 'Sent' ? <span className="text-emerald-400 inline-flex items-center gap-1"><Check className="h-3 w-3" /> Gönderildi</span> : <span className="text-red-400 inline-flex items-center gap-1" title={r.error ?? ''}><X className="h-3 w-3" /> {r.error ?? 'Hata'}</span>}</td>
                  <td data-label="Tetik" className="px-3 py-2.5 text-xs text-muted-foreground">{r.triggeredBy}</td>
                </tr>
              ))}</tbody>
            </table>
          </div>
        )}
      </CardContent></Card>
      {log && log.totalCount > log.pageSize && (
        <div className="flex items-center justify-between text-sm text-muted-foreground">
          <span>{log.totalCount} kayıt · sayfa {page}/{totalPages}</span>
          <div className="flex gap-2">
            <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage(p => p - 1)}><ChevronLeft className="h-4 w-4" /> Önceki</Button>
            <Button variant="outline" size="sm" disabled={page >= totalPages} onClick={() => setPage(p => p + 1)}>Sonraki <ChevronRight className="h-4 w-4" /></Button>
          </div>
        </div>
      )}

      {preview && <PreviewDialog customerId={preview.customerId} step={preview.step} companyName={preview.companyName} onClose={() => setPreview(null)} />}
      {testOpen && <TestMailDialog languages={st?.languages ?? []} onClose={() => setTestOpen(false)} />}
      {confirmSend && (
        <Dialog open onOpenChange={(o) => !o && setConfirmSend(null)}>
          <DialogContent className="sm:max-w-md">
            <DialogHeader><DialogTitle>{STEP_LABEL[confirmSend.step]} e-postasını gönder</DialogTitle><DialogDescription>{confirmSend.c.companyName} · {confirmSend.c.email} · dil {confirmSend.c.language}{confirmSend.force ? ' · kural beklenmeden (zorla)' : ''}</DialogDescription></DialogHeader>
            <DialogFooter><Button variant="outline" size="sm" onClick={() => setConfirmSend(null)}>Vazgeç</Button><Button size="sm" disabled={send.isPending} onClick={onSend}>{send.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />} Gönder</Button></DialogFooter>
          </DialogContent>
        </Dialog>
      )}
    </div>
  );
}
