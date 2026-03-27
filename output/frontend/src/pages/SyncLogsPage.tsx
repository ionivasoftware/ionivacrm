import { useState } from 'react';
import { RefreshCw, Play, CheckCircle2, XCircle, Clock, AlertTriangle, ChevronLeft, ChevronRight } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Skeleton } from '@/components/ui/skeleton';
import { useToast } from '@/hooks/use-toast';
import { useSyncLogs, useTriggerSync } from '@/api/sync';

// ── Helpers ───────────────────────────────────────────────────────────────────

const STATUS_CONFIG: Record<string, { label: string; className: string; icon: React.ReactNode }> = {
  Success:  { label: 'Başarılı',  className: 'bg-green-500/15 text-green-400 border-green-500/30',   icon: <CheckCircle2 className="h-3 w-3" /> },
  Failed:   { label: 'Hatalı',    className: 'bg-red-500/15 text-red-400 border-red-500/30',         icon: <XCircle className="h-3 w-3" /> },
  Pending:  { label: 'Bekliyor',  className: 'bg-yellow-500/15 text-yellow-400 border-yellow-500/30', icon: <Clock className="h-3 w-3" /> },
  Retrying: { label: 'Deneniyor', className: 'bg-orange-500/15 text-orange-400 border-orange-500/30', icon: <AlertTriangle className="h-3 w-3" /> },
};

const SOURCE_LABELS: Record<string, string> = {
  SaasA: 'EMS',
  SaasB: 'Rezerval',
};

const ENTITY_LABELS: Record<string, string> = {
  Customer:    'Müşteri',
  CrmCustomer: 'CRM Müşteri',
  Subscription:'Abonelik',
  Order:       'Sipariş',
  Webhook:     'Webhook',
};

function formatDate(iso: string | null) {
  if (!iso) return '—';
  return new Date(iso).toLocaleString('tr-TR', {
    day: '2-digit', month: '2-digit', year: 'numeric',
    hour: '2-digit', minute: '2-digit', second: '2-digit',
  });
}

function StatusBadge({ status }: { status: string }) {
  const cfg = STATUS_CONFIG[status] ?? { label: status, className: 'bg-slate-500/15 text-slate-400 border-slate-500/30', icon: null };
  return (
    <span className={`inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs font-semibold ${cfg.className}`}>
      {cfg.icon}{cfg.label}
    </span>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function SyncLogsPage() {
  const { toast } = useToast();
  const [page, setPage]     = useState(1);
  const [source, setSource] = useState('');
  const [status, setStatus] = useState('');

  const { data, isLoading, isFetching, refetch } = useSyncLogs({
    page, pageSize: 25, source: source || undefined, status: status || undefined,
  });
  const trigger = useTriggerSync();

  const items = data?.items ?? [];

  // Summary counts from current page (rough, for header cards)
  const successCount = items.filter(l => l.status === 'Success').length;
  const failedCount  = items.filter(l => l.status === 'Failed').length;
  const retryCount   = items.filter(l => l.status === 'Retrying').length;

  async function handleTrigger() {
    try {
      await trigger.mutateAsync();
      toast({ title: 'Sync başlatıldı', description: 'Arka planda çalışıyor, loglar 30 saniye içinde güncellenir.' });
    } catch {
      toast({ title: 'Hata', description: 'Sync başlatılamadı.', variant: 'destructive' });
    }
  }

  return (
    <div className="p-6 space-y-6 max-w-7xl mx-auto">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-foreground">Senkronizasyon Logları</h1>
          <p className="text-sm text-muted-foreground mt-1">
            SaaS sistemleriyle senkronizasyon geçmişi · Her 30 saniyede otomatik yenilenir
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="outline" size="sm" onClick={() => refetch()} disabled={isFetching}>
            <RefreshCw className={`h-4 w-4 mr-1.5 ${isFetching ? 'animate-spin' : ''}`} />
            Yenile
          </Button>
          <Button size="sm" onClick={handleTrigger} disabled={trigger.isPending}>
            <Play className="h-4 w-4 mr-1.5" />
            {trigger.isPending ? 'Başlatılıyor...' : 'Sync Başlat'}
          </Button>
        </div>
      </div>

      {/* Stats */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        {[
          { label: 'Toplam (bu sayfa)', value: items.length, color: 'text-foreground' },
          { label: 'Başarılı',          value: successCount,  color: 'text-green-400'  },
          { label: 'Hatalı',            value: failedCount,   color: 'text-red-400'    },
          { label: 'Yeniden Denenen',   value: retryCount,    color: 'text-orange-400' },
        ].map(s => (
          <Card key={s.label} className="border-border/50">
            <CardContent className="p-4">
              <p className="text-xs text-muted-foreground">{s.label}</p>
              <p className={`text-2xl font-bold mt-1 ${s.color}`}>{isLoading ? '—' : s.value}</p>
            </CardContent>
          </Card>
        ))}
      </div>

      {/* Filters */}
      <Card className="border-border/50">
        <CardHeader className="pb-3">
          <CardTitle className="text-base">Filtrele</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-wrap gap-3">
          <Select value={source || 'all'} onValueChange={v => { setSource(v === 'all' ? '' : v); setPage(1); }}>
            <SelectTrigger className="w-40 h-8 text-sm">
              <SelectValue placeholder="Kaynak" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">Tüm Kaynaklar</SelectItem>
              <SelectItem value="SaasA">EMS (SaaS A)</SelectItem>
              <SelectItem value="SaasB">Rezerval (SaaS B)</SelectItem>
            </SelectContent>
          </Select>

          <Select value={status || 'all'} onValueChange={v => { setStatus(v === 'all' ? '' : v); setPage(1); }}>
            <SelectTrigger className="w-40 h-8 text-sm">
              <SelectValue placeholder="Durum" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">Tüm Durumlar</SelectItem>
              <SelectItem value="Success">Başarılı</SelectItem>
              <SelectItem value="Failed">Hatalı</SelectItem>
              <SelectItem value="Retrying">Yeniden Deneniyor</SelectItem>
              <SelectItem value="Pending">Bekliyor</SelectItem>
            </SelectContent>
          </Select>
        </CardContent>
      </Card>

      {/* Table */}
      <Card className="border-border/50">
        <CardContent className="p-0">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border/50 bg-muted/30">
                  <th className="text-left px-4 py-3 text-xs font-medium text-muted-foreground">Zaman</th>
                  <th className="text-left px-4 py-3 text-xs font-medium text-muted-foreground">Kaynak</th>
                  <th className="text-left px-4 py-3 text-xs font-medium text-muted-foreground">Veri Tipi</th>
                  <th className="text-left px-4 py-3 text-xs font-medium text-muted-foreground">Durum</th>
                  <th className="text-left px-4 py-3 text-xs font-medium text-muted-foreground">Tamamlandı</th>
                  <th className="text-left px-4 py-3 text-xs font-medium text-muted-foreground">Deneme</th>
                  <th className="text-left px-4 py-3 text-xs font-medium text-muted-foreground">Hata</th>
                </tr>
              </thead>
              <tbody>
                {isLoading
                  ? Array.from({ length: 8 }).map((_, i) => (
                      <tr key={i} className="border-b border-border/30">
                        {Array.from({ length: 7 }).map((_, j) => (
                          <td key={j} className="px-4 py-3"><Skeleton className="h-4 w-full" /></td>
                        ))}
                      </tr>
                    ))
                  : items.length === 0
                  ? (
                      <tr>
                        <td colSpan={7} className="px-4 py-12 text-center text-muted-foreground">
                          Henüz sync logu yok.
                        </td>
                      </tr>
                    )
                  : items.map(log => (
                      <tr key={log.id} className="border-b border-border/30 hover:bg-muted/20 transition-colors">
                        <td className="px-4 py-3 text-xs text-muted-foreground whitespace-nowrap">
                          {formatDate(log.createdAt)}
                        </td>
                        <td className="px-4 py-3">
                          <Badge variant="outline" className="text-xs">
                            {SOURCE_LABELS[log.source] ?? log.source}
                          </Badge>
                        </td>
                        <td className="px-4 py-3 text-xs">
                          {ENTITY_LABELS[log.entityType] ?? log.entityType}
                        </td>
                        <td className="px-4 py-3">
                          <StatusBadge status={log.status} />
                        </td>
                        <td className="px-4 py-3 text-xs text-muted-foreground whitespace-nowrap">
                          {formatDate(log.syncedAt)}
                        </td>
                        <td className="px-4 py-3 text-xs text-center">
                          {log.retryCount > 0
                            ? <span className="text-orange-400 font-medium">{log.retryCount}</span>
                            : <span className="text-muted-foreground">0</span>
                          }
                        </td>
                        <td className="px-4 py-3 text-xs text-red-400 max-w-xs truncate" title={log.errorMessage ?? ''}>
                          {log.errorMessage ?? '—'}
                        </td>
                      </tr>
                    ))
                }
              </tbody>
            </table>
          </div>

          {/* Pagination */}
          {(data?.totalPages ?? 0) > 1 && (
            <div className="flex items-center justify-between px-4 py-3 border-t border-border/50">
              <p className="text-xs text-muted-foreground">
                Toplam {data?.totalCount ?? 0} kayıt · Sayfa {page} / {data?.totalPages}
              </p>
              <div className="flex items-center gap-2">
                <Button variant="outline" size="sm" onClick={() => setPage(p => p - 1)} disabled={!data?.hasPreviousPage}>
                  <ChevronLeft className="h-4 w-4" />
                </Button>
                <Button variant="outline" size="sm" onClick={() => setPage(p => p + 1)} disabled={!data?.hasNextPage}>
                  <ChevronRight className="h-4 w-4" />
                </Button>
              </div>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
