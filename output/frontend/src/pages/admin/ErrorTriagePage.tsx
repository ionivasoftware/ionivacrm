import { useState } from 'react';
import {
  RefreshCw, Check, X, AlertTriangle, Bug, Hash, FileCode2, Lightbulb, Inbox,
  Clock, CheckCircle2, XCircle, CheckCheck,
} from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { cn } from '@/lib/utils';
import { useToast } from '@/hooks/use-toast';
import {
  useErrorTriage, useUpdateErrorTriageStatus,
  type ErrorTriageCard, type ErrorTriageSource,
} from '@/api/errorTriage';

// ── Config ──────────────────────────────────────────────────────────────────

type TabValue = 'Triaged' | 'Approved' | 'Rejected' | 'Fixed';

const TABS: { value: TabValue; label: string; icon: React.ComponentType<{ className?: string }> }[] = [
  { value: 'Triaged',  label: 'Bekleyen',    icon: Clock },
  { value: 'Approved', label: 'Onaylanan',   icon: CheckCircle2 },
  { value: 'Rejected', label: 'Reddedilen',  icon: XCircle },
  { value: 'Fixed',    label: 'Tamamlanan',  icon: CheckCheck },
];

const SEVERITY_CONFIG: Record<string, { label: string; className: string }> = {
  Critical: { label: 'Kritik',  className: 'bg-red-500/15 text-red-400 border-red-500/30' },
  High:     { label: 'Yüksek',  className: 'bg-orange-500/15 text-orange-400 border-orange-500/30' },
  Medium:   { label: 'Orta',    className: 'bg-yellow-500/15 text-yellow-400 border-yellow-500/30' },
  Low:      { label: 'Düşük',   className: 'bg-blue-500/15 text-blue-400 border-blue-500/30' },
};

const STATUS_CONFIG: Record<string, { label: string; className: string }> = {
  Triaged:  { label: 'Bekliyor',    className: 'bg-yellow-500/15 text-yellow-400 border-yellow-500/30' },
  Approved: { label: 'Onaylandı',   className: 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30' },
  Rejected: { label: 'Reddedildi',  className: 'bg-red-500/15 text-red-400 border-red-500/30' },
  Fixed:    { label: 'Tamamlandı',  className: 'bg-sky-500/15 text-sky-400 border-sky-500/30' },
};

const SOURCE_CONFIG: Record<ErrorTriageSource, { label: string; className: string }> = {
  Rezerval: { label: 'Rezerval', className: 'bg-indigo-500/15 text-indigo-300 border-indigo-500/30' },
  Liftdesk: { label: 'Liftdesk', className: 'bg-cyan-500/15 text-cyan-300 border-cyan-500/30' },
};

// ── Badges ──────────────────────────────────────────────────────────────────

function Pill({ className, children }: { className: string; children: React.ReactNode }) {
  return (
    <span className={`inline-flex items-center gap-1 rounded-full border px-2.5 py-0.5 text-xs font-semibold ${className}`}>
      {children}
    </span>
  );
}

function SourceBadge({ source }: { source: ErrorTriageSource }) {
  const cfg = SOURCE_CONFIG[source] ?? { label: source, className: 'bg-slate-500/15 text-slate-400 border-slate-500/30' };
  return <Pill className={cfg.className}>{cfg.label}</Pill>;
}

function SeverityBadge({ severity }: { severity: string | null }) {
  const cfg = severity ? SEVERITY_CONFIG[severity] : undefined;
  const { label, className } = cfg ?? { label: severity ?? '—', className: 'bg-slate-500/15 text-slate-400 border-slate-500/30' };
  return <Pill className={className}><AlertTriangle className="h-3 w-3" />{label}</Pill>;
}

function StatusBadge({ status }: { status: string }) {
  const cfg = STATUS_CONFIG[status] ?? { label: status, className: 'bg-slate-500/15 text-slate-400 border-slate-500/30' };
  return <Pill className={cfg.className}>{cfg.label}</Pill>;
}

// ── Helpers ─────────────────────────────────────────────────────────────────

function formatDate(iso: string | null) {
  if (!iso) return '—';
  return new Date(iso).toLocaleString('tr-TR', {
    day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit',
  });
}

function errorMessage(err: unknown, fallback: string): string {
  if (
    err && typeof err === 'object' && 'response' in err &&
    err.response && typeof err.response === 'object' && 'data' in err.response
  ) {
    const data = (err.response as { data?: { message?: string } }).data;
    if (data?.message) return data.message;
  }
  return fallback;
}

// ── Card ────────────────────────────────────────────────────────────────────

function TriageCard({ card, actionable }: { card: ErrorTriageCard; actionable: boolean }) {
  const { toast } = useToast();
  const update = useUpdateErrorTriageStatus();
  const [acting, setActing] = useState<'Approved' | 'Rejected' | null>(null);

  async function decide(status: 'Approved' | 'Rejected') {
    setActing(status);
    try {
      await update.mutateAsync({ triageId: card.triageId, status });
      toast({
        title: status === 'Approved' ? 'Kart onaylandı' : 'Kart reddedildi',
        description: `#${card.triageId} · ${card.typeName ?? ''}`.trim(),
      });
    } catch (err) {
      toast({
        title: 'İşlem başarısız',
        description: errorMessage(err, 'RezervAl işlemi reddetti. Lütfen tekrar deneyin.'),
        variant: 'destructive',
      });
    } finally {
      setActing(null);
    }
  }

  const busy = update.isPending && acting !== null;

  return (
    <Card className="border-border/50">
      <CardContent className="p-4 sm:p-5 space-y-4">
        {/* Header */}
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="flex flex-wrap items-center gap-2 min-w-0">
            <SourceBadge source={card.source} />
            <SeverityBadge severity={card.severity} />
            <Pill className="border-border bg-muted/40 text-muted-foreground">
              <Hash className="h-3 w-3" />{card.occurrenceCount}× tekrar
            </Pill>
            {!actionable && <StatusBadge status={card.status} />}
            {card.typeName && (
              <span className="text-sm font-semibold text-foreground truncate">{card.typeName}</span>
            )}
          </div>
          <span className="text-xs text-muted-foreground whitespace-nowrap">
            #{card.triageId} · {formatDate(card.createdOn)}
          </span>
        </div>

        {/* Root cause */}
        {card.rootCause && (
          <div className="space-y-1">
            <p className="flex items-center gap-1.5 text-xs font-semibold text-muted-foreground uppercase tracking-wider">
              <Bug className="h-3.5 w-3.5" /> Kök Neden
            </p>
            <p className="text-sm text-foreground">{card.rootCause}</p>
          </div>
        )}

        {/* Suggested fix */}
        {card.suggestedFix && (
          <div className="space-y-1">
            <p className="flex items-center gap-1.5 text-xs font-semibold text-muted-foreground uppercase tracking-wider">
              <Lightbulb className="h-3.5 w-3.5" /> Önerilen Çözüm
            </p>
            <p className="text-sm text-emerald-300">{card.suggestedFix}</p>
          </div>
        )}

        {/* Source file */}
        {card.sourceFile && (
          <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
            <FileCode2 className="h-3.5 w-3.5 flex-shrink-0" />
            <code className="font-mono break-all">{card.sourceFile}</code>
          </div>
        )}

        {/* Exception */}
        {card.exception && (
          <pre className="rounded-md bg-muted/50 border border-border/50 p-3 text-xs text-red-300 font-mono whitespace-pre-wrap break-words max-h-32 overflow-y-auto">
            {card.exception}
          </pre>
        )}

        {/* Footer: actions (pending) or decision metadata (resolved) */}
        {actionable ? (
          <div className="flex items-center justify-end gap-2 pt-1">
            <Button
              variant="outline"
              size="sm"
              onClick={() => decide('Rejected')}
              disabled={busy}
              className="border-red-500/30 text-red-400 hover:bg-red-500/10 hover:text-red-300"
            >
              <X className="h-4 w-4 mr-1.5" />
              {acting === 'Rejected' ? 'Reddediliyor...' : 'Reddet'}
            </Button>
            <Button
              size="sm"
              onClick={() => decide('Approved')}
              disabled={busy}
              className="bg-emerald-600 hover:bg-emerald-700 text-white"
            >
              <Check className="h-4 w-4 mr-1.5" />
              {acting === 'Approved' ? 'Onaylanıyor...' : 'Onayla'}
            </Button>
          </div>
        ) : (
          (card.approvedBy || card.updatedOn) && (
            <div className="flex flex-wrap items-center gap-x-4 gap-y-1 pt-1 text-xs text-muted-foreground border-t border-border/40">
              {card.approvedBy && <span className="pt-2">İşleyen: <span className="text-foreground">{card.approvedBy}</span></span>}
              {card.updatedOn && <span className="pt-2">Tarih: {formatDate(card.updatedOn)}</span>}
            </div>
          )
        )}
      </CardContent>
    </Card>
  );
}

// ── Page ────────────────────────────────────────────────────────────────────

export function ErrorTriagePage() {
  const [tab, setTab] = useState<TabValue>('Triaged');

  const { data, isLoading, isFetching, refetch, isError, error } = useErrorTriage({
    status: tab,
    page: 1,
    pageSize: 50,
  });

  const cards = data ?? [];
  const actionable = tab === 'Triaged';

  const EMPTY_TEXT: Record<TabValue, string> = {
    Triaged:  'Triage edilmiş tüm hatalar değerlendirildi.',
    Approved: 'Henüz onaylanmış kart yok.',
    Rejected: 'Henüz reddedilmiş kart yok.',
    Fixed:    'Henüz tamamlanmış kart yok.',
  };

  return (
    <div className="p-6 space-y-6 max-w-4xl mx-auto">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-foreground">Hata Onay Ekranı</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Triage edilmiş hatalar · Onayla / Reddet · geçmişi görüntüle
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={() => refetch()} disabled={isFetching}>
          <RefreshCw className={`h-4 w-4 mr-1.5 ${isFetching ? 'animate-spin' : ''}`} />
          Yenile
        </Button>
      </div>

      {/* Tabs */}
      <div className="flex flex-wrap gap-1 rounded-lg border border-border/50 bg-muted/30 p-1">
        {TABS.map((t) => {
          const Icon = t.icon;
          const active = tab === t.value;
          return (
            <button
              key={t.value}
              onClick={() => setTab(t.value)}
              className={cn(
                'flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                active
                  ? 'bg-primary text-primary-foreground shadow-sm'
                  : 'text-muted-foreground hover:text-foreground hover:bg-accent'
              )}
            >
              <Icon className="h-4 w-4" />
              {t.label}
            </button>
          );
        })}
      </div>

      {/* Content */}
      {isLoading ? (
        <div className="space-y-4">
          {Array.from({ length: 3 }).map((_, i) => (
            <Card key={i} className="border-border/50">
              <CardContent className="p-5 space-y-3">
                <Skeleton className="h-5 w-1/3" />
                <Skeleton className="h-4 w-full" />
                <Skeleton className="h-4 w-2/3" />
              </CardContent>
            </Card>
          ))}
        </div>
      ) : isError ? (
        <Card className="border-red-500/30">
          <CardContent className="p-8 text-center text-red-400">
            <AlertTriangle className="h-8 w-8 mx-auto mb-3" />
            <p className="font-medium">Hata kartları yüklenemedi.</p>
            <p className="text-sm text-muted-foreground mt-1">
              {errorMessage(error, 'RezervAl API’sine bağlanılamadı.')}
            </p>
          </CardContent>
        </Card>
      ) : cards.length === 0 ? (
        <Card className="border-border/50">
          <CardContent className="p-12 text-center text-muted-foreground">
            <Inbox className="h-10 w-10 mx-auto mb-3 opacity-40" />
            <p className="font-medium text-foreground">Kayıt yok</p>
            <p className="text-sm mt-1">{EMPTY_TEXT[tab]}</p>
          </CardContent>
        </Card>
      ) : (
        <div className="space-y-4">
          {cards.map((card) => (
            <TriageCard key={card.triageId} card={card} actionable={actionable} />
          ))}
        </div>
      )}
    </div>
  );
}
