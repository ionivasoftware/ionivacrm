import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ChevronLeft, ChevronRight, TrendingUp, User, Calendar, Loader2, Filter, X } from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import { useAllOpportunities, useUpdateOpportunityStage } from '@/api/customers';
import { useToast } from '@/hooks/use-toast';
import type { Opportunity, OpportunityStage } from '@/types';

// ── Stage config ───────────────────────────────────────────────────────────────

const STAGES: { value: OpportunityStage; label: string; color: string }[] = [
  { value: 'YeniArama',  label: 'Yeni Arama',  color: 'bg-slate-500' },
  { value: 'Potansiyel', label: 'Potansiyel',  color: 'bg-blue-500' },
  { value: 'Demo',       label: 'Demo',         color: 'bg-violet-500' },
  { value: 'Musteri',    label: 'Müşteri',      color: 'bg-emerald-500' },
  { value: 'Kayip',      label: 'Kayıp',        color: 'bg-rose-500' },
];

const STAGE_ORDER = STAGES.map((s) => s.value);

// Müşteri ve Kayıp sütunları tarih filtresi uygular
const FILTERED_STAGES: OpportunityStage[] = ['Musteri', 'Kayip'];

function formatDate(dateStr: string | null) {
  if (!dateStr) return null;
  return new Date(dateStr).toLocaleDateString('tr-TR', { day: '2-digit', month: 'short' });
}

// ── Opportunity Card ───────────────────────────────────────────────────────────

function OpportunityCard({
  opportunity,
  onMoveStage,
  isMoving,
}: {
  opportunity: Opportunity;
  onMoveStage: (id: string, stage: OpportunityStage) => void;
  isMoving: boolean;
}) {
  const navigate = useNavigate();
  const currentIndex = STAGE_ORDER.indexOf(opportunity.stage);
  const prevStage = currentIndex > 0 ? STAGE_ORDER[currentIndex - 1] : null;
  const nextStage = currentIndex < STAGE_ORDER.length - 1 ? STAGE_ORDER[currentIndex + 1] : null;

  return (
    <div className="bg-card border border-border rounded-lg p-3 space-y-2 shadow-sm hover:shadow-md transition-shadow">
      <p
        className="text-sm font-semibold text-foreground leading-snug cursor-pointer hover:text-primary transition-colors line-clamp-2"
        onClick={() => navigate(`/customers/${opportunity.customerId}`)}
      >
        {opportunity.title}
      </p>

      <p
        className="text-xs text-muted-foreground cursor-pointer hover:text-foreground transition-colors truncate"
        onClick={() => navigate(`/customers/${opportunity.customerId}`)}
      >
        {opportunity.customerName}
      </p>

      <div className="flex flex-wrap gap-1.5">
        {opportunity.probability != null && (
          <Badge variant="outline" className="text-xs px-1.5 py-0">
            %{opportunity.probability}
          </Badge>
        )}
        {opportunity.expectedCloseDate && (
          <Badge variant="outline" className="text-xs px-1.5 py-0 gap-1">
            <Calendar className="h-2.5 w-2.5" />
            {formatDate(opportunity.expectedCloseDate)}
          </Badge>
        )}
      </div>

      {opportunity.assignedUserName && (
        <p className="text-xs text-muted-foreground flex items-center gap-1">
          <User className="h-3 w-3" /> {opportunity.assignedUserName}
        </p>
      )}

      <div className="flex gap-1 pt-1">
        {prevStage && (
          <Button
            variant="ghost"
            size="sm"
            className="h-6 px-2 text-xs flex-1"
            disabled={isMoving}
            onClick={() => onMoveStage(opportunity.id, prevStage)}
          >
            {isMoving ? <Loader2 className="h-3 w-3 animate-spin" /> : <ChevronLeft className="h-3 w-3" />}
            {STAGES.find(s => s.value === prevStage)?.label}
          </Button>
        )}
        {nextStage && (
          <Button
            variant="ghost"
            size="sm"
            className="h-6 px-2 text-xs flex-1"
            disabled={isMoving}
            onClick={() => onMoveStage(opportunity.id, nextStage)}
          >
            {STAGES.find(s => s.value === nextStage)?.label}
            {isMoving ? <Loader2 className="h-3 w-3 animate-spin" /> : <ChevronRight className="h-3 w-3" />}
          </Button>
        )}
      </div>
    </div>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function PipelinePage() {
  const { data, isLoading } = useAllOpportunities();
  const stageMutation = useUpdateOpportunityStage();
  const { toast } = useToast();

  const [showFilter, setShowFilter] = useState(false);
  const [filterFrom, setFilterFrom] = useState('');
  const [filterTo, setFilterTo] = useState('');

  const opportunities = data?.items ?? [];

  const handleMoveStage = async (id: string, stage: OpportunityStage) => {
    try {
      await stageMutation.mutateAsync({ id, stage });
    } catch {
      toast({ title: 'Hata', description: 'Aşama güncellenemedi.', variant: 'destructive' });
    }
  };

  function applyDateFilter(items: Opportunity[], stage: OpportunityStage) {
    if (!FILTERED_STAGES.includes(stage)) return items;
    // Default: son 30 gün — kullanıcı özel tarih seçmediyse
    const from = filterFrom ? new Date(filterFrom) : new Date(Date.now() - 30 * 24 * 60 * 60 * 1000);
    const to = filterTo ? new Date(filterTo + 'T23:59:59') : new Date();
    return items.filter((o) => {
      const d = new Date(o.updatedAt);
      return d >= from && d <= to;
    });
  }

  const byStage = STAGES.map((s) => {
    const all = opportunities.filter((o) => o.stage === s.value);
    const filtered = applyDateFilter(all, s.value);
    return { ...s, items: filtered, totalAll: all.length };
  });

  const activeCount = opportunities.filter(
    o => !FILTERED_STAGES.includes(o.stage)
  ).length;

  const hasCustomFilter = filterFrom || filterTo;

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-foreground">Pipeline</h1>
          <p className="text-muted-foreground text-sm mt-1">
            {isLoading ? 'Yükleniyor...' : `${activeCount} aktif fırsat`}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button
            variant={showFilter ? 'secondary' : 'outline'}
            size="sm"
            className="gap-2"
            onClick={() => setShowFilter(v => !v)}
          >
            <Filter className="h-4 w-4" />
            Tarih Filtresi
            {hasCustomFilter && <span className="w-2 h-2 bg-primary rounded-full" />}
          </Button>
          <TrendingUp className="h-6 w-6 text-muted-foreground" />
        </div>
      </div>

      {/* Date filter (Müşteri + Kayıp sütunları için) */}
      {showFilter && (
        <Card>
          <CardContent className="pt-4 pb-3">
            <p className="text-xs text-muted-foreground mb-3">
              Müşteri ve Kayıp sütunları için tarih filtresi (varsayılan: son 30 gün)
            </p>
            <div className="flex items-end gap-3 flex-wrap">
              <div className="space-y-1.5">
                <Label className="text-xs">Başlangıç</Label>
                <Input
                  type="date"
                  value={filterFrom}
                  onChange={e => setFilterFrom(e.target.value)}
                  className="h-9 w-40 text-sm"
                />
              </div>
              <div className="space-y-1.5">
                <Label className="text-xs">Bitiş</Label>
                <Input
                  type="date"
                  value={filterTo}
                  onChange={e => setFilterTo(e.target.value)}
                  className="h-9 w-40 text-sm"
                />
              </div>
              {hasCustomFilter && (
                <Button
                  variant="ghost"
                  size="sm"
                  className="gap-1.5 text-muted-foreground"
                  onClick={() => { setFilterFrom(''); setFilterTo(''); }}
                >
                  <X className="h-4 w-4" /> Varsayılana dön
                </Button>
              )}
            </div>
          </CardContent>
        </Card>
      )}

      {/* Kanban Board */}
      <div className="flex gap-3 overflow-x-auto pb-4" style={{ minHeight: '60vh' }}>
        {isLoading
          ? STAGES.map((s) => (
              <div key={s.value} className="flex-shrink-0 w-64">
                <div className="rounded-lg bg-muted/40 p-3 space-y-3">
                  <Skeleton className="h-5 w-24" />
                  {[1, 2].map((i) => <Skeleton key={i} className="h-28 w-full rounded-lg" />)}
                </div>
              </div>
            ))
          : byStage.map((stage) => (
              <div key={stage.value} className="flex-shrink-0 w-64">
                <div className="rounded-lg bg-muted/30 p-3 space-y-2 h-full">
                  {/* Column header */}
                  <div className="flex items-center justify-between mb-3">
                    <div className="flex items-center gap-2">
                      <div className={`w-2.5 h-2.5 rounded-full ${stage.color}`} />
                      <span className="text-sm font-semibold text-foreground">{stage.label}</span>
                      <span className="text-xs text-muted-foreground bg-muted rounded-full px-1.5 py-0.5">
                        {stage.items.length}
                        {FILTERED_STAGES.includes(stage.value) && stage.totalAll !== stage.items.length
                          ? `/${stage.totalAll}` : ''}
                      </span>
                    </div>
                    {FILTERED_STAGES.includes(stage.value) && (
                      <span className="text-xs text-muted-foreground opacity-60">
                        {hasCustomFilter ? 'filtrelendi' : '30g'}
                      </span>
                    )}
                  </div>

                  {/* Cards */}
                  {stage.items.length === 0 ? (
                    <div className="flex items-center justify-center py-8">
                      <p className="text-xs text-muted-foreground/50">Kayıt yok</p>
                    </div>
                  ) : (
                    <div className="space-y-2">
                      {stage.items.map((opp) => (
                        <OpportunityCard
                          key={opp.id}
                          opportunity={opp}
                          onMoveStage={handleMoveStage}
                          isMoving={stageMutation.isPending && stageMutation.variables?.id === opp.id}
                        />
                      ))}
                    </div>
                  )}
                </div>
              </div>
            ))}
      </div>
    </div>
  );
}
