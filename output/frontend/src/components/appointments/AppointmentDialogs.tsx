import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Loader2, Phone, Mail, Users, StickyNote, MessageCircle, MapPin, Search, CalendarPlus, Check, PhoneOff, AlertTriangle, Star } from 'lucide-react';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { cn } from '@/lib/utils';
import { useToast } from '@/hooks/use-toast';
import { useCustomers, useContactHistory } from '@/api/customers';
import {
  useCreateAppointment, useCompleteAppointment, KIND_LABEL, CALL_KINDS,
  type Appointment, type AppointmentKind,
} from '@/api/appointments';
import type { ContactType } from '@/types';

// ── Ortak ────────────────────────────────────────────────────────────────────

export const KIND_STYLE: Record<string, string> = {
  NewCall: 'bg-sky-500/15 text-sky-300 border-sky-500/30',
  FollowUpCall: 'bg-violet-500/15 text-violet-300 border-violet-500/30',
  SatisfactionCall: 'bg-emerald-500/15 text-emerald-300 border-emerald-500/30',
  Meeting: 'bg-amber-500/15 text-amber-300 border-amber-500/30',
  Training: 'bg-teal-500/15 text-teal-300 border-teal-500/30',
  Demo: 'bg-fuchsia-500/15 text-fuchsia-300 border-fuchsia-500/30',
  Other: 'bg-slate-500/15 text-slate-300 border-slate-500/30',
};

export function KindPill({ kind, className }: { kind: string; className?: string }) {
  return (
    <span className={cn('inline-flex items-center rounded-full border px-2 py-0.5 text-[11px] font-semibold', KIND_STYLE[kind] ?? KIND_STYLE.Other, className)}>
      {KIND_LABEL[kind as AppointmentKind] ?? kind}
    </span>
  );
}

const CONTACT_TYPES: { value: ContactType; label: string; icon: React.ComponentType<{ className?: string }> }[] = [
  { value: 'Call', label: 'Telefon', icon: Phone },
  { value: 'WhatsApp', label: 'WhatsApp', icon: MessageCircle },
  { value: 'Email', label: 'E-posta', icon: Mail },
  { value: 'Meeting', label: 'Toplantı', icon: Users },
  { value: 'Visit', label: 'Ziyaret', icon: MapPin },
  { value: 'Note', label: 'Not', icon: StickyNote },
];

function errorMessage(err: unknown, fallback: string): string {
  if (err && typeof err === 'object' && 'response' in err && err.response && typeof err.response === 'object' && 'data' in err.response) {
    const data = (err.response as { data?: { message?: string; errors?: string[] } }).data;
    if (data?.errors?.length) return data.errors[0];
    if (data?.message) return data.message;
  }
  return fallback;
}

function toLocalInput(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`;
}

function defaultSlot(day?: string): string {
  if (day) return `${day}T10:00`;
  const d = new Date(); d.setMinutes(0, 0, 0); d.setHours(d.getHours() + 1);
  return toLocalInput(d);
}

// ── Randevu oluştur ──────────────────────────────────────────────────────────

interface CreateAppointmentDialogProps {
  open: boolean;
  onClose: () => void;
  /** Müşteri detayından açıldıysa sabit; takvimden açıldıysa seçici gösterilir. */
  customerId?: string;
  customerName?: string;
  /** Takvimde tıklanan gün (yyyy-MM-dd). */
  day?: string;
  defaultKind?: AppointmentKind;
}

export function CreateAppointmentDialog({ open, onClose, customerId, customerName, day, defaultKind = 'FollowUpCall' }: CreateAppointmentDialogProps) {
  const { toast } = useToast();
  const create = useCreateAppointment();
  const [search, setSearch] = useState('');
  const [picked, setPicked] = useState<{ id: string; name: string } | null>(customerId ? { id: customerId, name: customerName ?? '' } : null);
  const [kind, setKind] = useState<AppointmentKind>(defaultKind);
  const [when, setWhen] = useState(defaultSlot(day));
  const [duration, setDuration] = useState<number>(15);
  const [title, setTitle] = useState('');
  const [note, setNote] = useState('');
  const { data: results, isFetching } = useCustomers({ search: search.trim(), pageSize: 8 });
  const candidates = useMemo(() => (search.trim().length >= 2 ? (results?.items ?? []) : []), [results, search]);

  useEffect(() => {
    if (!open) return;
    setPicked(customerId ? { id: customerId, name: customerName ?? '' } : null);
    setSearch(''); setKind(defaultKind); setWhen(defaultSlot(day)); setTitle(''); setNote('');
    setDuration(defaultKind === 'Meeting' ? 60 : defaultKind === 'Training' ? 90 : defaultKind === 'Demo' ? 45 : 15);
  }, [open, customerId, customerName, day, defaultKind]);

  function pickKind(k: AppointmentKind) {
    setKind(k);
    setDuration(k === 'Meeting' ? 60 : k === 'Training' ? 90 : k === 'Demo' ? 45 : k === 'Other' ? 30 : 15);
  }

  async function submit() {
    if (!picked) { toast({ title: 'Müşteri seçin', variant: 'destructive' }); return; }
    if (!when) { toast({ title: 'Tarih ve saat gerekli', variant: 'destructive' }); return; }
    try {
      await create.mutateAsync({
        customerId: picked.id, kind, scheduledAt: new Date(when).toISOString(), durationMinutes: duration,
        title: title.trim() || undefined, note: note.trim() || undefined,
      });
      toast({ title: 'Randevu oluşturuldu', description: `${picked.name} · ${KIND_LABEL[kind]} · ${new Date(when).toLocaleString('tr-TR', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })}` });
      onClose();
    } catch (err) { toast({ title: 'Oluşturulamadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && !create.isPending && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2"><CalendarPlus className="h-5 w-5" /> Randevu oluştur</DialogTitle>
          <DialogDescription>Görüşme, eğitim, demo ya da arama — takvimde günün içinde görünür, günü gelince buradan görüşme kaydı girilir.</DialogDescription>
        </DialogHeader>
        <div className="space-y-4">
          {/* Müşteri */}
          <div className="space-y-1.5">
            <Label>Müşteri</Label>
            {picked ? (
              <div className="flex items-center justify-between rounded-md border border-border px-3 py-2 text-sm">
                <span className="font-medium truncate">{picked.name || 'Seçili müşteri'}</span>
                {!customerId && <Button type="button" variant="ghost" size="sm" onClick={() => setPicked(null)}>Değiştir</Button>}
              </div>
            ) : (
              <div className="space-y-1">
                <div className="relative">
                  <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
                  <Input value={search} onChange={e => setSearch(e.target.value)} placeholder="Firma adı ile ara (en az 2 harf)" className="pl-8" autoFocus />
                </div>
                {search.trim().length >= 2 && (
                  <div className="max-h-48 overflow-y-auto rounded-md border border-border divide-y divide-border/50">
                    {isFetching && candidates.length === 0 && <div className="px-3 py-2 text-xs text-muted-foreground">Aranıyor…</div>}
                    {!isFetching && candidates.length === 0 && <div className="px-3 py-2 text-xs text-muted-foreground">Sonuç yok</div>}
                    {candidates.map(c => (
                      <button key={c.id} type="button" onClick={() => setPicked({ id: c.id, name: c.companyName })}
                        className="w-full text-left px-3 py-2 text-sm hover:bg-accent flex items-center justify-between gap-2">
                        <span className="truncate">{c.companyName}</span>
                        <span className="text-[11px] text-muted-foreground shrink-0">{c.status}{c.label ? ` · ${c.label}` : ''}</span>
                      </button>
                    ))}
                  </div>
                )}
              </div>
            )}
          </div>

          {/* Tür */}
          <div className="space-y-1.5">
            <Label>Tür</Label>
            <div className="flex flex-wrap gap-1.5">
              {(Object.keys(KIND_LABEL) as AppointmentKind[]).map(k => (
                <button key={k} type="button" onClick={() => pickKind(k)}
                  className={cn('rounded-full border px-2.5 py-1 text-xs font-medium transition-colors',
                    kind === k ? KIND_STYLE[k] + ' ring-1 ring-primary/40' : 'border-border text-muted-foreground hover:text-foreground')}>
                  {KIND_LABEL[k]}
                </button>
              ))}
            </div>
          </div>

          <div className="grid gap-3 sm:grid-cols-[1fr_120px]">
            <div className="space-y-1.5">
              <Label htmlFor="appt-when">Tarih ve saat</Label>
              <Input id="appt-when" type="datetime-local" value={when} onChange={e => setWhen(e.target.value)} />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="appt-dur">Süre (dk)</Label>
              <Input id="appt-dur" type="number" min={5} max={480} step={5} value={duration} onChange={e => setDuration(Number(e.target.value) || 15)} />
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="appt-title">Başlık (opsiyonel)</Label>
            <Input id="appt-title" value={title} onChange={e => setTitle(e.target.value)} maxLength={200}
              placeholder={kind === 'Training' ? 'Ör: Mobil uygulama eğitimi' : kind === 'Demo' ? 'Ör: Yönetim paneli demosu' : 'Ör: Yenileme görüşmesi'} />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="appt-note">Not</Label>
            <Textarea id="appt-note" rows={2} value={note} onChange={e => setNote(e.target.value)} placeholder="Görüşmede konuşulacaklar…" />
          </div>
        </div>
        <DialogFooter className="gap-2">
          <Button type="button" variant="outline" disabled={create.isPending} onClick={onClose}>İptal</Button>
          <Button type="button" disabled={create.isPending || !picked} onClick={submit}>
            {create.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <CalendarPlus className="h-4 w-4" />} Oluştur
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Görüşme kaydı gir (randevuyu kapat) ──────────────────────────────────────

export function CompleteAppointmentDialog({ appointment, onClose }: { appointment: Appointment | null; onClose: () => void }) {
  const { toast } = useToast();
  const navigate = useNavigate();
  const complete = useCompleteAppointment();
  const [type, setType] = useState<ContactType>('Call');
  const [reached, setReached] = useState(true);
  const [subject, setSubject] = useState('');
  const [content, setContent] = useState('');
  const [outcome, setOutcome] = useState('');
  const [score, setScore] = useState<number | null>(null);
  const [nextContact, setNextContact] = useState('');
  const [reschedule, setReschedule] = useState('');
  const { data: history } = useContactHistory(appointment?.customerId ?? '');
  const recent = useMemo(() => (history?.items ?? []).slice(0, 3), [history]);
  const isCall = CALL_KINDS.includes((appointment?.kind ?? '') as AppointmentKind);
  const isSatisfaction = appointment?.kind === 'SatisfactionCall';

  useEffect(() => {
    if (!appointment) return;
    setType(appointment.kind === 'Meeting' || appointment.kind === 'Demo' || appointment.kind === 'Training' ? 'Meeting' : 'Call');
    setReached(true); setSubject(''); setContent(''); setOutcome(''); setScore(null); setNextContact(''); setReschedule('');
  }, [appointment]);

  async function submit() {
    if (!appointment) return;
    if (reached && !content.trim() && !outcome.trim()) { toast({ title: 'Görüşme notu girin', description: 'Açıklama ya da sonuç alanlarından biri dolu olmalı.', variant: 'destructive' }); return; }
    if (isSatisfaction && reached && score === null) { toast({ title: 'Memnuniyet puanı seçin (1–5)', variant: 'destructive' }); return; }
    try {
      const r = await complete.mutateAsync({
        id: appointment.id, type, reached,
        subject: subject.trim() || undefined, content: content.trim() || undefined, outcome: outcome.trim() || undefined,
        satisfactionScore: isSatisfaction || score !== null ? score : null,
        nextContactAt: reached && nextContact ? new Date(`${nextContact}T14:00`).toISOString() : null,
        rescheduleAt: !reached && reschedule ? new Date(reschedule).toISOString() : null,
      });
      toast({
        title: reached ? 'Görüşme kaydedildi' : 'Ulaşılamadı olarak kaydedildi',
        description: r.followUp ? `Takip randevusu: ${new Date(r.followUp.scheduledAt).toLocaleString('tr-TR', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })}` : appointment.customerName,
      });
      onClose();
    } catch (err) { toast({ title: 'Kaydedilemedi', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }

  if (!appointment) return null;
  const a = appointment;
  return (
    <Dialog open onOpenChange={(o) => !o && !complete.isPending && onClose()}>
      <DialogContent className="sm:max-w-2xl max-h-[92vh] overflow-y-auto">
        <DialogHeader>
          <div className="flex flex-wrap items-center gap-2 mb-1">
            <KindPill kind={a.kind} />
            <span className="text-xs text-muted-foreground">{new Date(a.scheduledAt).toLocaleString('tr-TR', { weekday: 'short', day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })}</span>
            {a.source === 'Agent' && <span className="text-[10px] rounded border border-border px-1.5 py-0.5 text-muted-foreground">CSO planı</span>}
          </div>
          <DialogTitle className="text-left">
            <button type="button" className="hover:text-primary text-left" onClick={() => navigate(`/customers/${a.customerId}`)}>{a.customerName}</button>
          </DialogTitle>
          <DialogDescription className="text-left">
            {[a.customerContactName, a.customerPhone, a.customerStatus, a.customerLabel].filter(Boolean).join(' · ')}
            {a.reason && <span className="block mt-1 text-foreground/80">Neden: {a.reason}</span>}
            {a.title && <span className="block mt-1">{a.title}</span>}
            {a.note && <span className="block mt-1 italic">{a.note}</span>}
          </DialogDescription>
        </DialogHeader>

        {recent.length > 0 && (
          <div className="rounded-md border border-border bg-muted/20 px-3 py-2 text-xs space-y-1">
            <div className="font-medium text-foreground">Son görüşmeler</div>
            {recent.map(h => (
              <div key={h.id} className="text-muted-foreground truncate">
                {new Date(h.contactedAt).toLocaleDateString('tr-TR', { day: '2-digit', month: 'short', year: '2-digit' })} · {h.subject ?? h.type}{h.outcome ? ` → ${h.outcome}` : ''}
              </div>
            ))}
          </div>
        )}

        <div className="space-y-4">
          {/* Ulaşıldı mı */}
          {isCall && (
            <div className="grid grid-cols-2 gap-2">
              <button type="button" onClick={() => setReached(true)}
                className={cn('flex items-center justify-center gap-2 rounded-lg border px-3 py-2.5 text-sm font-medium', reached ? 'border-emerald-500/50 bg-emerald-500/10 text-emerald-300' : 'border-border text-muted-foreground')}>
                <Check className="h-4 w-4" /> Görüşüldü
              </button>
              <button type="button" onClick={() => setReached(false)}
                className={cn('flex items-center justify-center gap-2 rounded-lg border px-3 py-2.5 text-sm font-medium', !reached ? 'border-amber-500/50 bg-amber-500/10 text-amber-300' : 'border-border text-muted-foreground')}>
                <PhoneOff className="h-4 w-4" /> Ulaşılamadı
              </button>
            </div>
          )}

          {/* Kanal */}
          <div className="space-y-1.5">
            <Label>Kanal</Label>
            <div className="flex flex-wrap gap-1.5">
              {CONTACT_TYPES.map(t => (
                <button key={t.value} type="button" onClick={() => setType(t.value)}
                  className={cn('inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-xs font-medium', type === t.value ? 'border-primary bg-primary/10 text-primary' : 'border-border text-muted-foreground hover:text-foreground')}>
                  <t.icon className="h-3.5 w-3.5" /> {t.label}
                </button>
              ))}
            </div>
          </div>

          {reached ? (
            <>
              {isSatisfaction && (
                <div className="space-y-1.5">
                  <Label>Memnuniyet puanı</Label>
                  <div className="flex gap-1.5">
                    {[1, 2, 3, 4, 5].map(n => (
                      <button key={n} type="button" onClick={() => setScore(n)} title={`${n}/5`}
                        className={cn('h-9 w-9 rounded-md border inline-flex items-center justify-center', score !== null && n <= score ? 'border-amber-400 bg-amber-400/15 text-amber-300' : 'border-border text-muted-foreground')}>
                        <Star className={cn('h-4 w-4', score !== null && n <= score && 'fill-current')} />
                      </button>
                    ))}
                    <span className="text-xs text-muted-foreground self-center ml-1">{score === null ? 'seçilmedi' : ['', 'çok memnuniyetsiz', 'memnuniyetsiz', 'nötr', 'memnun', 'çok memnun'][score]}</span>
                  </div>
                </div>
              )}
              <div className="space-y-1.5">
                <Label htmlFor="c-subject">Konu</Label>
                <Input id="c-subject" value={subject} onChange={e => setSubject(e.target.value)} maxLength={500} placeholder={KIND_LABEL[a.kind as AppointmentKind] ?? 'Görüşme'} />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="c-content">Görüşme notu</Label>
                <Textarea id="c-content" rows={4} value={content} onChange={e => setContent(e.target.value)} maxLength={4000} placeholder="Ne konuşuldu, müşteri ne dedi, hangi ihtiyaçlar çıktı…" />
              </div>
              <div className="grid gap-3 sm:grid-cols-2">
                <div className="space-y-1.5">
                  <Label htmlFor="c-outcome">Sonuç / aksiyon</Label>
                  <Input id="c-outcome" value={outcome} onChange={e => setOutcome(e.target.value)} maxLength={300} placeholder="Ör: teklif gönderilecek" />
                </div>
                <div className="space-y-1.5">
                  <Label htmlFor="c-next">Tekrar arama günü (söz verildiyse)</Label>
                  <Input id="c-next" type="date" value={nextContact} onChange={e => setNextContact(e.target.value)} />
                  <p className="text-[11px] text-muted-foreground">Dolu ise o güne 14:00 takip randevusu açılır.</p>
                </div>
              </div>
            </>
          ) : (
            <>
              <div className="space-y-1.5">
                <Label htmlFor="c-content2">Not (opsiyonel)</Label>
                <Input id="c-content2" value={content} onChange={e => setContent(e.target.value)} maxLength={4000} placeholder="Ör: meşgul, mesaj bırakıldı" />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="c-resched">Yeniden planla</Label>
                <Input id="c-resched" type="datetime-local" value={reschedule} onChange={e => setReschedule(e.target.value)} />
                <p className="text-[11px] text-muted-foreground flex items-center gap-1"><AlertTriangle className="h-3 w-3" /> Boş bırakılırsa randevu "Ulaşılamadı" olarak kapanır; planlayıcı firmayı sonraki günlerde yeniden değerlendirir.</p>
              </div>
            </>
          )}
        </div>

        <DialogFooter className="gap-2">
          <Button type="button" variant="outline" disabled={complete.isPending} onClick={onClose}>İptal</Button>
          <Button type="button" disabled={complete.isPending} onClick={submit}>
            {complete.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : reached ? <Check className="h-4 w-4" /> : <PhoneOff className="h-4 w-4" />}
            {reached ? 'Kaydet ve kapat' : 'Ulaşılamadı olarak kapat'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
