import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  CalendarDays, ChevronLeft, ChevronRight, CalendarPlus, PlayCircle, Loader2, Check, SkipForward, PhoneOff, RotateCcw, Phone, Bot, KanbanSquare, Filter, Clock, AlertTriangle,
} from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '@/components/ui/dialog';
import { cn } from '@/lib/utils';
import { useToast } from '@/hooks/use-toast';
import {
  useAppointments, useCallPlanStatus, useRunCallPlan, useRebuildCallPlan, useSkipAppointment, useUpdateAppointment, useDeleteAppointment,
  localDay, addDays, startOfWeek, KIND_LABEL, STATUS_LABEL, CALL_KINDS,
  type Appointment, type AppointmentKind, type AppointmentStatus,
} from '@/api/appointments';
import { CreateAppointmentDialog, CompleteAppointmentDialog, KindPill, KIND_STYLE } from '@/components/appointments/AppointmentDialogs';

const DAY_NAMES = ['Pzt', 'Sal', 'Çar', 'Per', 'Cum', 'Cmt', 'Paz'];

function errorMessage(err: unknown, fallback: string): string {
  if (err && typeof err === 'object' && 'response' in err && err.response && typeof err.response === 'object' && 'data' in err.response) {
    const data = (err.response as { data?: { message?: string; errors?: string[] } }).data;
    if (data?.errors?.length) return data.errors[0];
    if (data?.message) return data.message;
  }
  return fallback;
}

function fmtDayHeader(day: string): string {
  const [y, m, d] = day.split('-').map(Number);
  return new Date(y, m - 1, d).toLocaleDateString('tr-TR', { day: '2-digit', month: 'short' });
}
function timeOf(iso: string): string {
  return new Date(iso).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' });
}

const STATUS_ICON: Record<string, React.ComponentType<{ className?: string }>> = { Completed: Check, Skipped: SkipForward, NoAnswer: PhoneOff, Planned: Clock };

// ── Randevu kartı ────────────────────────────────────────────────────────────

function AppointmentCard({ a, onOpen, onSkip, onReopen, onDelete, busy }: {
  a: Appointment; onOpen: () => void; onSkip: () => void; onReopen: () => void; onDelete: () => void; busy: boolean;
}) {
  const navigate = useNavigate();
  const done = a.status !== 'Planned';
  const Icon = STATUS_ICON[a.status] ?? Clock;
  return (
    <div className={cn('group rounded-lg border px-2.5 py-2 text-xs space-y-1 transition-colors',
      done ? 'border-border/50 bg-muted/20 opacity-75' : 'border-border bg-card hover:border-primary/40')}>
      <div className="flex items-center justify-between gap-2">
        <span className="font-mono text-[11px] text-muted-foreground">{timeOf(a.scheduledAt)}</span>
        <KindPill kind={a.kind} className="text-[10px] px-1.5" />
      </div>
      <button type="button" onClick={() => navigate(`/customers/${a.customerId}`)} className="block text-left font-semibold text-foreground hover:text-primary truncate w-full" title={a.customerName}>
        {a.customerName}
      </button>
      {(a.title || a.reason) && <div className="text-muted-foreground line-clamp-2" title={a.reason ?? a.title ?? undefined}>{a.title ?? a.reason}</div>}
      {a.customerPhone && <div className="text-muted-foreground inline-flex items-center gap-1"><Phone className="h-3 w-3" /> {a.customerPhone}</div>}
      <div className="flex items-center justify-between gap-1 pt-0.5">
        <span className={cn('inline-flex items-center gap-1 text-[10px]', a.status === 'Completed' ? 'text-emerald-400' : a.status === 'NoAnswer' ? 'text-amber-400' : a.status === 'Skipped' ? 'text-muted-foreground' : 'text-sky-400')}>
          <Icon className="h-3 w-3" /> {STATUS_LABEL[a.status as AppointmentStatus] ?? a.status}{a.source === 'Agent' && a.status === 'Planned' ? ' · CSO' : ''}
        </span>
        <div className="flex items-center gap-0.5 opacity-0 group-hover:opacity-100 transition-opacity">
          {!done ? (
            <>
              <Button size="sm" variant="ghost" className="h-6 px-1.5 text-[10px]" disabled={busy} onClick={onOpen} title="Görüşme kaydı gir"><Check className="h-3 w-3" /> Kayıt</Button>
              <Button size="sm" variant="ghost" className="h-6 px-1.5 text-[10px]" disabled={busy} onClick={onSkip} title="Atla"><SkipForward className="h-3 w-3" /></Button>
            </>
          ) : a.status !== 'Completed' ? (
            <Button size="sm" variant="ghost" className="h-6 px-1.5 text-[10px]" disabled={busy} onClick={onReopen} title="Yeniden planlıya al"><RotateCcw className="h-3 w-3" /></Button>
          ) : null}
          {a.source === 'Manual' && !done && (
            <Button size="sm" variant="ghost" className="h-6 px-1.5 text-[10px] text-red-400" disabled={busy} onClick={onDelete} title="Sil">×</Button>
          )}
        </div>
      </div>
    </div>
  );
}

// ── Sayfa ────────────────────────────────────────────────────────────────────

export function CallPlanPage() {
  const { toast } = useToast();
  const navigate = useNavigate();
  const today = localDay(new Date());
  const [weekStart, setWeekStart] = useState(startOfWeek(today));
  const [showWeekend, setShowWeekend] = useState(false);
  const [kindFilter, setKindFilter] = useState<AppointmentKind | ''>('');
  const [statusFilter, setStatusFilter] = useState<AppointmentStatus | ''>('');
  const [createFor, setCreateFor] = useState<{ day?: string } | null>(null);
  const [completing, setCompleting] = useState<Appointment | null>(null);

  const days = useMemo(() => Array.from({ length: showWeekend ? 7 : 5 }, (_, i) => addDays(weekStart, i)), [weekStart, showWeekend]);
  const { data: items = [], isLoading, isError, error } = useAppointments({ from: weekStart, to: addDays(weekStart, 6), kind: kindFilter, status: statusFilter });
  const { data: plan } = useCallPlanStatus();
  const run = useRunCallPlan();
  const rebuild = useRebuildCallPlan();
  const [rebuildOpen, setRebuildOpen] = useState(false);
  const skip = useSkipAppointment();
  const update = useUpdateAppointment();
  const remove = useDeleteAppointment();
  const busy = skip.isPending || update.isPending || remove.isPending;

  const byDay = useMemo(() => {
    const map = new Map<string, Appointment[]>();
    for (const a of items) {
      const d = localDay(new Date(a.scheduledAt));
      if (!map.has(d)) map.set(d, []);
      map.get(d)!.push(a);
    }
    return map;
  }, [items]);

  function quota(kind: AppointmentKind): number {
    if (!plan) return 0;
    return kind === 'NewCall' ? plan.newCallsPerDay : kind === 'FollowUpCall' ? plan.followUpsPerDay : kind === 'SatisfactionCall' ? plan.satisfactionPerDay : 0;
  }

  async function onRun() {
    try {
      const r = await run.mutateAsync();
      toast({ title: r.created > 0 ? `${r.created} randevu planlandı` : 'Plan güncel', description: r.summary });
    } catch (err) { toast({ title: 'Plan çalıştırılamadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function onRebuild() {
    try {
      const r = await rebuild.mutateAsync();
      toast({ title: 'Plan yeniden oluşturuldu', description: r.summary });
      setRebuildOpen(false);
    } catch (err) { toast({ title: 'Yeniden oluşturulamadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function onSkip(a: Appointment) {
    try { await skip.mutateAsync({ id: a.id }); } catch (err) { toast({ title: 'Olmadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function onReopen(a: Appointment) {
    try { await update.mutateAsync({ id: a.id, status: 'Planned' }); } catch (err) { toast({ title: 'Olmadı', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function onDelete(a: Appointment) {
    try { await remove.mutateAsync(a.id); } catch (err) { toast({ title: 'Silinemedi', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }

  const weekTotals = useMemo(() => {
    const planned = items.filter(a => a.status === 'Planned').length;
    const done = items.filter(a => a.status === 'Completed').length;
    const noAnswer = items.filter(a => a.status === 'NoAnswer').length;
    return { planned, done, noAnswer, total: items.length };
  }, [items]);

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-foreground flex items-center gap-2"><CalendarDays className="h-6 w-6" /> Görüşme Takvimi</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Her iş günü CSO planı {plan ? `${plan.newCallsPerDay} yeni arama, ${plan.followUpsPerDay} takip, ${plan.satisfactionPerDay} memnuniyet araması` : 'arama'} önerir; randevuya tıklayıp görüşme kaydını buradan girersin.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="outline" size="sm" onClick={() => navigate('/pipeline/firsatlar')} title="Eski fırsat panosu"><KanbanSquare className="h-4 w-4" /> Fırsatlar</Button>
          {plan?.canRun && (
            <>
              <Button variant="outline" size="sm" disabled={run.isPending} onClick={onRun} title="Boş kotaları şimdi doldur (önümüzdeki iş günleri)">
                {run.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Bot className="h-4 w-4" />} CSO planını çalıştır
              </Button>
              <Button variant="ghost" size="sm" disabled={rebuild.isPending} onClick={() => setRebuildOpen(true)} title="Ajanın planlı randevularını kaldırıp güncel kurallarla yeniden planla">
                <RotateCcw className="h-4 w-4" /> Yeniden oluştur
              </Button>
            </>
          )}
          <Button size="sm" onClick={() => setCreateFor({})}><CalendarPlus className="h-4 w-4" /> Randevu ekle</Button>
        </div>
      </div>

      {/* Hafta gezinme + süzgeçler */}
      <div className="flex flex-wrap items-center gap-2">
        <div className="inline-flex items-center rounded-md border border-border">
          <Button variant="ghost" size="sm" className="h-9 px-2" onClick={() => setWeekStart(addDays(weekStart, -7))}><ChevronLeft className="h-4 w-4" /></Button>
          <button type="button" className="px-3 text-sm font-medium hover:text-primary" onClick={() => setWeekStart(startOfWeek(today))}>
            {fmtDayHeader(weekStart)} – {fmtDayHeader(addDays(weekStart, showWeekend ? 6 : 4))}
            {weekStart === startOfWeek(today) && <span className="ml-1.5 text-[10px] text-muted-foreground">bu hafta</span>}
          </button>
          <Button variant="ghost" size="sm" className="h-9 px-2" onClick={() => setWeekStart(addDays(weekStart, 7))}><ChevronRight className="h-4 w-4" /></Button>
        </div>
        <button type="button" onClick={() => setShowWeekend(v => !v)} className={cn('rounded-full border px-2.5 py-1 text-xs', showWeekend ? 'bg-primary text-primary-foreground border-primary' : 'border-border text-muted-foreground')}>Hafta sonu</button>
        <div className="ml-auto flex flex-wrap items-center gap-1.5">
          <Filter className="h-3.5 w-3.5 text-muted-foreground" />
          {([['', 'Tümü'], ...Object.entries(KIND_LABEL)] as [AppointmentKind | '', string][]).map(([v, l]) => (
            <button key={v || 'all'} type="button" onClick={() => setKindFilter(v)}
              className={cn('rounded-full border px-2 py-0.5 text-[11px]', kindFilter === v ? (v ? KIND_STYLE[v] + ' ring-1 ring-primary/40' : 'bg-primary text-primary-foreground border-primary') : 'border-border text-muted-foreground hover:text-foreground')}>
              {l}
            </button>
          ))}
          <span className="mx-1 h-4 w-px bg-border" />
          {([['', 'Hepsi'], ['Planned', 'Planlı'], ['Completed', 'Yapıldı'], ['NoAnswer', 'Ulaşılamadı'], ['Skipped', 'Atlandı']] as [AppointmentStatus | '', string][]).map(([v, l]) => (
            <button key={v || 'any'} type="button" onClick={() => setStatusFilter(v)}
              className={cn('rounded-full border px-2 py-0.5 text-[11px]', statusFilter === v ? 'bg-primary text-primary-foreground border-primary' : 'border-border text-muted-foreground hover:text-foreground')}>
              {l}
            </button>
          ))}
        </div>
      </div>

      {/* Plan durumu */}
      <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
        <span className="inline-flex items-center gap-1"><Bot className="h-3.5 w-3.5" /> CSO planı:
          {plan?.lastRanAt ? ` son koşu ${new Date(plan.lastRanAt).toLocaleString('tr-TR', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })}${plan.lastCreatedCount != null ? ` · ${plan.lastCreatedCount} randevu` : ''}` : ' henüz koşmadı (her gün 05:00\'ten sonra otomatik)'}
        </span>
        <span>Bu hafta: {weekTotals.total} randevu · {weekTotals.done} yapıldı · {weekTotals.noAnswer} ulaşılamadı · {weekTotals.planned} planlı</span>
        {plan && !plan.enabled && <span className="inline-flex items-center gap-1 text-amber-400"><AlertTriangle className="h-3.5 w-3.5" /> Planlayıcı kapalı (CallPlan:Enabled=false)</span>}
      </div>

      {isError && <div className="text-sm text-red-400 flex items-center gap-2"><AlertTriangle className="h-4 w-4" /> {errorMessage(error, 'Randevular alınamadı.')}</div>}

      {/* Hafta ızgarası */}
      <div className={cn('grid gap-3', showWeekend ? 'lg:grid-cols-7 md:grid-cols-4' : 'lg:grid-cols-5 md:grid-cols-3')}>
        {days.map((day, i) => {
          const list = (byDay.get(day) ?? []).slice().sort((a, b) => a.scheduledAt.localeCompare(b.scheduledAt));
          const isToday = day === today;
          const isPast = day < today;
          const counts = (k: AppointmentKind) => list.filter(a => a.kind === k && a.status !== 'Skipped').length;
          const doneCount = (k: AppointmentKind) => list.filter(a => a.kind === k && (a.status === 'Completed' || a.status === 'NoAnswer')).length;
          return (
            <Card key={day} className={cn(isToday && 'border-primary/50 ring-1 ring-primary/20', isPast && 'opacity-90')}>
              <CardContent className="p-2.5 space-y-2">
                <div className="flex items-center justify-between">
                  <div>
                    <div className={cn('text-sm font-semibold', isToday ? 'text-primary' : 'text-foreground')}>{DAY_NAMES[i]} <span className="text-muted-foreground font-normal">{fmtDayHeader(day)}</span></div>
                  </div>
                  <Button variant="ghost" size="sm" className="h-7 w-7 p-0" title="Bu güne randevu ekle" onClick={() => setCreateFor({ day })}><CalendarPlus className="h-3.5 w-3.5" /></Button>
                </div>
                {/* Kota çubukları */}
                {i < 5 && plan && (
                  <div className="space-y-0.5">
                    {CALL_KINDS.map(k => {
                      const q = quota(k); if (q === 0) return null;
                      const c = counts(k); const d = doneCount(k);
                      return (
                        <div key={k} className="flex items-center gap-1.5 text-[10px] text-muted-foreground" title={`${KIND_LABEL[k]}: ${d} yapıldı / ${c} planlı / ${q} kota`}>
                          <span className="w-16 truncate">{KIND_LABEL[k].split(' ')[0]}</span>
                          <div className="flex-1 h-1.5 rounded bg-muted/60 overflow-hidden flex">
                            <div className={cn('h-full', k === 'NewCall' ? 'bg-sky-500' : k === 'FollowUpCall' ? 'bg-violet-500' : 'bg-emerald-500')} style={{ width: `${Math.min(100, (d / Math.max(q, c)) * 100)}%` }} />
                            <div className={cn('h-full opacity-40', k === 'NewCall' ? 'bg-sky-500' : k === 'FollowUpCall' ? 'bg-violet-500' : 'bg-emerald-500')} style={{ width: `${Math.min(100, ((c - d) / Math.max(q, c)) * 100)}%` }} />
                          </div>
                          <span className="tabular-nums w-9 text-right">{d}/{c}{c < q ? `·${q}` : ''}</span>
                        </div>
                      );
                    })}
                  </div>
                )}
                <div className="space-y-1.5 min-h-[80px]">
                  {isLoading && <div className="text-xs text-muted-foreground flex items-center gap-1"><Loader2 className="h-3 w-3 animate-spin" /> Yükleniyor…</div>}
                  {!isLoading && list.length === 0 && <div className="text-[11px] text-muted-foreground py-3 text-center">{i >= 5 ? '—' : isPast ? 'Kayıt yok' : 'Henüz plan yok'}</div>}
                  {list.map(a => (
                    <AppointmentCard key={a.id} a={a} busy={busy}
                      onOpen={() => setCompleting(a)} onSkip={() => onSkip(a)} onReopen={() => onReopen(a)} onDelete={() => onDelete(a)} />
                  ))}
                </div>
              </CardContent>
            </Card>
          );
        })}
      </div>

      <Dialog open={rebuildOpen} onOpenChange={(o) => !o && !rebuild.isPending && setRebuildOpen(false)}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Plan yeniden oluşturulsun mu?</DialogTitle>
            <DialogDescription>CSO planının önümüzdeki iş günlerindeki <b>henüz yapılmamış</b> randevuları kaldırılır ve güncel kurallarla yeniden seçilir. Elle açtığın randevular ve kapatılmış kayıtlar korunur.</DialogDescription>
          </DialogHeader>
          <DialogFooter className="gap-2">
            <Button variant="outline" size="sm" disabled={rebuild.isPending} onClick={() => setRebuildOpen(false)}>Vazgeç</Button>
            <Button size="sm" disabled={rebuild.isPending} onClick={onRebuild}>{rebuild.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <RotateCcw className="h-4 w-4" />} Yeniden oluştur</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      <CreateAppointmentDialog open={createFor !== null} onClose={() => setCreateFor(null)} day={createFor?.day} />
      <CompleteAppointmentDialog appointment={completing} onClose={() => setCompleting(null)} />
    </div>
  );
}
