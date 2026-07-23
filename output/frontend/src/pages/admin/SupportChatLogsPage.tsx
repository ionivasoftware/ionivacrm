import { useEffect, useState } from 'react';
import {
  RefreshCw, Search, MessageCircle, Inbox, AlertTriangle, Info, User, Building2, Clock,
  ChevronLeft, ChevronRight, X, Bot,
} from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { useSupportChatLogs, type SupportChatLog } from '@/api/supportChatLogs';

// ── Helpers ─────────────────────────────────────────────────────────────────

function formatDate(iso: string) {
  return new Date(iso).toLocaleString('tr-TR', {
    day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit',
  });
}

// The picker gives a bare calendar date and rows are DISPLAYED in the browser's local timezone
// (formatDate has no timeZone override), so the filter boundaries must be the LOCAL day expressed in
// UTC — otherwise near-midnight chats fall on the wrong side of the filter. `new Date("Y-M-DThh:mm:ss")`
// (no trailing Z) parses as LOCAL time per the ES spec; toISOString() then converts to the UTC instant
// the EMS endpoint compares against.

/** A bare date (YYYY-MM-DD) → the UTC instant of that day's local 00:00. */
function toStartIso(d: string): string | undefined {
  return d ? new Date(`${d}T00:00:00`).toISOString() : undefined;
}

/** A bare end date → the UTC instant of the NEXT local day's 00:00, so the whole chosen local day is
 *  included (EMS compares with <=). */
function toEndIso(d: string): string | undefined {
  if (!d) return undefined;
  const dt = new Date(`${d}T00:00:00`);
  dt.setDate(dt.getDate() + 1);
  return dt.toISOString();
}

function errorMessage(err: unknown, fallback: string): string {
  if (
    err && typeof err === 'object' && 'response' in err &&
    err.response && typeof err.response === 'object' && 'data' in err.response
  ) {
    const data = (err.response as { data?: { message?: string; errors?: string[] } }).data;
    if (data?.message) return data.message;
    if (data?.errors?.length) return data.errors[0];
  }
  return fallback;
}

// ── Log card ──────────────────────────────────────────────────────────────────

function LogCard({ log }: { log: SupportChatLog }) {
  return (
    <Card className="border-border/50">
      <CardContent className="p-4 space-y-3">
        {/* Meta */}
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
          <span className="inline-flex items-center gap-1">
            <Building2 className="h-3.5 w-3.5" /> {log.projectName || '—'}
          </span>
          <span className="inline-flex items-center gap-1">
            <User className="h-3.5 w-3.5" /> {log.userName || log.userEmail || 'Bilinmeyen kullanıcı'}
          </span>
          {log.userName && log.userEmail && <span className="text-muted-foreground/70">{log.userEmail}</span>}
          <span className="inline-flex items-center gap-1 ml-auto whitespace-nowrap">
            <Clock className="h-3.5 w-3.5" /> {formatDate(log.createdAt)}
          </span>
        </div>

        {/* Question */}
        <div className="flex items-start gap-2">
          <div className="w-6 h-6 rounded-full bg-blue-500/15 flex items-center justify-center flex-shrink-0">
            <User className="h-3.5 w-3.5 text-blue-400" />
          </div>
          <p className="text-sm font-medium text-foreground whitespace-pre-wrap flex-1 pt-0.5">{log.question}</p>
        </div>

        {/* Answer */}
        <div className="flex items-start gap-2">
          <div className="w-6 h-6 rounded-full bg-violet-500/15 flex items-center justify-center flex-shrink-0">
            <Bot className="h-3.5 w-3.5 text-violet-400" />
          </div>
          <div className="flex-1 rounded-lg bg-muted/40 border border-border/50 p-3 max-h-64 overflow-y-auto">
            <p className="text-sm text-muted-foreground whitespace-pre-wrap">{log.answer}</p>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

// ── Page ────────────────────────────────────────────────────────────────────

export function SupportChatLogsPage() {
  const [searchInput, setSearchInput] = useState('');
  const [appliedSearch, setAppliedSearch] = useState('');
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [page, setPage] = useState(1);

  const { data, isLoading, isFetching, refetch, isError, error } = useSupportChatLogs({
    search: appliedSearch || undefined,
    startDate: toStartIso(startDate),
    endDate: toEndIso(endDate),
    page,
    pageSize: 20,
  });

  const logs = data?.items ?? [];

  // Clamp the page down when the result set shrinks (e.g. a refresh after the 04:30 UTC cleanup
  // removes older rows) so the operator is never stranded on an empty trailing page.
  useEffect(() => {
    if (data && data.totalPages > 0 && page > data.totalPages) {
      setPage(data.totalPages);
    }
  }, [data, page]);

  function applySearch() {
    setAppliedSearch(searchInput.trim());
    setPage(1);
  }

  function clearFilters() {
    setSearchInput('');
    setAppliedSearch('');
    setStartDate('');
    setEndDate('');
    setPage(1);
  }

  function changeStart(v: string) { setStartDate(v); setPage(1); }
  function changeEnd(v: string) { setEndDate(v); setPage(1); }

  const hasFilters = !!(appliedSearch || startDate || endDate);

  return (
    <div className="p-6 space-y-6 max-w-4xl mx-auto">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-foreground flex items-center gap-2">
            <MessageCircle className="h-6 w-6" /> Destek Sohbetleri
          </h1>
          <p className="text-sm text-muted-foreground mt-1">
            Uygulama içi Destek Asistanı sohbet logları · salt okunur · en yeni önce
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={() => refetch()} disabled={isFetching}>
          <RefreshCw className={`h-4 w-4 mr-1.5 ${isFetching ? 'animate-spin' : ''}`} /> Yenile
        </Button>
      </div>

      {/* 10-day retention notice */}
      <div className="flex items-start gap-2 rounded-lg border border-blue-500/30 bg-blue-500/5 px-3 py-2">
        <Info className="h-4 w-4 text-blue-400 flex-shrink-0 mt-0.5" />
        <p className="text-xs text-blue-300">
          Sohbet logları Liftdesk tarafında yalnızca <span className="font-semibold">10 gün</span> saklanır;
          daha eski sohbetler burada görünmez. Kalıcı analiz gerekiyorsa düzenli dışa aktarın.
        </p>
      </div>

      {/* Filters */}
      <div className="flex flex-wrap gap-2">
        <div className="flex items-center gap-1.5 flex-1 min-w-[220px]">
          <Input
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') applySearch(); }}
            placeholder="Soru, yanıt veya e-posta ara..."
            className="h-9"
          />
          <Button variant="outline" size="sm" className="h-9" onClick={applySearch}>
            <Search className="h-4 w-4" />
          </Button>
        </div>
        <div className="flex items-center gap-1.5">
          <span className="text-xs text-muted-foreground whitespace-nowrap">Tarih:</span>
          <Input type="date" value={startDate} onChange={(e) => changeStart(e.target.value)} className="h-9 w-40 text-sm" />
          <span className="text-xs text-muted-foreground">–</span>
          <Input type="date" value={endDate} onChange={(e) => changeEnd(e.target.value)} className="h-9 w-40 text-sm" />
        </div>
        {hasFilters && (
          <Button variant="ghost" size="sm" className="h-9" onClick={clearFilters}>
            <X className="h-4 w-4 mr-1" /> Temizle
          </Button>
        )}
      </div>

      {/* Content */}
      {isLoading ? (
        <div className="space-y-4">
          {Array.from({ length: 3 }).map((_, i) => (
            <Card key={i} className="border-border/50">
              <CardContent className="p-4 space-y-3">
                <Skeleton className="h-4 w-1/3" />
                <Skeleton className="h-4 w-2/3" />
                <Skeleton className="h-16 w-full" />
              </CardContent>
            </Card>
          ))}
        </div>
      ) : isError ? (
        <Card className="border-red-500/30">
          <CardContent className="p-8 text-center text-red-400">
            <AlertTriangle className="h-8 w-8 mx-auto mb-3" />
            <p className="font-medium">Sohbet logları yüklenemedi.</p>
            <p className="text-sm text-muted-foreground mt-1">
              {errorMessage(error, 'Liftdesk sohbet log servisine bağlanılamadı.')}
            </p>
          </CardContent>
        </Card>
      ) : logs.length === 0 ? (
        <Card className="border-border/50">
          <CardContent className="p-12 text-center text-muted-foreground">
            <Inbox className="h-10 w-10 mx-auto mb-3 opacity-40" />
            <p className="font-medium text-foreground">Kayıt yok</p>
            <p className="text-sm mt-1">
              {hasFilters ? 'Seçilen filtrelere uyan sohbet bulunamadı.' : 'Henüz destek sohbeti kaydı yok.'}
            </p>
          </CardContent>
        </Card>
      ) : (
        <>
          <div className="space-y-3">
            {logs.map((log) => <LogCard key={log.id} log={log} />)}
          </div>

          {/* Pagination */}
          {data && data.totalPages > 1 && (
            <div className="flex items-center justify-between pt-2">
              <span className="text-xs text-muted-foreground">
                {data.totalCount} kayıt · Sayfa {data.page}/{data.totalPages}
              </span>
              <div className="flex items-center gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  disabled={!data.hasPreviousPage}
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                >
                  <ChevronLeft className="h-4 w-4" />
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  disabled={!data.hasNextPage}
                  onClick={() => setPage((p) => p + 1)}
                >
                  <ChevronRight className="h-4 w-4" />
                </Button>
              </div>
            </div>
          )}
        </>
      )}
    </div>
  );
}
