import { useEffect, useMemo, useState } from 'react';
import {
  Globe, Search, Check, X, ExternalLink, Loader2, AlertTriangle, Phone, Mail, MapPin, RotateCcw, Bot, ChevronLeft, ChevronRight, Info,
  Send, Pause, Play, Inbox, Ban, Reply, Clock, Paperclip, Settings2,
} from 'lucide-react';
import { useNavigate } from 'react-router-dom';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '@/components/ui/dialog';
import { cn } from '@/lib/utils';
import { useToast } from '@/hooks/use-toast';
import {
  useGlobalLeads, useGlobalLeadMeta, useGlobalLead, useGlobalAgentRuns, useUpdateGlobalLead, useApproveGlobalLead, useApproveManyGlobalLeads,
  useRejectGlobalLead, useRejectManyGlobalLeads, useReopenGlobalLead, useOutreachAction, useStopOutreach, useRunOutreachCycle,
  type GlobalLeadStatus, type OutreachStatus, type GlobalLeadMeta,
} from '@/api/globalLeads';

const STATUS_TABS: { value: GlobalLeadStatus; label: string; key: 'pending' | 'approved' | 'rejected' | 'duplicate' }[] = [
  { value: 'Pending', label: 'Bekleyen', key: 'pending' },
  { value: 'Approved', label: 'Onaylı / Outreach', key: 'approved' },
  { value: 'Rejected', label: 'Reddedilen', key: 'rejected' },
  { value: 'Duplicate', label: 'Mükerrer', key: 'duplicate' },
];

const STATUS_STYLE: Record<string, string> = {
  Pending: 'bg-yellow-500/15 text-yellow-400 border-yellow-500/30',
  Approved: 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30',
  Rejected: 'bg-red-500/15 text-red-400 border-red-500/30',
  Duplicate: 'bg-slate-500/15 text-slate-300 border-slate-500/30',
};
const STATUS_LABEL: Record<string, string> = { Pending: 'Bekleyen', Approved: 'Onaylandı', Rejected: 'Reddedildi', Duplicate: 'Mükerrer' };

const OUTREACH_LABEL: Record<string, string> = {
  NotStarted: 'Başlamadı', Active: 'Dizi aktif', Replied: 'Yanıtladı', Bounced: 'Bounce', Unsubscribed: 'Abonelikten çıktı', Completed: 'Tamamlandı', Paused: 'Duraklatıldı', Failed: 'Hata',
};
const OUTREACH_STYLE: Record<string, string> = {
  NotStarted: 'bg-slate-500/15 text-slate-300 border-slate-500/30',
  Active: 'bg-sky-500/15 text-sky-300 border-sky-500/30',
  Replied: 'bg-emerald-500/15 text-emerald-300 border-emerald-500/30',
  Bounced: 'bg-red-500/15 text-red-300 border-red-500/30',
  Unsubscribed: 'bg-zinc-500/15 text-zinc-300 border-zinc-500/30',
  Completed: 'bg-violet-500/15 text-violet-300 border-violet-500/30',
  Paused: 'bg-amber-500/15 text-amber-300 border-amber-500/30',
  Failed: 'bg-red-500/15 text-red-300 border-red-500/30',
};
const OUTREACH_FILTERS: OutreachStatus[] = ['Active', 'Replied', 'Completed', 'Paused', 'Failed', 'Bounced', 'Unsubscribed', 'NotStarted'];

const COUNTRY_NAME: Record<string, string> = { GB: 'Birleşik Krallık', IE: 'İrlanda', DE: 'Almanya', FR: 'Fransa', NL: 'Hollanda', ES: 'İspanya', IT: 'İtalya', US: 'ABD' };
const countryName = (c: string) => COUNTRY_NAME[c] ?? c;

function errorMessage(err: unknown, fallback: string): string {
  if (err && typeof err === 'object' && 'response' in err && err.response && typeof err.response === 'object' && 'data' in err.response) {
    const data = (err.response as { data?: { message?: string; errors?: string[] } }).data;
    if (data?.errors?.length) return data.errors[0];
    if (data?.message) return data.message;
  }
  return fallback;
}
function fmt(iso: string | null | undefined): string {
  return iso ? new Date(iso).toLocaleString('tr-TR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '—';
}
function fmtShort(iso: string | null | undefined): string {
  return iso ? new Date(iso).toLocaleString('tr-TR', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }) : '—';
}

function ConfidenceBar({ value }: { value: number }) {
  return (
    <div className="flex items-center gap-1.5" title={`Güven ${value}/100`}>
      <div className="h-1.5 w-14 rounded bg-muted/60 overflow-hidden"><div className={cn('h-full', value >= 75 ? 'bg-emerald-500' : value >= 50 ? 'bg-amber-500' : 'bg-red-500')} style={{ width: `${value}%` }} /></div>
      <span className="text-[11px] tabular-nums text-muted-foreground">{value}</span>
    </div>
  );
}

function OutreachPill({ status, step, total }: { status: string; step?: number; total?: number }) {
  return (
    <span className={cn('inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-[11px] font-semibold whitespace-nowrap', OUTREACH_STYLE[status] ?? OUTREACH_STYLE.NotStarted)}>
      {OUTREACH_LABEL[status] ?? status}{typeof step === 'number' && total ? <span className="opacity-70">{Math.min(step, total)}/{total}</span> : null}
    </span>
  );
}

// ── Detay diyaloğu ───────────────────────────────────────────────────────────

function LeadDialog({ leadId, meta, onClose }: { leadId: string; meta: GlobalLeadMeta; onClose: () => void }) {
  const { toast } = useToast();
  const navigate = useNavigate();
  const { data, isLoading } = useGlobalLead(leadId);
  const update = useUpdateGlobalLead();
  const approve = useApproveGlobalLead();
  const reject = useRejectGlobalLead();
  const reopen = useReopenGlobalLead();
  const action = useOutreachAction();
  const stop = useStopOutreach();
  const l = data?.lead;
  const [form, setForm] = useState({ companyName: '', contactName: '', email: '', phone: '', website: '', city: '', notes: '' });
  const [rejectReason, setRejectReason] = useState('');
  const [startOutreach, setStartOutreach] = useState(true);
  const busy = update.isPending || approve.isPending || reject.isPending || reopen.isPending || action.isPending || stop.isPending;
  const totalSteps = meta.sequence.steps.length;

  useEffect(() => {
    if (!l) return;
    setForm({ companyName: l.companyName, contactName: l.contactName ?? '', email: l.email ?? '', phone: l.phone ?? '', website: l.website ?? '', city: l.city ?? '', notes: l.notes ?? '' });
  }, [l]);

  if (!l) {
    return (
      <Dialog open onOpenChange={(o) => !o && onClose()}>
        <DialogContent className="sm:max-w-2xl"><DialogHeader><DialogTitle>Lead</DialogTitle><DialogDescription>{isLoading ? 'Yükleniyor…' : 'Bulunamadı.'}</DialogDescription></DialogHeader></DialogContent>
      </Dialog>
    );
  }
  const lead = l;
  const pending = lead.status === 'Pending';
  const dirty = form.companyName !== lead.companyName || form.contactName !== (lead.contactName ?? '') || form.email !== (lead.email ?? '') || form.phone !== (lead.phone ?? '') || form.website !== (lead.website ?? '') || form.city !== (lead.city ?? '') || form.notes !== (lead.notes ?? '');

  async function saveEdits() {
    if (!dirty) return;
    await update.mutateAsync({ id: lead.id, companyName: form.companyName.trim(), contactName: form.contactName.trim(), email: form.email.trim(), phone: form.phone.trim(), website: form.website.trim(), city: form.city.trim(), notes: form.notes });
  }
  async function onSave() {
    try { await saveEdits(); toast({ title: 'Kaydedildi' }); }
    catch (err) { toast({ title: 'Kaydedilemedi', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function onApprove() {
    if (!form.companyName.trim()) { toast({ title: 'Firma adı gerekli', variant: 'destructive' }); return; }
    if (startOutreach && !form.email.trim()) { toast({ title: 'E-posta gerekli', description: 'Outreach e-posta ile gider; adresi girin ya da outreach’siz onaylayın.', variant: 'destructive' }); return; }
    try {
      await saveEdits();
      await approve.mutateAsync({ id: lead.id, startOutreach });
      toast({ title: startOutreach ? 'Onaylandı · tanıtım maili sıraya alındı' : 'Onaylandı', description: startOutreach ? `${form.email.trim()} — gönderim penceresi ${meta.sequence.startHour}:00–${meta.sequence.endHour}:00 ${meta.sequence.timeZone}` : form.companyName.trim() });
      onClose();
    } catch (err) { toast({ title: 'Onaylanamadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function onReject() {
    try { await reject.mutateAsync({ id: lead.id, reason: rejectReason.trim() || undefined }); toast({ title: 'Reddedildi', description: 'Bu firma yeniden önerilmez.' }); onClose(); }
    catch (err) { toast({ title: 'Olmadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function onReopen() {
    try { await reopen.mutateAsync(lead.id); toast({ title: 'Bekleyene alındı' }); onClose(); }
    catch (err) { toast({ title: 'Olmadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function run(a: 'start' | 'pause' | 'resume' | 'send-now' | 'check-inbox', restart = false) {
    try {
      const r = await action.mutateAsync({ id: lead.id, action: a, restart });
      if (a === 'check-inbox' && r && 'replied' in r) toast({ title: r.replied ? 'Yanıt bulundu — dizi durdu' : r.bounced ? 'Bounce bulundu — dizi durdu' : 'Yanıt/bounce yok', description: r.detail ?? undefined });
      else toast({ title: a === 'send-now' ? 'Gönderildi' : a === 'pause' ? 'Duraklatıldı' : a === 'resume' ? 'Sürdürüldü' : 'Dizi başlatıldı' });
    } catch (err) { toast({ title: 'Olmadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function onStop(reason: 'replied' | 'unsubscribed' | 'completed') {
    try { await stop.mutateAsync({ id: lead.id, reason }); toast({ title: 'Dizi durduruldu', description: OUTREACH_LABEL[reason === 'replied' ? 'Replied' : reason === 'unsubscribed' ? 'Unsubscribed' : 'Completed'] }); }
    catch (err) { toast({ title: 'Olmadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }

  const o = lead.outreachStatus;
  return (
    <Dialog open onOpenChange={(op) => !op && !busy && onClose()}>
      <DialogContent className="sm:max-w-3xl max-h-[92vh] overflow-y-auto">
        <DialogHeader>
          <div className="flex flex-wrap items-center gap-2 mb-1">
            <span className={cn('inline-flex items-center rounded-full border px-2 py-0.5 text-[11px] font-semibold', STATUS_STYLE[lead.status])}>{STATUS_LABEL[lead.status] ?? lead.status}</span>
            {lead.status === 'Approved' && <OutreachPill status={o} step={lead.outreachStep} total={totalSteps} />}
            <span className="text-xs text-muted-foreground">{countryName(lead.country)} · {lead.region ?? lead.city ?? '—'} · {lead.source} · {fmt(lead.createdAt)}</span>
            <ConfidenceBar value={lead.confidence} />
          </div>
          <DialogTitle className="text-left">{lead.companyName}</DialogTitle>
          <DialogDescription className="text-left">
            {lead.snippet && <span className="block">{lead.snippet}</span>}
            {lead.sourceUrl && <a href={lead.sourceUrl} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 text-primary hover:underline mt-1 break-all"><ExternalLink className="h-3 w-3" /> {lead.sourceUrl}</a>}
          </DialogDescription>
        </DialogHeader>

        {lead.matchNotes && (
          <div className="rounded-md border border-amber-500/40 bg-amber-500/5 px-3 py-2 text-xs text-amber-200 flex items-start gap-2">
            <AlertTriangle className="h-3.5 w-3.5 shrink-0 mt-0.5" /><span>{lead.matchNotes}</span>
          </div>
        )}
        {lead.status === 'Rejected' && (
          <div className="rounded-md border border-red-500/30 bg-red-500/5 px-3 py-2 text-xs text-red-300">Reddedildi ({lead.reviewedBy} · {fmt(lead.reviewedAt)}){lead.rejectReason ? ` — ${lead.rejectReason}` : ''}</div>
        )}
        {lead.lastError && (
          <div className="rounded-md border border-red-500/30 bg-red-500/5 px-3 py-2 text-xs text-red-300 flex items-start gap-2"><AlertTriangle className="h-3.5 w-3.5 shrink-0 mt-0.5" /><span>{lead.lastError}</span></div>
        )}

        {/* Outreach paneli */}
        {lead.status === 'Approved' && (
          <div className="rounded-md border border-border p-3 space-y-2">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="text-xs font-medium text-foreground inline-flex items-center gap-1.5"><Send className="h-3.5 w-3.5" /> E-posta dizisi</div>
              <div className="text-[11px] text-muted-foreground">
                {data?.sender ? <>Gönderen: <span className="text-foreground">{data.sender}</span> · </> : null}
                {o === 'Active' && lead.nextSendAt && <>Sıradaki: <span className="text-foreground">{fmtShort(lead.nextSendAt)}</span> · </>}
                {lead.lastSentAt && <>Son gönderim: {fmtShort(lead.lastSentAt)} · </>}{lead.sentCount} gönderildi
              </div>
            </div>
            <div className="flex flex-wrap gap-2">
              {(o === 'NotStarted' || o === 'Completed' || o === 'Bounced' || o === 'Replied') && (
                <Button type="button" size="sm" variant="outline" disabled={busy || !meta.sender.ready || !lead.email} onClick={() => run('start', o !== 'NotStarted')} title={!meta.sender.ready ? 'Gmail ayarlarınız eksik' : undefined}>
                  <Play className="h-4 w-4" /> {o === 'NotStarted' ? 'Diziyi başlat' : 'Yeniden başlat'}
                </Button>
              )}
              {o === 'Active' && <Button type="button" size="sm" variant="outline" disabled={busy} onClick={() => run('pause')}><Pause className="h-4 w-4" /> Duraklat</Button>}
              {(o === 'Paused' || o === 'Failed') && <Button type="button" size="sm" variant="outline" disabled={busy} onClick={() => run('resume')}><Play className="h-4 w-4" /> Sürdür</Button>}
              {(o === 'Active' || o === 'Paused' || o === 'Failed' || o === 'NotStarted') && lead.outreachStep < totalSteps && (
                <Button type="button" size="sm" variant="outline" disabled={busy || !lead.email} onClick={() => run('send-now')} title="Pencere ve günlük limit beklenmez"><Send className="h-4 w-4" /> Sıradaki adımı şimdi gönder</Button>
              )}
              {lead.sentCount > 0 && o !== 'Unsubscribed' && <Button type="button" size="sm" variant="outline" disabled={busy} onClick={() => run('check-inbox')}><Inbox className="h-4 w-4" /> Gelen kutusunu kontrol et</Button>}
              {(o === 'Active' || o === 'Paused' || o === 'Failed') && (
                <>
                  <Button type="button" size="sm" variant="ghost" disabled={busy} onClick={() => onStop('replied')}><Reply className="h-4 w-4" /> Yanıtladı</Button>
                  <Button type="button" size="sm" variant="ghost" disabled={busy} onClick={() => onStop('completed')}><Ban className="h-4 w-4" /> Durdur</Button>
                </>
              )}
              {o !== 'Unsubscribed' && <Button type="button" size="sm" variant="ghost" className="text-muted-foreground" disabled={busy} onClick={() => onStop('unsubscribed')} title="Bir daha gönderilmez">Listeden çıkar</Button>}
            </div>
            <div className="text-xs">
              <div className="text-muted-foreground mb-1">Gönderim geçmişi</div>
              {(data?.emails ?? []).length === 0 ? <div className="text-muted-foreground">Henüz gönderim yok.</div> : (
                <table className="table-stack w-full text-xs">
                  <thead><tr className="text-left text-muted-foreground border-b border-border"><th className="py-1 pr-2">Adım</th><th className="py-1 pr-2">Konu</th><th className="py-1 pr-2">Tarih</th><th className="py-1">Durum</th></tr></thead>
                  <tbody>{(data?.emails ?? []).map(e => (
                    <tr key={e.id} className="border-b border-border/40 last:border-0 align-top">
                      <td data-label="Adım" className="py-1 pr-2 whitespace-nowrap">{e.step + 1} · {e.templateKey.replace(/\.en-GB\.html$/, '')}</td>
                      <td data-label="Konu" data-stack="primary" className="py-1 pr-2">{e.subject}</td>
                      <td data-label="Tarih" className="py-1 pr-2 whitespace-nowrap">{fmtShort(e.sentAt)}</td>
                      <td data-label="Durum" className={cn('py-1', e.status === 'Sent' ? 'text-emerald-400' : 'text-red-400')}>{e.status === 'Sent' ? 'Gönderildi' : `Hata: ${e.error ?? ''}`}</td>
                    </tr>
                  ))}</tbody>
                </table>
              )}
            </div>
          </div>
        )}

        {/* Düzenlenebilir alanlar */}
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1 sm:col-span-2"><Label>Firma adı</Label><Input value={form.companyName} onChange={e => setForm(f => ({ ...f, companyName: e.target.value }))} /></div>
          <div className="space-y-1"><Label>Yetkili (opsiyonel — "Dear …")</Label><Input value={form.contactName} onChange={e => setForm(f => ({ ...f, contactName: e.target.value }))} placeholder="Boşsa firma adıyla hitap edilir" /></div>
          <div className="space-y-1"><Label>E-posta (outreach adresi)</Label><Input value={form.email} onChange={e => setForm(f => ({ ...f, email: e.target.value }))} placeholder="info@company.co.uk" /></div>
          <div className="space-y-1"><Label>Telefon</Label><Input value={form.phone} onChange={e => setForm(f => ({ ...f, phone: e.target.value }))} /></div>
          <div className="space-y-1"><Label>Web</Label><Input value={form.website} onChange={e => setForm(f => ({ ...f, website: e.target.value }))} /></div>
          <div className="space-y-1"><Label>Şehir</Label><Input value={form.city} onChange={e => setForm(f => ({ ...f, city: e.target.value }))} /></div>
          <div className="space-y-1 sm:col-span-2"><Label>Not</Label><Textarea value={form.notes} rows={2} onChange={e => setForm(f => ({ ...f, notes: e.target.value }))} /></div>
        </div>
        <div className="flex flex-wrap gap-3 text-xs text-muted-foreground">
          {lead.website && <a href={lead.website.startsWith('http') ? lead.website : `https://${lead.website}`} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 hover:text-primary"><Globe className="h-3.5 w-3.5" /> {lead.website}</a>}
          {lead.phone && <span className="inline-flex items-center gap-1"><Phone className="h-3.5 w-3.5" /> {lead.phone}</span>}
          {lead.email && <span className="inline-flex items-center gap-1"><Mail className="h-3.5 w-3.5" /> {lead.email}</span>}
          {(lead.address || lead.city) && <span className="inline-flex items-center gap-1"><MapPin className="h-3.5 w-3.5" /> {lead.address ?? lead.city}</span>}
        </div>

        {pending && (
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={startOutreach} onChange={e => setStartOutreach(e.target.checked)} />
            <span>Onaylayınca İngilizce tanıtım maili + takipler otomatik gitsin ({meta.sequence.steps.map(s => s.delayDays === 0 ? 'gün 0' : `+${s.delayDays} gün`).join(', ')})</span>
          </label>
        )}
        {pending && startOutreach && !meta.sender.ready && (
          <div className="rounded-md border border-amber-500/40 bg-amber-500/5 px-3 py-2 text-xs text-amber-200 flex items-start gap-2">
            <AlertTriangle className="h-3.5 w-3.5 shrink-0 mt-0.5" /><span>Mailler sizin Gmail hesabınızdan gider; önce <button type="button" className="underline" onClick={() => navigate('/settings')}>Ayarlar → E-posta Gönderimi</button> bölümünü doldurun.</span>
          </div>
        )}

        <DialogFooter className="gap-2 flex-wrap sm:justify-between">
          <div className="flex gap-2">
            {pending && (
              <div className="flex items-center gap-2">
                <Input value={rejectReason} onChange={e => setRejectReason(e.target.value)} placeholder="Ret sebebi (opsiyonel)" className="h-9 w-full sm:w-52" maxLength={500} />
                <Button type="button" variant="outline" size="sm" disabled={busy} onClick={onReject}><X className="h-4 w-4" /> Reddet</Button>
              </div>
            )}
            {(lead.status === 'Rejected' || lead.status === 'Duplicate') && (
              <Button type="button" variant="outline" size="sm" disabled={busy} onClick={onReopen}><RotateCcw className="h-4 w-4" /> Bekleyene al</Button>
            )}
          </div>
          <div className="flex gap-2">
            <Button type="button" variant="outline" size="sm" disabled={busy} onClick={onClose}>Kapat</Button>
            {!pending && <Button type="button" variant="outline" size="sm" disabled={busy || !dirty} onClick={onSave}>{update.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />} Kaydet</Button>}
            {pending && (
              <Button type="button" size="sm" disabled={busy || (startOutreach && !meta.sender.ready)} onClick={onApprove}>
                {approve.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />} {startOutreach ? 'Onayla → maili başlat' : 'Onayla'}
              </Button>
            )}
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Sayfa ────────────────────────────────────────────────────────────────────

export function GlobalLeadsPage() {
  const { toast } = useToast();
  const navigate = useNavigate();
  const [status, setStatus] = useState<GlobalLeadStatus>('Pending');
  const [outreach, setOutreach] = useState<OutreachStatus | ''>('');
  const [country, setCountry] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [checked, setChecked] = useState<Set<string>>(new Set());
  const [showRuns, setShowRuns] = useState(false);
  const [showSequence, setShowSequence] = useState(false);
  const { data, isLoading, isError, error } = useGlobalLeads({ status, outreach: status === 'Approved' ? outreach : '', country, search, page });
  const { data: meta } = useGlobalLeadMeta();
  const { data: runs = [] } = useGlobalAgentRuns(10);
  const rejectMany = useRejectManyGlobalLeads();
  const approveMany = useApproveManyGlobalLeads();
  const runCycle = useRunOutreachCycle();
  const items = data?.items ?? [];
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;
  const totalSteps = meta?.sequence.steps.length ?? 0;

  const allChecked = useMemo(() => items.length > 0 && items.every(i => checked.has(i.id)), [items, checked]);
  function toggleAll() { setChecked(allChecked ? new Set() : new Set(items.map(i => i.id))); }
  function toggle(id: string) { setChecked(s => { const n = new Set(s); if (n.has(id)) n.delete(id); else n.add(id); return n; }); }

  async function onRejectChecked() {
    try { const r = await rejectMany.mutateAsync({ ids: [...checked], reason: 'Toplu ret' }); toast({ title: `${r.rejected} lead reddedildi` }); setChecked(new Set()); }
    catch (err) { toast({ title: 'Olmadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function onApproveChecked() {
    try {
      const r = await approveMany.mutateAsync({ ids: [...checked], startOutreach: true });
      toast({ title: `${r.approved} lead onaylandı · mailler sıraya alındı`, description: r.errors.length ? `${r.errors.length} atlandı: ${r.errors[0]}` : undefined, variant: r.errors.length && !r.approved ? 'destructive' : 'default' });
      setChecked(new Set());
    } catch (err) { toast({ title: 'Olmadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function onRunCycle() {
    try { const r = await runCycle.mutateAsync(); toast({ title: 'Döngü çalıştı', description: `${r.due} sırada · ${r.sent} gönderildi · ${r.replied} yanıt · ${r.bounced} bounce · ${r.failed} hata · ${r.deferred} ertelendi${r.notes.length ? ` — ${r.notes[0]}` : ''}` }); }
    catch (err) { toast({ title: 'Olmadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }

  const activeCount = meta?.outreach?.Active ?? 0;
  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-foreground flex items-center gap-2"><Globe className="h-6 w-6" /> Global Lead'ler</h1>
          <p className="text-sm text-muted-foreground mt-1">Türkiye dışı asansör firmaları (şimdilik Birleşik Krallık). Global ajan haftada bir bölge bölge tarar; bulduğu firma buraya düşer, Müşteriler'e girmez. Onayladığınız lead'e İngilizce tanıtım maili sizin Gmail'inizden gider, takip mailleri belirlenen günlerde otomatik devam eder; yanıt, bounce ya da abonelikten çıkma diziyi durdurur.</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" size="sm" onClick={() => setShowSequence(v => !v)}><Settings2 className="h-4 w-4" /> Dizi & limitler</Button>
          <Button variant="outline" size="sm" onClick={() => setShowRuns(v => !v)}><Bot className="h-4 w-4" /> Ajan koşuları</Button>
          <Button variant="outline" size="sm" disabled={runCycle.isPending} onClick={onRunCycle} title="Zamanlayıcıyı beklemeden sıradakileri gönder"><Send className="h-4 w-4" /> Sıradakileri şimdi gönder</Button>
        </div>
      </div>

      {meta && !meta.sender.ready && (
        <div className="flex items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/5 px-3 py-2 text-xs text-amber-200">
          <AlertTriangle className="h-3.5 w-3.5 shrink-0 mt-0.5" /> <span>Outreach mailleri onaylayan kullanıcının Gmail hesabından gider. Sizin Gmail ayarınız yok — <button type="button" className="underline" onClick={() => navigate('/settings')}>Ayarlar → E-posta Gönderimi</button>.</span>
        </div>
      )}
      {meta && !meta.agentKeyConfigured && (
        <div className="flex items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/5 px-3 py-2 text-xs text-amber-200">
          <AlertTriangle className="h-3.5 w-3.5 shrink-0 mt-0.5" /> <span>Railway'de <code>LeadAgent__ApiKey</code> tanımlı değil — ajan aday gönderemez.</span>
        </div>
      )}
      {meta && !meta.limits.enabled && (
        <div className="flex items-start gap-2 rounded-md border border-red-500/40 bg-red-500/5 px-3 py-2 text-xs text-red-200"><AlertTriangle className="h-3.5 w-3.5 shrink-0 mt-0.5" /> <span>Otomatik gönderim kapalı (<code>Outreach__Enabled=false</code>).</span></div>
      )}

      {showSequence && meta && (
        <Card><CardContent className="p-3 space-y-3 text-xs">
          <div className="flex flex-wrap gap-x-6 gap-y-1 text-muted-foreground">
            <span><Clock className="inline h-3.5 w-3.5 mr-1" />Pencere: hafta içi {meta.sequence.startHour}:00–{meta.sequence.endHour}:00 ({meta.sequence.timeZone})</span>
            <span>Günlük limit: gönderici başına {meta.limits.dailyCapPerSender}</span>
            <span>Zamanlayıcı: her {meta.limits.intervalMinutes} dk</span>
            <span>Gönderen: {meta.sender.ready ? `${meta.sender.name} <${meta.sender.address}>` : '—'}</span>
          </div>
          <table className="table-stack w-full text-xs">
            <thead><tr className="text-left text-muted-foreground border-b border-border"><th className="py-1 pr-2">Adım</th><th className="py-1 pr-2">Şablon</th><th className="py-1 pr-2">Zaman</th><th className="py-1 pr-2">Konu</th><th className="py-1">Ek</th></tr></thead>
            <tbody>{meta.sequence.steps.map((s, i) => (
              <tr key={s.template} className="border-b border-border/40 last:border-0 align-top">
                <td data-label="Adım" className="py-1 pr-2">{i + 1}</td>
                <td data-label="Şablon" data-stack="primary" className={cn('py-1 pr-2', !s.exists && 'text-red-400')}>{s.template}{!s.exists && ' (yok!)'}</td>
                <td data-label="Zaman" className="py-1 pr-2 whitespace-nowrap">{s.delayDays === 0 ? 'Onay günü' : `Önceki + ${s.delayDays - (meta.sequence.steps[i - 1]?.delayDays ?? 0)} gün (gün ${s.delayDays})`}</td>
                <td data-label="Konu" className="py-1 pr-2">{s.subjects[0] ?? <span className="text-muted-foreground">Re: (tanıtım konusu)</span>}</td>
                <td data-label="Ek" className="py-1">{s.attachments ? (meta.sequence.attachments.length ? <span className="inline-flex items-center gap-1"><Paperclip className="h-3 w-3" />{meta.sequence.attachments.join(', ')}</span> : <span className="text-amber-300">PDF yok (docs/marketing/global)</span>) : '—'}</td>
              </tr>
            ))}</tbody>
          </table>
          {meta.lastRun && (
            <div className="text-muted-foreground">Son döngü {fmtShort(meta.lastRun.finishedAt)}: {meta.lastRun.due} sırada · {meta.lastRun.sent} gönderildi · {meta.lastRun.replied} yanıt · {meta.lastRun.bounced} bounce · {meta.lastRun.failed} hata · {meta.lastRun.deferred} ertelendi{meta.lastRun.notes.length ? ` — ${meta.lastRun.notes.slice(0, 2).join(' | ')}` : ''}</div>
          )}
          <div className="text-muted-foreground">Şablonlar ve <code>sequence.json</code> repo'da <code>docs/marketing/global/</code> altında; gün sayıları ve pencere oradan değişir.</div>
        </CardContent></Card>
      )}

      {showRuns && (
        <Card><CardContent className="p-3">
          <div className="text-xs font-semibold text-foreground mb-2 inline-flex items-center gap-1.5"><Bot className="h-3.5 w-3.5" /> Son koşular</div>
          {runs.length === 0 ? <div className="text-xs text-muted-foreground">Henüz koşu yok. GitHub → Actions → Global Lead Discovery Agent → Run workflow.</div> : (
            <table className="table-stack w-full text-xs">
              <thead><tr className="text-left text-muted-foreground border-b border-border"><th className="py-1 pr-2">Koşu</th><th className="py-1 pr-2">Ülke</th><th className="py-1 pr-2">Bölgeler</th><th className="py-1 pr-2 text-right">Bulunan</th><th className="py-1 pr-2 text-right">Yeni</th><th className="py-1 pr-2 text-right">Önceden</th><th className="py-1 text-right">Geçersiz</th></tr></thead>
              <tbody>{runs.map(r => (
                <tr key={r.runId} className="border-b border-border/40 last:border-0">
                  <td data-label="Koşu" data-stack="primary" className="py-1 pr-2 whitespace-nowrap">{fmt(r.startedAt)}{!r.finishedAt && <span className="ml-1 text-amber-400">(sürüyor)</span>}</td>
                  <td data-label="Ülke" className="py-1 pr-2">{r.country}</td><td data-label="Bölgeler" className="py-1 pr-2">{r.regions}</td>
                  <td data-label="Bulunan" className="py-1 pr-2 text-right tabular-nums">{r.found}</td><td data-label="Yeni" className="py-1 pr-2 text-right tabular-nums text-emerald-400">{r.created}</td>
                  <td data-label="Önceden" className="py-1 pr-2 text-right tabular-nums">{r.existing}</td><td data-label="Geçersiz" className="py-1 text-right tabular-nums">{r.invalid}</td>
                </tr>
              ))}</tbody>
            </table>
          )}
        </CardContent></Card>
      )}

      <div className="flex flex-wrap items-center gap-2">
        {STATUS_TABS.map(t => (
          <button key={t.value} type="button" onClick={() => { setStatus(t.value); setPage(1); setChecked(new Set()); }}
            className={cn('inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-sm font-medium', status === t.value ? 'bg-primary text-primary-foreground border-primary' : 'border-border text-muted-foreground hover:text-foreground')}>
            {t.label}{meta && <span className={cn('rounded-full px-1.5 text-[11px]', status === t.value ? 'bg-primary-foreground/20' : 'bg-muted')}>{meta.counts[t.key]}</span>}
          </button>
        ))}
        <select value={country} onChange={e => { setCountry(e.target.value); setPage(1); }} className="h-9 rounded-md border border-input bg-transparent px-2 text-sm">
          <option value="">Tüm ülkeler</option>
          {(meta?.countries ?? []).map(c => <option key={c.code} value={c.code}>{countryName(c.code)} ({c.count})</option>)}
          {(meta?.supportedCountries ?? []).filter(c => !(meta?.countries ?? []).some(x => x.code === c)).map(c => <option key={c} value={c}>{countryName(c)}</option>)}
        </select>
        {status === 'Approved' && (
          <select value={outreach} onChange={e => { setOutreach(e.target.value as OutreachStatus | ''); setPage(1); }} className="h-9 rounded-md border border-input bg-transparent px-2 text-sm">
            <option value="">Tüm dizi durumları{activeCount ? ` (${activeCount} aktif)` : ''}</option>
            {OUTREACH_FILTERS.map(o => <option key={o} value={o}>{OUTREACH_LABEL[o]} ({meta?.outreach?.[o] ?? 0})</option>)}
          </select>
        )}
        <div className="relative w-full sm:w-auto sm:ml-auto">
          <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input value={search} onChange={e => { setSearch(e.target.value); setPage(1); }} placeholder="Firma / e-posta / şehir" className="pl-8 h-9 w-full sm:w-64" />
        </div>
        {status === 'Pending' && checked.size > 0 && (
          <>
            <Button size="sm" disabled={approveMany.isPending || !meta?.sender.ready} onClick={onApproveChecked} title={!meta?.sender.ready ? 'Gmail ayarlarınız eksik' : undefined}><Check className="h-4 w-4" /> Seçili {checked.size} lead'i onayla + mail</Button>
            <Button variant="outline" size="sm" disabled={rejectMany.isPending} onClick={onRejectChecked}><X className="h-4 w-4" /> Reddet</Button>
          </>
        )}
      </div>

      {isLoading && <div className="text-sm text-muted-foreground flex items-center gap-2"><Loader2 className="h-4 w-4 animate-spin" /> Yükleniyor…</div>}
      {isError && <div className="text-sm text-red-400 flex items-center gap-2"><AlertTriangle className="h-4 w-4" /> {errorMessage(error, "Lead'ler alınamadı.")}</div>}

      {data && (
        <Card><CardContent className="p-0">
          {items.length === 0 ? (
            <div className="py-12 text-center text-sm text-muted-foreground flex flex-col items-center gap-2">
              <Info className="h-6 w-6" />
              {status === 'Pending' ? 'Bekleyen global lead yok. Ajan Perşembe sabahları koşar; elle tetiklemek için GitHub Actions → Global Lead Discovery Agent.' : 'Kayıt yok.'}
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="table-stack w-full text-sm">
                <thead><tr className="text-left text-xs text-muted-foreground border-b border-border">
                  {status === 'Pending' && <th className="px-3 py-2.5"><input type="checkbox" checked={allChecked} onChange={toggleAll} /></th>}
                  <th className="px-3 py-2.5 font-medium">Firma</th><th className="px-3 py-2.5 font-medium">E-posta / web</th><th className="px-3 py-2.5 font-medium">Ülke / bölge</th>
                  {status === 'Approved' ? <th className="px-3 py-2.5 font-medium">Dizi</th> : <th className="px-3 py-2.5 font-medium">Kaynak</th>}
                  <th className="px-3 py-2.5 font-medium">Güven</th><th className="px-3 py-2.5 font-medium">Not</th><th className="px-3 py-2.5 font-medium">{status === 'Approved' ? 'Sıradaki' : 'Bulundu'}</th>
                </tr></thead>
                <tbody>{items.map(l => (
                  <tr key={l.id} onClick={() => setSelectedId(l.id)} className="border-b border-border/50 last:border-0 hover:bg-accent/40 cursor-pointer align-top">
                    {status === 'Pending' && <td data-stack="check" data-label="" className="px-3 py-2.5" onClick={e => e.stopPropagation()}><input type="checkbox" checked={checked.has(l.id)} onChange={() => toggle(l.id)} /></td>}
                    <td data-stack="primary" data-label="Firma" className="px-3 py-2.5 md:max-w-[260px]"><div className="font-medium truncate" title={l.companyName}>{l.companyName}</div>{l.contactName && <div className="text-[11px] text-muted-foreground truncate">{l.contactName}</div>}</td>
                    <td data-label="E-posta / web" className="px-3 py-2.5 text-xs text-muted-foreground space-y-0.5">
                      {l.email ? <div className="inline-flex items-center gap-1 truncate max-w-[220px]"><Mail className="h-3 w-3" /> {l.email}</div> : <div className="text-amber-400/80 inline-flex items-center gap-1"><Mail className="h-3 w-3" /> e-posta yok</div>}
                      {l.website && <div className="inline-flex items-center gap-1 truncate max-w-[220px]"><Globe className="h-3 w-3" /> {l.website.replace(/^https?:\/\/(www\.)?/, '')}</div>}
                    </td>
                    <td data-label="Ülke / bölge" className="px-3 py-2.5 text-xs">{l.country} · {l.city ?? l.region ?? '—'}</td>
                    {status === 'Approved'
                      ? <td data-label="Dizi" className="px-3 py-2.5"><OutreachPill status={l.outreachStatus} step={l.outreachStep} total={totalSteps} /></td>
                      : <td data-label="Kaynak" className="px-3 py-2.5 text-xs">{l.sourceUrl ? <a href={l.sourceUrl} target="_blank" rel="noopener noreferrer" onClick={e => e.stopPropagation()} className="inline-flex items-center gap-1 text-primary hover:underline"><ExternalLink className="h-3 w-3" /> {l.source}</a> : l.source}</td>}
                    <td data-label="Güven" className="px-3 py-2.5"><ConfidenceBar value={l.confidence} /></td>
                    <td data-label="Not" className="px-3 py-2.5 text-xs md:max-w-[240px]">{l.lastError ? <span className="text-red-300 inline-flex items-start gap-1" title={l.lastError}><AlertTriangle className="h-3 w-3 shrink-0 mt-0.5" /><span className="truncate">{l.lastError}</span></span> : l.matchNotes ? <span className="text-amber-300 inline-flex items-start gap-1" title={l.matchNotes}><AlertTriangle className="h-3 w-3 shrink-0 mt-0.5" /><span className="truncate">{l.matchNotes}</span></span> : l.rejectReason ? <span className="text-muted-foreground truncate">{l.rejectReason}</span> : null}</td>
                    <td data-label={status === 'Approved' ? 'Sıradaki' : 'Bulundu'} className="px-3 py-2.5 text-xs text-muted-foreground whitespace-nowrap">{status === 'Approved' ? (l.outreachStatus === 'Active' ? fmtShort(l.nextSendAt) : l.lastSentAt ? `son ${fmtShort(l.lastSentAt)}` : '—') : new Date(l.createdAt).toLocaleDateString('tr-TR', { day: '2-digit', month: 'short' })}</td>
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

      {selectedId && meta && <LeadDialog leadId={selectedId} meta={meta} onClose={() => setSelectedId(null)} />}
    </div>
  );
}
