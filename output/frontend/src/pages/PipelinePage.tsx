import { useNavigate } from 'react-router-dom';
import { ChevronLeft, ChevronRight, TrendingUp, User, Calendar, Loader2 } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useAllOpportunities, useUpdateOpportunityStage } from '@/api/customers';
import { useToast } from '@/hooks/use-toast';
import type { Opportunity, OpportunityStage } from '@/types';

// ── Stage config ───────────────────────────────────────────────────────────────

const STAGES: { value: OpportunityStage; label: string; color: string }[] = [
  { value: 'Prospecting',   label: 'Potansiyel',   color: 'bg-slate-500' },
  { value: 'Qualification', label: 'Nitelendirme', color: 'bg-blue-500' },
  { value: 'Proposal',      label: 'Teklif',       color: 'bg-violet-500' },
  { value: 'Negotiation',   label: 'Müzakere',     color: 'bg-amber-500' },
  { value: 'ClosedWon',     label: 'Kazanıldı',    color: 'bg-emerald-500' },
  { value: 'ClosedLost',    label: 'Kaybedildi',   color: 'bg-rose-500' },
];

const STAGE_ORDER = STAGES.map((s) => s.value);

function formatCurrency(value: number | null) {
  if (value == null) return null;
  return new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY', maximumFractionDigits: 0 }).format(value);
}

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

      {opportunity.value != null && (
        <p className="text-sm font-bold text-foreground">{formatCurrency(opportunity.value)}</p>
      )}

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

  const opportunities = data?.items ?? [];

  const handleMoveStage = async (id: string, stage: OpportunityStage) => {
    try {
      await stageMutation.mutateAsync({ id, stage });
    } catch {
      toast({ title: 'Hata', description: 'Aşama güncellenemedi.', variant: 'destructive' });
    }
  };

  // Group by stage
  const byStage = STAGES.map((s) => ({
    ...s,
    items: opportunities.filter((o) => o.stage === s.value),
    total: opportunities.filter((o) => o.stage === s.value).reduce((sum, o) => sum + (o.value ?? 0), 0),
  }));

  const totalPipelineValue = opportunities
    .filter(o => o.stage !== 'ClosedLost')
    .reduce((sum, o) => sum + (o.value ?? 0), 0);

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-foreground">Pipeline</h1>
          <p className="text-muted-foreground text-sm mt-1">
            {isLoading ? 'Yükleniyor...' : `${opportunities.length} fırsat · Toplam: ${formatCurrency(totalPipelineValue)}`}
          </p>
        </div>
        <TrendingUp className="h-6 w-6 text-muted-foreground" />
      </div>

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
                      </span>
                    </div>
                  </div>
                  {stage.total > 0 && (
                    <p className="text-xs text-muted-foreground font-medium mb-2">
                      {formatCurrency(stage.total)}
                    </p>
                  )}

                  {/* Cards */}
                  {stage.items.length === 0 ? (
                    <div className="flex items-center justify-center py-8">
                      <p className="text-xs text-muted-foreground/50">Fırsat yok</p>
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
