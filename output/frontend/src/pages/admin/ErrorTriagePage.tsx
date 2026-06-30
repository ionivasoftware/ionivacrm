import { useState } from 'react';
import {
  RefreshCw, Check, X, AlertTriangle, Bug, Hash, FileCode2, Lightbulb, Inbox,
} from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useToast } from '@/hooks/use-toast';
import { useErrorTriage, useUpdateErrorTriageStatus, type ErrorTriageCard } from '@/api/errorTriage';

// ── Helpers ───────────────────────────────────────────────────────────────────

const SEVERITY_CONFIG: Record<string, { label: string; className: string }> = {
  Critical: { label: 'Kritik',  className: 'bg-red-500/15 text-red-400 border-red-500/30' },
  High:     { label: 'Yüksek',  className: 'bg-orange-500/15 text-orange-400 border-orange-500/30' },
  Medium:   { label: 'Orta',    className: 'bg-yellow-500/15 text-yellow-400 border-yellow-500/30' },
  Low:      { label: 'Düşük',   className: 'bg-blue-500/15 text-blue-400 border-blue-500/30' },
};

function SeverityBadge({ severity }: { severity: string | null }) {
  const cfg = severity ? SEVERITY_CONFIG[severity] : undefined;
  const fallback = { label: severity ?? '—', className: 'bg-slate-500/15 text-slate-400 border-slate-500/30' };
  const { label, className } = cfg ?? fallback;
  return (
    <span className={`inline-flex items-center gap-1 rounded-full border px-2.5 py-0.5 text-xs font-semibold ${className}`}>
      <AlertTriangle className="h-3 w-3" />{label}
    </span>
  );
}

function formatDate(iso: string | null) {
  if (!iso) return '—';
  return new Date(iso).toLocaleString('tr-TR', {
    day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit',
  });
}

/** Pulls the backend ApiResponse.message out of an axios error, with a fallback. */
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

// ── Card ──────────────────────────────────────────────────────────────────────

function TriageCard({ card }: { card: ErrorTriageCard }) {
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
            <SeverityBadge severity={card.severity} />
            <span className="inline-flex items-center gap-1 rounded-full border border-border bg-muted/40 px-2.5 py-0.5 text-xs font-medium text-muted-foreground">
              <Hash className="h-3 w-3" />{card.occurrenceCount}× tekrar
            </span>
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

        {/* Actions */}
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
      </CardContent>
    </Card>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function ErrorTriagePage() {
  const { data, isLoading, isFetching, refetch, isError, error } = useErrorTriage({
    status: 'Triaged',
    page: 1,
    pageSize: 50,
  });

  const cards = data ?? [];

  return (
    <div className="p-6 space-y-6 max-w-4xl mx-auto">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-foreground">Hata Onay Ekranı</h1>
          <p className="text-sm text-muted-foreground mt-1">
            RezervAl tarafından triage edilmiş hatalar · Onayla / Reddet
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={() => refetch()} disabled={isFetching}>
          <RefreshCw className={`h-4 w-4 mr-1.5 ${isFetching ? 'animate-spin' : ''}`} />
          Yenile
        </Button>
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
            <p className="font-medium text-foreground">Bekleyen hata kartı yok</p>
            <p className="text-sm mt-1">Triage edilmiş tüm hatalar değerlendirildi.</p>
          </CardContent>
        </Card>
      ) : (
        <div className="space-y-4">
          {cards.map((card) => (
            <TriageCard key={card.triageId} card={card} />
          ))}
        </div>
      )}
    </div>
  );
}
