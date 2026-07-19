import { useState } from 'react';
import {
  RefreshCw, Check, X, AlertTriangle, Bug, Hash, FileCode2, Lightbulb, Inbox,
  Clock, CheckCircle2, XCircle, CheckCheck, ExternalLink,
} from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter,
} from '@/components/ui/dialog';
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
  Triaged:  { label: 'Bekliyor',      className: 'bg-yellow-500/15 text-yellow-400 border-yellow-500/30' },
  Approved: { label: 'Onaylandı',     className: 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30' },
  Rejected: { label: 'Reddedildi',    className: 'bg-red-500/15 text-red-400 border-red-500/30' },
  Fixing:   { label: 'Düzeltiliyor',  className: 'bg-purple-500/15 text-purple-400 border-purple-500/30' },
  Fixed:    { label: 'Tamamlandı',    className: 'bg-sky-500/15 text-sky-400 border-sky-500/30' },
  Failed:   { label: 'Fix Başarısız', className: 'bg-rose-500/15 text-rose-400 border-rose-500/30' },
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

/** Guid ids (Liftdesk) are unreadable in full — show the first block only. */
function shortId(id: string) {
  return id.length > 12 ? id.slice(0, 8) : id;
}

function errorMessage(err: unknown, fallback: string): string {
  if (
    err && typeof err === 'object' && 'response' in err &&
    err.response && typeof err.response === 'object' && 'data' in err.response
  ) {
    const data = (err.response as { data?: { message?: string; errors?: string[] } }).data;
    if (data?.message) return data.message;
    // ApiResponse.Fail puts the text into errors[], leaving message null.
    if (data?.errors?.length) return data.errors[0];
  }
  return fallback;
}

// ── Card ────────────────────────────────────────────────────────────────────

function TriageCard({
  card,
  actionable,
  onReject,
}: {
  card: ErrorTriageCard;
  actionable: boolean;
  onReject: (card: ErrorTriageCard) => void;
}) {
  const { toast } = useToast();
  const update = useUpdateErrorTriageStatus();

  async function approve() {
    try {
      await update.mutateAsync({ source: card.source, id: card.id, status: 'Approved' });
      toast({
        title: 'Kart onaylandı',
        description: `#${shortId(card.id)} · ${card.typeName ?? ''}`.trim(),
      });
    } catch (err) {
      toast({
        title: 'İşlem başarısız',
        description: errorMessage(err, 'Kaynak sistem işlemi reddetti. Lütfen tekrar deneyin.'),
        variant: 'destructive',
      });
    }
  }

  const busy = update.isPending;

  return (
    <Card className="border-border/50">
      <CardContent className="p-4 sm:p-5 space-y-4">
        {/* Header */}
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="flex flex-wrap items-center gap-2 min-w-0">
            <SourceBadge source={card.source} />
            {card.component && (
              <Pill className="border-border bg-muted/40 text-muted-foreground">{card.component}</Pill>
            )}
            <SeverityBadge severity={card.severity} />
            <Pill className="border-border bg-muted/40 text-muted-foreground">
              <Hash className="h-3 w-3" />{card.occurrenceCount}× tekrar
            </Pill>
            {!actionable && card.status && <StatusBadge status={card.status} />}
            {card.typeName && (
              <span className="text-sm font-semibold text-foreground truncate">{card.typeName}</span>
            )}
          </div>
          <span className="text-xs text-muted-foreground whitespace-nowrap">
            #{shortId(card.id)} · {formatDate(card.createdOn)}
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

        {/* Fix agent failure (Liftdesk) */}
        {card.failReason && (
          <p className="text-xs text-rose-400">
            <span className="font-semibold">Fix başarısız:</span> {card.failReason}
          </p>
        )}

        {/* Footer: actions (pending) or decision metadata (resolved) */}
        {actionable ? (
          <div className="flex items-center justify-end gap-2 pt-1">
            <Button
              variant="outline"
              size="sm"
              onClick={() => onReject(card)}
              disabled={busy}
              className="border-red-500/30 text-red-400 hover:bg-red-500/10 hover:text-red-300"
            >
              <X className="h-4 w-4 mr-1.5" />
              Reddet
            </Button>
            <Button
              size="sm"
              onClick={approve}
              disabled={busy}
              className="bg-emerald-600 hover:bg-emerald-700 text-white"
            >
              <Check className="h-4 w-4 mr-1.5" />
              {busy ? 'Onaylanıyor...' : 'Onayla'}
            </Button>
          </div>
        ) : (
          (card.approvedBy || card.updatedOn || card.rejectReason || card.fixPrUrl) && (
            <div className="flex flex-wrap items-center gap-x-4 gap-y-1 pt-1 text-xs text-muted-foreground border-t border-border/40">
              {card.approvedBy && <span className="pt-2">İşleyen: <span className="text-foreground">{card.approvedBy}</span></span>}
              {card.updatedOn && <span className="pt-2">Tarih: {formatDate(card.updatedOn)}</span>}
              {card.rejectReason && <span className="pt-2">Ret gerekçesi: <span className="text-foreground">{card.rejectReason}</span></span>}
              {card.fixPrUrl && (
                <a
                  href={card.fixPrUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="pt-2 inline-flex items-center gap-1 text-sky-400 hover:text-sky-300 font-medium"
                >
                  <ExternalLink className="h-3.5 w-3.5" /> PR'ı İncele
                </a>
              )}
            </div>
          )
        )}
      </CardContent>
    </Card>
  );
}

// ── Reject dialog ───────────────────────────────────────────────────────────
// Lives at page level (not inside TriageCard) so a background refetch that drops the card from
// the list can't unmount the dialog and silently discard the reason being typed.

function RejectDialog({ card, onClose }: { card: ErrorTriageCard; onClose: () => void }) {
  const { toast } = useToast();
  const update = useUpdateErrorTriageStatus();
  const [reason, setReason] = useState('');
  const busy = update.isPending;

  async function confirm() {
    try {
      await update.mutateAsync({
        source: card.source,
        id: card.id,
        status: 'Rejected',
        rejectReason: reason.trim() || undefined,
      });
      toast({
        title: 'Kart reddedildi',
        description: `#${shortId(card.id)} · ${card.typeName ?? ''}`.trim(),
      });
      onClose();
    } catch (err) {
      toast({
        title: 'İşlem başarısız',
        description: errorMessage(err, 'Kaynak sistem işlemi reddetti. Lütfen tekrar deneyin.'),
        variant: 'destructive',
      });
    }
  }

  return (
    <Dialog open onOpenChange={(o) => !o && !busy && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Kartı Reddet — #{shortId(card.id)}</DialogTitle>
        </DialogHeader>
        <div className="space-y-2">
          <p className="text-sm text-muted-foreground">
            Ret gerekçesi (opsiyonel){card.source === 'Liftdesk' ? ' — Liftdesk kaydına yazılır.' : '.'}
          </p>
          <Textarea
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder="Örn: Gürültü, gerçek bug değil"
            rows={3}
          />
        </div>
        <DialogFooter className="gap-2">
          <Button variant="outline" size="sm" onClick={onClose} disabled={busy}>
            Vazgeç
          </Button>
          <Button
            size="sm"
            onClick={confirm}
            disabled={busy}
            className="bg-red-600 hover:bg-red-700 text-white"
          >
            <X className="h-4 w-4 mr-1.5" />
            {busy ? 'Reddediliyor...' : 'Reddet'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Page ────────────────────────────────────────────────────────────────────

const FROM_DATE_KEY = 'errorTriage.fromDate';

export function ErrorTriagePage() {
  const [tab, setTab] = useState<TabValue>('Triaged');
  const [fromDate, setFromDate] = useState<string>(() => localStorage.getItem(FROM_DATE_KEY) ?? '');
  const [rejectTarget, setRejectTarget] = useState<ErrorTriageCard | null>(null);

  const { data, isLoading, isFetching, refetch, isError, error } = useErrorTriage({
    status: tab,
    page: 1,
    pageSize: 50,
  });

  function changeFromDate(v: string) {
    setFromDate(v);
    if (v) localStorage.setItem(FROM_DATE_KEY, v);
    else localStorage.removeItem(FROM_DATE_KEY);
  }

  // Hide older/recurring cards: keep only those created on/after the chosen date (compares ISO date part).
  const allCards = data?.cards ?? [];
  const warning = data?.warning ?? null;
  const cards = fromDate
    ? allCards.filter((c) => !c.createdOn || c.createdOn.slice(0, 10) >= fromDate)
    : allCards;
  const hiddenCount = allCards.length - cards.length;
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
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-foreground">Hata Onay Ekranı</h1>
          <p className="text-sm text-muted-foreground mt-1">
            Rezerval + Liftdesk · Onayla / Reddet · geçmişi görüntüle
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <div className="flex items-center gap-1.5">
            <span className="text-xs text-muted-foreground whitespace-nowrap">Şu tarihten sonra:</span>
            <Input
              type="date"
              value={fromDate}
              onChange={(e) => changeFromDate(e.target.value)}
              className="h-8 w-40 text-sm"
            />
            {fromDate && (
              <Button variant="ghost" size="sm" className="h-8 px-2 text-xs" onClick={() => changeFromDate('')}>
                Temizle
              </Button>
            )}
          </div>
          <Button variant="outline" size="sm" onClick={() => refetch()} disabled={isFetching}>
            <RefreshCw className={`h-4 w-4 mr-1.5 ${isFetching ? 'animate-spin' : ''}`} />
            Yenile
          </Button>
        </div>
      </div>

      {fromDate && hiddenCount > 0 && (
        <p className="text-xs text-muted-foreground -mt-2">
          {new Date(fromDate).toLocaleDateString('tr-TR')} öncesi {hiddenCount} kart gizlendi.
        </p>
      )}

      {/* Partial-source warning (e.g. one queue unreachable) */}
      {warning && (
        <p className="flex items-center gap-1.5 text-xs text-amber-400 -mt-2">
          <AlertTriangle className="h-3.5 w-3.5 flex-shrink-0" /> {warning}
        </p>
      )}

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
              {errorMessage(error, 'Hata-triage kaynaklarına bağlanılamadı.')}
            </p>
          </CardContent>
        </Card>
      ) : cards.length === 0 ? (
        warning ? (
          // Zero cards while a source failed is NOT a processed queue — don't reassure the operator.
          <Card className="border-amber-500/30">
            <CardContent className="p-8 text-center text-amber-400">
              <AlertTriangle className="h-8 w-8 mx-auto mb-3" />
              <p className="font-medium">Kaynaklara ulaşılamadı — liste eksik olabilir.</p>
              <p className="text-sm text-muted-foreground mt-1">{warning}</p>
            </CardContent>
          </Card>
        ) : (
          <Card className="border-border/50">
            <CardContent className="p-12 text-center text-muted-foreground">
              <Inbox className="h-10 w-10 mx-auto mb-3 opacity-40" />
              <p className="font-medium text-foreground">Kayıt yok</p>
              <p className="text-sm mt-1">{EMPTY_TEXT[tab]}</p>
            </CardContent>
          </Card>
        )
      ) : (
        <div className="space-y-4">
          {cards.map((card) => (
            <TriageCard
              key={`${card.source}-${card.id}`}
              card={card}
              actionable={actionable}
              onReject={setRejectTarget}
            />
          ))}
        </div>
      )}

      {rejectTarget && <RejectDialog card={rejectTarget} onClose={() => setRejectTarget(null)} />}
    </div>
  );
}
