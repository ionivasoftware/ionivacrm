import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Phone, Mail, Users, StickyNote, MessageCircle, MapPin,
  Search, Filter, ChevronLeft, ChevronRight, ArrowLeft, X,
} from 'lucide-react';
import { Card, CardContent, CardHeader } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from '@/components/ui/select';
import { useAllContactHistories } from '@/api/customers';
import type { ContactType } from '@/types';

// ── Constants ─────────────────────────────────────────────────────────────────

const TYPE_LABELS: Record<ContactType, string> = {
  Call: 'Arama',
  Email: 'E-posta',
  Meeting: 'Toplantı',
  Note: 'Not',
  WhatsApp: 'WhatsApp',
  Visit: 'Ziyaret',
};

const TYPE_ICONS: Record<ContactType, React.ComponentType<{ className?: string }>> = {
  Call: Phone,
  Email: Mail,
  Meeting: Users,
  Note: StickyNote,
  WhatsApp: MessageCircle,
  Visit: MapPin,
};

const TYPE_COLORS: Record<ContactType, string> = {
  Call: 'text-blue-400 bg-blue-400/10',
  Email: 'text-violet-400 bg-violet-400/10',
  Meeting: 'text-green-400 bg-green-400/10',
  Note: 'text-amber-400 bg-amber-400/10',
  WhatsApp: 'text-emerald-400 bg-emerald-400/10',
  Visit: 'text-rose-400 bg-rose-400/10',
};

function formatDate(dateStr: string): string {
  return new Intl.DateTimeFormat('tr-TR', {
    day: 'numeric', month: 'short', year: 'numeric',
    hour: '2-digit', minute: '2-digit',
  }).format(new Date(dateStr));
}

const PAGE_SIZE = 20;

// ── Component ─────────────────────────────────────────────────────────────────

export function ContactHistoriesPage() {
  const navigate = useNavigate();
  const [search, setSearch] = useState('');
  const [typeFilter, setTypeFilter] = useState<ContactType | 'all'>('all');
  const [showFilters, setShowFilters] = useState(false);
  const [page, setPage] = useState(1);

  const { data, isLoading } = useAllContactHistories({
    type: typeFilter !== 'all' ? typeFilter : undefined,
    page,
    pageSize: PAGE_SIZE,
  });

  const items = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const totalPages = Math.ceil(totalCount / PAGE_SIZE);

  // Client-side search filter on customerName / subject / createdByUserName
  const filtered = search.trim()
    ? items.filter((h) => {
        const q = search.toLowerCase();
        return (
          h.customerName?.toLowerCase().includes(q) ||
          h.subject?.toLowerCase().includes(q) ||
          h.createdByUserName?.toLowerCase().includes(q)
        );
      })
    : items;

  function handleTypeChange(value: string) {
    setTypeFilter(value as ContactType | 'all');
    setPage(1);
  }

  const hasFilters = typeFilter !== 'all';

  return (
    <div className="space-y-6">
      {/* ── Header ── */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <Button
            variant="ghost"
            size="icon"
            onClick={() => navigate('/customers')}
            className="h-9 w-9"
          >
            <ArrowLeft className="h-4 w-4" />
          </Button>
          <div>
            <h1 className="text-2xl font-bold text-foreground">Tüm Görüşmeler</h1>
            <p className="text-muted-foreground text-sm mt-0.5">
              {isLoading ? 'Yükleniyor...' : `${totalCount.toLocaleString('tr-TR')} görüşme kaydı`}
            </p>
          </div>
        </div>
        <Button
          variant="outline"
          size="sm"
          className="gap-2 h-10"
          onClick={() => setShowFilters((v) => !v)}
        >
          <Filter className="h-4 w-4" />
          Filtrele
          {hasFilters && (
            <Badge variant="secondary" className="ml-1 px-1.5 py-0 text-xs">1</Badge>
          )}
        </Button>
      </div>

      {/* ── Search + Filter Card ── */}
      <Card>
        <CardHeader className="pb-4">
          <div className="flex flex-col sm:flex-row gap-3">
            <div className="relative flex-1">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
              <Input
                placeholder="Müşteri adı, konu veya temsilci ara..."
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                className="pl-9 h-10"
              />
            </div>
          </div>

          {showFilters && (
            <div className="flex flex-wrap gap-3 pt-3 border-t border-border mt-3">
              <Select value={typeFilter} onValueChange={handleTypeChange}>
                <SelectTrigger className="h-9 w-44">
                  <SelectValue placeholder="Tür" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">Tüm Türler</SelectItem>
                  {(Object.keys(TYPE_LABELS) as ContactType[]).map((t) => (
                    <SelectItem key={t} value={t}>{TYPE_LABELS[t]}</SelectItem>
                  ))}
                </SelectContent>
              </Select>

              {hasFilters && (
                <Button
                  variant="ghost"
                  size="sm"
                  className="h-9 gap-1.5 text-muted-foreground"
                  onClick={() => { setTypeFilter('all'); setPage(1); }}
                >
                  <X className="h-3.5 w-3.5" />
                  Temizle
                </Button>
              )}
            </div>
          )}
        </CardHeader>
      </Card>

      {/* ── List ── */}
      <Card>
        <CardContent className="p-0">
          {isLoading ? (
            <div className="divide-y divide-border">
              {Array.from({ length: 8 }).map((_, i) => (
                <div key={i} className="flex items-start gap-4 p-4">
                  <Skeleton className="h-9 w-9 rounded-full flex-shrink-0" />
                  <div className="flex-1 space-y-2">
                    <Skeleton className="h-4 w-40" />
                    <Skeleton className="h-3 w-64" />
                    <Skeleton className="h-3 w-24" />
                  </div>
                </div>
              ))}
            </div>
          ) : filtered.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-16 text-center">
              <MessageCircle className="h-10 w-10 text-muted-foreground/30 mb-3" />
              <p className="text-muted-foreground text-sm">Görüşme kaydı bulunamadı.</p>
            </div>
          ) : (
            <div className="divide-y divide-border">
              {filtered.map((h) => {
                const Icon = TYPE_ICONS[h.type] ?? StickyNote;
                const colorClass = TYPE_COLORS[h.type] ?? 'text-muted-foreground bg-muted';
                return (
                  <div
                    key={h.id}
                    className="flex items-start gap-4 p-4 hover:bg-muted/30 transition-colors cursor-pointer"
                    onClick={() => navigate(`/customers/${h.customerId}`)}
                  >
                    <div className={`w-9 h-9 rounded-full flex items-center justify-center flex-shrink-0 mt-0.5 ${colorClass}`}>
                      <Icon className="h-4 w-4" />
                    </div>
                    <div className="flex-1 min-w-0">
                      <div className="flex items-start justify-between gap-2 flex-wrap">
                        <p className="text-sm font-semibold text-foreground">
                          {h.customerName ?? '—'}
                        </p>
                        <div className="flex items-center gap-2 flex-shrink-0">
                          <Badge variant="secondary" className="text-xs">
                            {TYPE_LABELS[h.type]}
                          </Badge>
                          <span className="text-xs text-muted-foreground">
                            {formatDate(h.contactedAt)}
                          </span>
                        </div>
                      </div>
                      {h.subject && (
                        <p className="text-sm text-foreground mt-0.5 truncate">{h.subject}</p>
                      )}
                      {h.content && (
                        <p className="text-xs text-muted-foreground mt-0.5 line-clamp-2">{h.content}</p>
                      )}
                      {h.createdByUserName && (
                        <p className="text-xs text-muted-foreground mt-1">
                          {h.createdByUserName}
                        </p>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </CardContent>
      </Card>

      {/* ── Pagination ── */}
      {totalPages > 1 && (
        <div className="flex items-center justify-between text-sm text-muted-foreground">
          <span>
            {(page - 1) * PAGE_SIZE + 1}–{Math.min(page * PAGE_SIZE, totalCount)} / {totalCount.toLocaleString('tr-TR')}
          </span>
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="icon"
              className="h-8 w-8"
              disabled={page <= 1}
              onClick={() => setPage((p) => p - 1)}
            >
              <ChevronLeft className="h-4 w-4" />
            </Button>
            <span className="px-2">{page} / {totalPages}</span>
            <Button
              variant="outline"
              size="icon"
              className="h-8 w-8"
              disabled={page >= totalPages}
              onClick={() => setPage((p) => p + 1)}
            >
              <ChevronRight className="h-4 w-4" />
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
