import { useState } from 'react';
import { CalendarDays, CalendarPlus, Check, Clock, PhoneOff, SkipForward } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { useCustomerAppointments, STATUS_LABEL, type Appointment, type AppointmentStatus } from '@/api/appointments';
import { CompleteAppointmentDialog, KindPill } from './AppointmentDialogs';

const STATUS_ICON: Record<string, React.ComponentType<{ className?: string }>> = { Completed: Check, Skipped: SkipForward, NoAnswer: PhoneOff, Planned: Clock };

/** Müşteri detayı: planlı randevular + son kapananlar; buradan da görüşme kaydı girilir. */
export function UpcomingAppointments({ customerId, onCreate }: { customerId: string; onCreate: () => void }) {
  const { data: items = [] } = useCustomerAppointments(customerId, 8);
  const [completing, setCompleting] = useState<Appointment | null>(null);
  const planned = items.filter(a => a.status === 'Planned');
  const recent = items.filter(a => a.status !== 'Planned').slice(0, 3);
  if (items.length === 0) return null;

  return (
    <div className="rounded-lg border border-border bg-muted/10 px-3 py-2.5 space-y-2">
      <div className="flex items-center justify-between">
        <div className="text-xs font-semibold text-foreground inline-flex items-center gap-1.5"><CalendarDays className="h-3.5 w-3.5" /> Randevular</div>
        <Button variant="ghost" size="sm" className="h-7 px-2 text-xs" onClick={onCreate}><CalendarPlus className="h-3.5 w-3.5" /> Ekle</Button>
      </div>
      {planned.length === 0 && <div className="text-xs text-muted-foreground">Planlı randevu yok.</div>}
      {planned.map(a => (
        <div key={a.id} className="flex flex-wrap items-center gap-2 text-xs">
          <span className="font-mono text-muted-foreground">{new Date(a.scheduledAt).toLocaleString('tr-TR', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })}</span>
          <KindPill kind={a.kind} className="text-[10px] px-1.5" />
          {a.title && <span className="text-foreground">{a.title}</span>}
          {a.reason && <span className="text-muted-foreground truncate max-w-[320px]" title={a.reason}>{a.reason}</span>}
          <Button size="sm" variant="outline" className="h-6 px-2 text-[11px] ml-auto" onClick={() => setCompleting(a)}><Check className="h-3 w-3" /> Görüşme kaydı</Button>
        </div>
      ))}
      {recent.length > 0 && (
        <div className="pt-1 border-t border-border/50 space-y-1">
          {recent.map(a => {
            const Icon = STATUS_ICON[a.status] ?? Clock;
            return (
              <div key={a.id} className="flex items-center gap-2 text-[11px] text-muted-foreground">
                <Icon className={cn('h-3 w-3', a.status === 'Completed' ? 'text-emerald-400' : a.status === 'NoAnswer' ? 'text-amber-400' : '')} />
                <span>{new Date(a.scheduledAt).toLocaleDateString('tr-TR', { day: '2-digit', month: 'short' })}</span>
                <KindPill kind={a.kind} className="text-[10px] px-1.5" />
                <span>{STATUS_LABEL[a.status as AppointmentStatus] ?? a.status}</span>
              </div>
            );
          })}
        </div>
      )}
      <CompleteAppointmentDialog appointment={completing} onClose={() => setCompleting(null)} />
    </div>
  );
}
