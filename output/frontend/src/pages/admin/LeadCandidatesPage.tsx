import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Radar, Search, Check, X, Copy as CopyIcon, ExternalLink, Loader2, AlertTriangle, Phone, Globe, Mail, MapPin, RotateCcw, Bot, ChevronLeft, ChevronRight, Info, Link2,
} from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '@/components/ui/dialog';
import { cn } from '@/lib/utils';
import { useToast } from '@/hooks/use-toast';
import { useCustomers } from '@/api/customers';
import {
  useLeadCandidates, useLeadMeta, useLeadAgentRuns, useApproveLead, useRejectLead, useRejectManyLeads, useMarkLeadDuplicate, useReopenLead,
  type LeadCandidate, type LeadCandidateStatus,
} from '@/api/leadCandidates';

const STATUS_TABS: { value: LeadCandidateStatus; label: string; key: 'pending' | 'approved' | 'rejected' | 'duplicate' }[] = [
  { value: 'Pending', label: 'Bekleyen', key: 'pending' },
  { value: 'Approved', label: 'Onaylanan', key: 'approved' },
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

const LABELS = [
  { value: 'YuksekPotansiyel', label: 'Yüksek potansiyel' }, { value: 'Potansiyel', label: 'Potansiyel' }, { value: 'Notr', label: 'Nötr' }, { value: 'Vasat', label: 'Vasat' }, { value: 'Kotu', label: 'Kötü' },
];

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

function ConfidenceBar({ value }: { value: number }) {
  return (
    <div className="flex items-center gap-1.5" title={`Güven ${value}/100`}>
      <div className="h-1.5 w-14 rounded bg-muted/60 overflow-hidden"><div className={cn('h-full', value >= 75 ? 'bg-emerald-500' : value >= 50 ? 'bg-amber-500' : 'bg-red-500')} style={{ width: `${value}%` }} /></div>
      <span className="text-[11px] tabular-nums text-muted-foreground">{value}</span>
    </div>
  );
}

// ── Detay / onay diyaloğu ────────────────────────────────────────────────────

function CandidateDialog({ candidate, projects, defaultProjectId, onClose }: { candidate: LeadCandidate; projects: { id: string; name: string }[]; defaultProjectId: string | null; onClose: () => void }) {
  const { toast } = useToast();
  const navigate = useNavigate();
  const approve = useApproveLead();
  const reject = useRejectLead();
  const dup = useMarkLeadDuplicate();
  const reopen = useReopenLead();
  const c = candidate;
  const [form, setForm] = useState({ companyName: c.companyName, contactName: '', phone: c.phone ?? '', email: c.email ?? '', address: [c.address, c.district, c.city].filter(Boolean).join(', '), taxNumber: c.taxNumber ?? '', label: 'Potansiyel', projectId: c.projectId || defaultProjectId || '' });
  const [rejectReason, setRejectReason] = useState('');
  const [dupSearch, setDupSearch] = useState('');
  const { data: dupResults } = useCustomers({ search: dupSearch.trim(), pageSize: 6 });
  const busy = approve.isPending || reject.isPending || dup.isPending || reopen.isPending;
  const pending = c.status === 'Pending';

  useEffect(() => {
    setForm({ companyName: c.companyName, contactName: '', phone: c.phone ?? '', email: c.email ?? '', address: [c.address, c.district, c.city].filter(Boolean).join(', '), taxNumber: c.taxNumber ?? '', label: 'Potansiyel', projectId: c.projectId || defaultProjectId || '' });
    setRejectReason(''); setDupSearch('');
  }, [c, defaultProjectId]);

  async function onApprove() {
    if (!form.companyName.trim()) { toast({ title: 'Firma adı gerekli', variant: 'destructive' }); return; }
    try {
      const r = await approve.mutateAsync({ id: c.id, projectId: form.projectId || undefined, companyName: form.companyName.trim(), contactName: form.contactName.trim() || undefined, phone: form.phone.trim() || undefined, email: form.email.trim() || undefined, address: form.address.trim() || undefined, taxNumber: form.taxNumber.trim() || undefined, label: form.label });
      toast({ title: 'Lead oluşturuldu', description: form.companyName.trim() });
      onClose();
      navigate(`/customers/${r.customerId}`);
    } catch (err) { toast({ title: 'Onaylanamadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function onReject() {
    try { await reject.mutateAsync({ id: c.id, reason: rejectReason.trim() || undefined }); toast({ title: 'Reddedildi', description: 'Bu firma yeniden önerilmez.' }); onClose(); }
    catch (err) { toast({ title: 'Olmadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function onDup(customerId: string, name: string) {
    try { await dup.mutateAsync({ id: c.id, customerId }); toast({ title: 'Mükerrer işaretlendi', description: name }); onClose(); }
    catch (err) { toast({ title: 'Olmadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function onReopen() {
    try { await reopen.mutateAsync(c.id); toast({ title: 'Bekleyene alındı' }); onClose(); }
    catch (err) { toast({ title: 'Olmadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }

  return (
    <Dialog open onOpenChange={(o) => !o && !busy && onClose()}>
      <DialogContent className="sm:max-w-2xl max-h-[92vh] overflow-y-auto">
        <DialogHeader>
          <div className="flex flex-wrap items-center gap-2 mb-1">
            <span className={cn('inline-flex items-center rounded-full border px-2 py-0.5 text-[11px] font-semibold', STATUS_STYLE[c.status])}>{STATUS_LABEL[c.status] ?? c.status}</span>
            <span className="text-xs text-muted-foreground">{c.searchCity ?? c.city} · {c.source} · {fmt(c.createdAt)}</span>
            <ConfidenceBar value={c.confidence} />
          </div>
          <DialogTitle className="text-left">{c.companyName}</DialogTitle>
          <DialogDescription className="text-left">
            {c.legalTitle && <span className="block">{c.legalTitle}</span>}
            {c.snippet && <span className="block mt-1">{c.snippet}</span>}
            {c.sourceUrl && <a href={c.sourceUrl} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 text-primary hover:underline mt-1 break-all"><ExternalLink className="h-3 w-3" /> {c.sourceUrl}</a>}
          </DialogDescription>
        </DialogHeader>

        {c.matchNotes && (
          <div className={cn('rounded-md border px-3 py-2 text-xs flex items-start gap-2', c.status === 'Duplicate' ? 'border-slate-500/40 bg-slate-500/10 text-slate-300' : 'border-amber-500/40 bg-amber-500/5 text-amber-200')}>
            <AlertTriangle className="h-3.5 w-3.5 shrink-0 mt-0.5" />
            <span>{c.matchNotes}{(c.duplicateOfCustomerId || c.possibleMatchCustomerId) && (
              <> · <button type="button" className="underline" onClick={() => navigate(`/customers/${c.duplicateOfCustomerId ?? c.possibleMatchCustomerId}`)}>mevcut müşteriyi aç</button></>
            )}</span>
          </div>
        )}
        {c.status === 'Approved' && c.createdCustomerId && (
          <div className="rounded-md border border-emerald-500/40 bg-emerald-500/5 px-3 py-2 text-xs text-emerald-300">
            Onaylandı ({c.reviewedBy} · {fmt(c.reviewedAt)}) → <button type="button" className="underline" onClick={() => navigate(`/customers/${c.createdCustomerId}`)}>müşteri kartı</button>
          </div>
        )}
        {c.status === 'Rejected' && (
          <div className="rounded-md border border-red-500/30 bg-red-500/5 px-3 py-2 text-xs text-red-300">Reddedildi ({c.reviewedBy} · {fmt(c.reviewedAt)}){c.rejectReason ? ` — ${c.rejectReason}` : ''}</div>
        )}

        {/* Düzenlenebilir alanlar */}
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1 sm:col-span-2"><Label>Firma adı</Label><Input value={form.companyName} disabled={!pending} onChange={e => setForm(f => ({ ...f, companyName: e.target.value }))} /></div>
          <div className="space-y-1"><Label>Yetkili (opsiyonel)</Label><Input value={form.contactName} disabled={!pending} onChange={e => setForm(f => ({ ...f, contactName: e.target.value }))} placeholder="Ad Soyad" /></div>
          <div className="space-y-1"><Label>Telefon</Label><Input value={form.phone} disabled={!pending} onChange={e => setForm(f => ({ ...f, phone: e.target.value }))} /></div>
          <div className="space-y-1"><Label>E-posta</Label><Input value={form.email} disabled={!pending} onChange={e => setForm(f => ({ ...f, email: e.target.value }))} /></div>
          <div className="space-y-1"><Label>Vergi no</Label><Input value={form.taxNumber} disabled={!pending} onChange={e => setForm(f => ({ ...f, taxNumber: e.target.value }))} /></div>
          <div className="space-y-1 sm:col-span-2"><Label>Adres</Label><Input value={form.address} disabled={!pending} onChange={e => setForm(f => ({ ...f, address: e.target.value }))} /></div>
          <div className="space-y-1">
            <Label>Etiket</Label>
            <select value={form.label} disabled={!pending} onChange={e => setForm(f => ({ ...f, label: e.target.value }))} className="w-full h-10 rounded-md border border-input bg-transparent px-2 text-sm">
              {LABELS.map(l => <option key={l.value} value={l.value}>{l.label}</option>)}
            </select>
          </div>
          <div className="space-y-1">
            <Label>Proje</Label>
            <select value={form.projectId} disabled={!pending} onChange={e => setForm(f => ({ ...f, projectId: e.target.value }))} className="w-full h-10 rounded-md border border-input bg-transparent px-2 text-sm">
              {projects.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
          </div>
        </div>
        <div className="flex flex-wrap gap-3 text-xs text-muted-foreground">
          {c.website && <a href={c.website.startsWith('http') ? c.website : `https://${c.website}`} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 hover:text-primary"><Globe className="h-3.5 w-3.5" /> {c.website}</a>}
          {c.phone && <span className="inline-flex items-center gap-1"><Phone className="h-3.5 w-3.5" /> {c.phone}</span>}
          {c.email && <span className="inline-flex items-center gap-1"><Mail className="h-3.5 w-3.5" /> {c.email}</span>}
          {(c.district || c.city) && <span className="inline-flex items-center gap-1"><MapPin className="h-3.5 w-3.5" /> {[c.district, c.city].filter(Boolean).join(' / ')}</span>}
        </div>

        {pending && (
          <div className="rounded-md border border-border p-3 space-y-2">
            <div className="text-xs font-medium text-foreground inline-flex items-center gap-1.5"><Link2 className="h-3.5 w-3.5" /> Mevcut bir müşteri mi? Eşleştir</div>
            <div className="relative"><Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" /><Input value={dupSearch} onChange={e => setDupSearch(e.target.value)} placeholder="Müşteri ara (en az 2 harf)" className="pl-8 h-9" /></div>
            {dupSearch.trim().length >= 2 && (
              <div className="max-h-40 overflow-y-auto divide-y divide-border/50 rounded-md border border-border">
                {(dupResults?.items ?? []).length === 0 && <div className="px-3 py-2 text-xs text-muted-foreground">Sonuç yok</div>}
                {(dupResults?.items ?? []).map(cu => (
                  <button key={cu.id} type="button" disabled={busy} onClick={() => onDup(cu.id, cu.companyName)} className="w-full text-left px-3 py-2 text-xs hover:bg-accent flex items-center justify-between gap-2">
                    <span className="truncate">{cu.companyName}</span><span className="text-muted-foreground shrink-0">{cu.phone ?? ''} · {cu.status}</span>
                  </button>
                ))}
              </div>
            )}
          </div>
        )}

        <DialogFooter className="gap-2 flex-wrap sm:justify-between">
          <div className="flex gap-2">
            {pending && (
              <div className="flex items-center gap-2">
                <Input value={rejectReason} onChange={e => setRejectReason(e.target.value)} placeholder="Ret sebebi (opsiyonel)" className="h-9 w-56" maxLength={500} />
                <Button type="button" variant="outline" size="sm" disabled={busy} onClick={onReject}><X className="h-4 w-4" /> Reddet</Button>
              </div>
            )}
            {(c.status === 'Rejected' || c.status === 'Duplicate') && (
              <Button type="button" variant="outline" size="sm" disabled={busy} onClick={onReopen}><RotateCcw className="h-4 w-4" /> Bekleyene al</Button>
            )}
          </div>
          <div className="flex gap-2">
            <Button type="button" variant="outline" size="sm" disabled={busy} onClick={onClose}>Kapat</Button>
            {pending && (
              <Button type="button" size="sm" disabled={busy || !form.projectId} onClick={onApprove}>
                {approve.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />} Onayla → Lead oluştur
              </Button>
            )}
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Sayfa ────────────────────────────────────────────────────────────────────

export function LeadCandidatesPage() {
  const { toast } = useToast();
  const [status, setStatus] = useState<LeadCandidateStatus>('Pending');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<LeadCandidate | null>(null);
  const [checked, setChecked] = useState<Set<string>>(new Set());
  const [showRuns, setShowRuns] = useState(false);
  const { data, isLoading, isError, error } = useLeadCandidates({ status, search, page });
  const { data: meta } = useLeadMeta();
  const { data: runs = [] } = useLeadAgentRuns(10);
  const rejectMany = useRejectManyLeads();
  const items = data?.items ?? [];
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  const allChecked = useMemo(() => items.length > 0 && items.every(i => checked.has(i.id)), [items, checked]);
  function toggleAll() { setChecked(allChecked ? new Set() : new Set(items.map(i => i.id))); }
  function toggle(id: string) { setChecked(s => { const n = new Set(s); if (n.has(id)) n.delete(id); else n.add(id); return n; }); }

  async function onRejectChecked() {
    try { const r = await rejectMany.mutateAsync({ ids: [...checked], reason: 'Toplu ret' }); toast({ title: `${r.rejected} aday reddedildi` }); setChecked(new Set()); }
    catch (err) { toast({ title: 'Olmadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-foreground flex items-center gap-2"><Radar className="h-6 w-6" /> Lead Adayları</h1>
          <p className="text-sm text-muted-foreground mt-1">Lead ajanı haftada bir Türkiye'de asansör firmalarını tarar; bulduğu her firma önce buraya düşer. Onaylayınca Lead müşteri olur, reddedilen bir daha önerilmez. Mükerrerler mevcut müşterilerle telefon / web / vergi no / isim üzerinden otomatik yakalanır.</p>
        </div>
        <Button variant="outline" size="sm" onClick={() => setShowRuns(v => !v)}><Bot className="h-4 w-4" /> Ajan koşuları</Button>
      </div>

      {meta && !meta.agentKeyConfigured && (
        <div className="flex items-start gap-2 rounded-md border border-amber-500/40 bg-amber-500/5 px-3 py-2 text-xs text-amber-200">
          <AlertTriangle className="h-3.5 w-3.5 shrink-0 mt-0.5" /> <span>Railway'de <code>LeadAgent__ApiKey</code> tanımlı değil — ajan aday gönderemez. Anahtarı girip GitHub secret <code>CRM_LEAD_AGENT_KEY</code> ile eşleyin (tools/lead-agent/README.md).</span>
        </div>
      )}

      {showRuns && (
        <Card><CardContent className="p-3">
          <div className="text-xs font-semibold text-foreground mb-2 inline-flex items-center gap-1.5"><Bot className="h-3.5 w-3.5" /> Son koşular</div>
          {runs.length === 0 ? <div className="text-xs text-muted-foreground">Henüz koşu yok. GitHub → Actions → Lead Discovery Agent → Run workflow.</div> : (
            <table className="w-full text-xs">
              <thead><tr className="text-left text-muted-foreground border-b border-border"><th className="py-1 pr-2">Koşu</th><th className="py-1 pr-2">İller</th><th className="py-1 pr-2 text-right">Bulunan</th><th className="py-1 pr-2 text-right">Yeni</th><th className="py-1 pr-2 text-right">Mükerrer</th><th className="py-1 pr-2 text-right">Önceden</th><th className="py-1 text-right">Geçersiz</th></tr></thead>
              <tbody>{runs.map(r => (
                <tr key={r.runId} className="border-b border-border/40 last:border-0">
                  <td className="py-1 pr-2 whitespace-nowrap">{fmt(r.startedAt)}{!r.finishedAt && <span className="ml-1 text-amber-400">(sürüyor)</span>}</td>
                  <td className="py-1 pr-2">{r.cities}</td>
                  <td className="py-1 pr-2 text-right tabular-nums">{r.found}</td><td className="py-1 pr-2 text-right tabular-nums text-emerald-400">{r.created}</td>
                  <td className="py-1 pr-2 text-right tabular-nums">{r.duplicates}</td><td className="py-1 pr-2 text-right tabular-nums">{r.existing}</td><td className="py-1 text-right tabular-nums">{r.invalid}</td>
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
        <div className="relative ml-auto">
          <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input value={search} onChange={e => { setSearch(e.target.value); setPage(1); }} placeholder="Firma / il / telefon" className="pl-8 h-9 w-64" />
        </div>
        {status === 'Pending' && checked.size > 0 && (
          <Button variant="outline" size="sm" disabled={rejectMany.isPending} onClick={onRejectChecked}><X className="h-4 w-4" /> Seçili {checked.size} adayı reddet</Button>
        )}
      </div>

      {isLoading && <div className="text-sm text-muted-foreground flex items-center gap-2"><Loader2 className="h-4 w-4 animate-spin" /> Yükleniyor…</div>}
      {isError && <div className="text-sm text-red-400 flex items-center gap-2"><AlertTriangle className="h-4 w-4" /> {errorMessage(error, 'Adaylar alınamadı.')}</div>}

      {data && (
        <Card><CardContent className="p-0">
          {items.length === 0 ? (
            <div className="py-12 text-center text-sm text-muted-foreground flex flex-col items-center gap-2">
              <Info className="h-6 w-6" />
              {status === 'Pending' ? 'Bekleyen aday yok. Ajan Salı sabahları koşar; elle tetiklemek için GitHub Actions → Lead Discovery Agent.' : 'Kayıt yok.'}
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead><tr className="text-left text-xs text-muted-foreground border-b border-border">
                  {status === 'Pending' && <th className="px-3 py-2.5"><input type="checkbox" checked={allChecked} onChange={toggleAll} /></th>}
                  <th className="px-3 py-2.5 font-medium">Firma</th><th className="px-3 py-2.5 font-medium">İletişim</th><th className="px-3 py-2.5 font-medium">İl / ilçe</th><th className="px-3 py-2.5 font-medium">Kaynak</th><th className="px-3 py-2.5 font-medium">Güven</th><th className="px-3 py-2.5 font-medium">Not</th><th className="px-3 py-2.5 font-medium">Bulundu</th>
                </tr></thead>
                <tbody>{items.map(c => (
                  <tr key={c.id} onClick={() => setSelected(c)} className="border-b border-border/50 last:border-0 hover:bg-accent/40 cursor-pointer align-top">
                    {status === 'Pending' && <td className="px-3 py-2.5" onClick={e => e.stopPropagation()}><input type="checkbox" checked={checked.has(c.id)} onChange={() => toggle(c.id)} /></td>}
                    <td className="px-3 py-2.5 max-w-[280px]"><div className="font-medium truncate" title={c.companyName}>{c.companyName}</div>{c.legalTitle && <div className="text-[11px] text-muted-foreground truncate">{c.legalTitle}</div>}</td>
                    <td className="px-3 py-2.5 text-xs text-muted-foreground space-y-0.5">{c.phone && <div className="inline-flex items-center gap-1"><Phone className="h-3 w-3" /> {c.phone}</div>}{c.website && <div className="inline-flex items-center gap-1 truncate max-w-[220px]"><Globe className="h-3 w-3" /> {c.website.replace(/^https?:\/\/(www\.)?/, '')}</div>}</td>
                    <td className="px-3 py-2.5 text-xs">{[c.district, c.city ?? c.searchCity].filter(Boolean).join(' / ')}</td>
                    <td className="px-3 py-2.5 text-xs">{c.sourceUrl ? <a href={c.sourceUrl} target="_blank" rel="noopener noreferrer" onClick={e => e.stopPropagation()} className="inline-flex items-center gap-1 text-primary hover:underline"><ExternalLink className="h-3 w-3" /> {c.source}</a> : c.source}</td>
                    <td className="px-3 py-2.5"><ConfidenceBar value={c.confidence} /></td>
                    <td className="px-3 py-2.5 text-xs max-w-[260px]">{c.matchNotes ? <span className={cn('inline-flex items-start gap-1', c.status === 'Duplicate' ? 'text-slate-300' : 'text-amber-300')} title={c.matchNotes}><AlertTriangle className="h-3 w-3 shrink-0 mt-0.5" /><span className="truncate">{c.matchNotes}</span></span> : c.rejectReason ? <span className="text-muted-foreground truncate">{c.rejectReason}</span> : null}</td>
                    <td className="px-3 py-2.5 text-xs text-muted-foreground whitespace-nowrap">{new Date(c.createdAt).toLocaleDateString('tr-TR', { day: '2-digit', month: 'short' })}</td>
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

      {selected && meta && <CandidateDialog candidate={selected} projects={meta.projects} defaultProjectId={meta.defaultProjectId} onClose={() => setSelected(null)} />}
    </div>
  );
}
