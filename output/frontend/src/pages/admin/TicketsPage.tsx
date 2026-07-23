import { useEffect, useState } from 'react';
import {
  RefreshCw, Plus, MessageSquareText, Inbox, AlertTriangle, Check, X, RotateCcw,
  ExternalLink, Sparkles, Lightbulb, Building2, User, Clock, ChevronLeft, ChevronRight, Loader2,
} from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter,
} from '@/components/ui/dialog';
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from '@/components/ui/select';
import { cn } from '@/lib/utils';
import { useToast } from '@/hooks/use-toast';
import {
  useTickets, useTicket, useCreateTicket, useUpdateTicketStatus,
  type Ticket, type TicketType, type TicketPlatform,
} from '@/api/tickets';

// ── Config ──────────────────────────────────────────────────────────────────

const STATUS_CONFIG: Record<string, { label: string; className: string }> = {
  New:        { label: 'Yeni',        className: 'bg-yellow-500/15 text-yellow-400 border-yellow-500/30' },
  Triaged:    { label: 'AI İnceledi', className: 'bg-violet-500/15 text-violet-300 border-violet-500/30' },
  Approved:   { label: 'Onaylandı',   className: 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30' },
  Rejected:   { label: 'Reddedildi',  className: 'bg-red-500/15 text-red-400 border-red-500/30' },
  InProgress: { label: 'Uygulanıyor', className: 'bg-purple-500/15 text-purple-300 border-purple-500/30' },
  Done:       { label: 'Tamamlandı',  className: 'bg-sky-500/15 text-sky-400 border-sky-500/30' },
  Failed:     { label: 'Başarısız',   className: 'bg-rose-500/15 text-rose-400 border-rose-500/30' },
};

const TYPE_CONFIG: Record<string, { label: string; className: string }> = {
  Feedback:   { label: 'Geri Bildirim', className: 'bg-blue-500/15 text-blue-300 border-blue-500/30' },
  Suggestion: { label: 'Öneri',         className: 'bg-teal-500/15 text-teal-300 border-teal-500/30' },
};

const PLATFORM_LABEL: Record<string, string> = {
  Web: 'Web (Yönetim)',
  MobileStaff: 'Mobil (Saha)',
  CustomerPortal: 'Müşteri Portalı',
  CustomerMobile: 'Müşteri Mobil',
};

const SOURCE_LABEL: Record<string, string> = { Tenant: 'Tenant', Crm: 'Destek' };

/** Status chips shown as the primary filter row. Empty value = all statuses. */
const STATUS_FILTERS: { value: string; label: string }[] = [
  { value: '', label: 'Tümü' },
  { value: 'New', label: 'Yeni' },
  { value: 'Triaged', label: 'AI İnceledi' },
  { value: 'Approved', label: 'Onaylandı' },
  { value: 'InProgress', label: 'Uygulanıyor' },
  { value: 'Done', label: 'Tamamlandı' },
  { value: 'Rejected', label: 'Reddedildi' },
  { value: 'Failed', label: 'Başarısız' },
];

const TYPE_OPTIONS: { value: TicketType; label: string }[] = [
  { value: 'Feedback', label: 'Geri Bildirim' },
  { value: 'Suggestion', label: 'Öneri' },
];

const PLATFORM_OPTIONS: { value: TicketPlatform; label: string }[] = [
  { value: 'Web', label: 'Web (Yönetim)' },
  { value: 'MobileStaff', label: 'Mobil (Saha)' },
  { value: 'CustomerPortal', label: 'Müşteri Portalı' },
  { value: 'CustomerMobile', label: 'Müşteri Mobil' },
];

// ── Helpers ─────────────────────────────────────────────────────────────────

function Pill({ className, children }: { className: string; children: React.ReactNode }) {
  return (
    <span className={`inline-flex items-center gap-1 rounded-full border px-2.5 py-0.5 text-xs font-semibold ${className}`}>
      {children}
    </span>
  );
}

function StatusBadge({ status }: { status: string }) {
  const cfg = STATUS_CONFIG[status] ?? { label: status, className: 'bg-slate-500/15 text-slate-400 border-slate-500/30' };
  return <Pill className={cfg.className}>{cfg.label}</Pill>;
}

function TypeBadge({ type }: { type: string }) {
  const cfg = TYPE_CONFIG[type] ?? { label: type, className: 'bg-slate-500/15 text-slate-400 border-slate-500/30' };
  return <Pill className={cfg.className}>{cfg.label}</Pill>;
}

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
    const data = (err.response as { data?: { message?: string; errors?: string[] } }).data;
    if (data?.message) return data.message;
    if (data?.errors?.length) return data.errors[0];
  }
  return fallback;
}

/** New/Triaged tickets can be approved or rejected; a Failed ticket can be re-approved to retry. */
function canApprove(status: string) { return status === 'New' || status === 'Triaged' || status === 'Failed'; }
function canReject(status: string) { return status === 'New' || status === 'Triaged'; }

// ── Detail dialog ─────────────────────────────────────────────────────────────

function TicketDetailDialog({ ticketId, onClose }: { ticketId: string; onClose: () => void }) {
  const { toast } = useToast();
  const { data: ticket, isLoading, isError, error } = useTicket(ticketId);
  const update = useUpdateTicketStatus();
  const [note, setNote] = useState('');
  const busy = update.isPending;

  async function decide(status: 'Approved' | 'Rejected') {
    if (!ticket) return;
    try {
      await update.mutateAsync({ id: ticket.id, status, decisionNote: note.trim() || undefined });
      toast({
        title: status === 'Approved' ? 'Talep onaylandı' : 'Talep reddedildi',
        description: ticket.subject,
      });
      onClose();
    } catch (err) {
      toast({
        title: 'İşlem başarısız',
        description: errorMessage(err, 'Liftdesk işlemi reddetti. Lütfen tekrar deneyin.'),
        variant: 'destructive',
      });
    }
  }

  const actionable = ticket ? (canApprove(ticket.status) || canReject(ticket.status)) : false;

  return (
    <Dialog open onOpenChange={(o) => !o && !busy && onClose()}>
      <DialogContent className="sm:max-w-2xl max-h-[90vh] overflow-y-auto">
        {isLoading ? (
          <div className="space-y-3 py-4">
            <Skeleton className="h-6 w-2/3" />
            <Skeleton className="h-4 w-full" />
            <Skeleton className="h-24 w-full" />
          </div>
        ) : isError || !ticket ? (
          <div className="py-8 text-center space-y-3">
            <DialogHeader>
              <DialogTitle className="text-center">Talep açılamadı</DialogTitle>
            </DialogHeader>
            <AlertTriangle className="h-8 w-8 mx-auto text-red-400" />
            <p className="text-sm text-muted-foreground">
              {errorMessage(error, 'Talep detayı yüklenemedi. Liste yenilenmiş veya kayıt kaldırılmış olabilir.')}
            </p>
            <Button variant="outline" size="sm" onClick={onClose}>Kapat</Button>
          </div>
        ) : (
          <>
            <DialogHeader>
              <div className="flex flex-wrap items-center gap-2 mb-2">
                <TypeBadge type={ticket.type} />
                <Pill className="border-border bg-muted/40 text-muted-foreground">
                  {PLATFORM_LABEL[ticket.platform] ?? ticket.platform}
                </Pill>
                <StatusBadge status={ticket.status} />
                <Pill className="border-border bg-muted/40 text-muted-foreground">
                  {SOURCE_LABEL[ticket.source] ?? ticket.source}
                </Pill>
              </div>
              <DialogTitle className="text-left">{ticket.subject}</DialogTitle>
            </DialogHeader>

            <div className="space-y-4">
              {/* Meta */}
              <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
                <span className="inline-flex items-center gap-1">
                  <Building2 className="h-3.5 w-3.5" /> {ticket.projectName ?? 'Global'}
                </span>
                <span className="inline-flex items-center gap-1">
                  <User className="h-3.5 w-3.5" /> {ticket.createdByName}
                </span>
                <span className="inline-flex items-center gap-1">
                  <Clock className="h-3.5 w-3.5" /> {formatDate(ticket.createdAt)}
                </span>
              </div>

              {/* Description */}
              <div className="space-y-1">
                <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">Açıklama</p>
                <p className="text-sm text-foreground whitespace-pre-wrap">{ticket.description}</p>
              </div>

              {/* AI triage */}
              {(ticket.agentComment || ticket.agentSuggestedAction) && (
                <div className="rounded-lg border border-violet-500/30 bg-violet-500/5 p-3 space-y-2">
                  <p className="flex items-center gap-1.5 text-xs font-semibold text-violet-300 uppercase tracking-wider">
                    <Sparkles className="h-3.5 w-3.5" /> AI Değerlendirmesi
                    {ticket.agentAnalyzedAt && (
                      <span className="ml-auto font-normal normal-case text-muted-foreground">
                        {formatDate(ticket.agentAnalyzedAt)}
                      </span>
                    )}
                  </p>
                  {ticket.agentComment && (
                    <p className="text-sm text-foreground whitespace-pre-wrap">{ticket.agentComment}</p>
                  )}
                  {ticket.agentSuggestedAction && (
                    <div className="flex items-start gap-1.5 pt-1 border-t border-violet-500/20">
                      <Lightbulb className="h-3.5 w-3.5 text-emerald-400 mt-0.5 flex-shrink-0" />
                      <p className="text-sm text-emerald-300">{ticket.agentSuggestedAction}</p>
                    </div>
                  )}
                </div>
              )}

              {/* Decision */}
              {(ticket.decidedBy || ticket.decisionNote) && (
                <div className="rounded-lg border border-border/60 bg-muted/20 p-3 space-y-1">
                  <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">Karar</p>
                  {ticket.decisionNote && <p className="text-sm text-foreground">{ticket.decisionNote}</p>}
                  <p className="text-xs text-muted-foreground">
                    {ticket.decidedBy && <>İşleyen: <span className="text-foreground">{ticket.decidedBy}</span> · </>}
                    {formatDate(ticket.decidedAt)}
                  </p>
                </div>
              )}

              {/* Fix result */}
              {(ticket.fixPrUrl || ticket.resolutionNote || ticket.completedAt) && ticket.status === 'Done' && (
                <div className="rounded-lg border border-sky-500/30 bg-sky-500/5 p-3 space-y-2">
                  <p className="text-xs font-semibold text-sky-300 uppercase tracking-wider">Sonuç</p>
                  {ticket.resolutionNote && <p className="text-sm text-foreground">{ticket.resolutionNote}</p>}
                  {ticket.fixPrUrl && (
                    <a
                      href={ticket.fixPrUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="inline-flex items-center gap-1 text-sm text-sky-400 hover:text-sky-300 font-medium"
                    >
                      <ExternalLink className="h-3.5 w-3.5" /> PR'ı İncele (merge insanda)
                    </a>
                  )}
                </div>
              )}

              {/* Fix failure */}
              {ticket.status === 'Failed' && ticket.failReason && (
                <div className="rounded-lg border border-rose-500/30 bg-rose-500/5 p-3">
                  <p className="text-xs font-semibold text-rose-300 uppercase tracking-wider mb-1">Uygulama Başarısız</p>
                  <p className="text-sm text-rose-200 whitespace-pre-wrap">{ticket.failReason}</p>
                </div>
              )}

              {/* Decision input + actions */}
              {actionable && (
                <div className="space-y-2 pt-2 border-t border-border/40">
                  <p className="text-xs text-muted-foreground">
                    Karar notu (opsiyonel) — tenant'a resmi yanıt olarak gösterilir, kullanıcıya hitaben yazın.
                  </p>
                  <Textarea
                    value={note}
                    onChange={(e) => setNote(e.target.value)}
                    placeholder="Örn: Talebiniz alındı, bir sonraki sürümde eklenecek."
                    rows={2}
                  />
                </div>
              )}
            </div>

            <DialogFooter className="gap-2">
              <Button variant="outline" size="sm" onClick={onClose} disabled={busy}>Kapat</Button>
              {ticket && canReject(ticket.status) && (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => decide('Rejected')}
                  disabled={busy}
                  className="border-red-500/30 text-red-400 hover:bg-red-500/10 hover:text-red-300"
                >
                  <X className="h-4 w-4 mr-1.5" /> Reddet
                </Button>
              )}
              {ticket && canApprove(ticket.status) && (
                <Button
                  size="sm"
                  onClick={() => decide('Approved')}
                  disabled={busy}
                  className="bg-emerald-600 hover:bg-emerald-700 text-white"
                >
                  {busy ? <Loader2 className="h-4 w-4 mr-1.5 animate-spin" />
                    : ticket.status === 'Failed' ? <RotateCcw className="h-4 w-4 mr-1.5" />
                    : <Check className="h-4 w-4 mr-1.5" />}
                  {ticket.status === 'Failed' ? 'Yeniden Onayla' : 'Onayla'}
                </Button>
              )}
            </DialogFooter>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}

// ── Create dialog ─────────────────────────────────────────────────────────────

function CreateTicketDialog({ onClose }: { onClose: () => void }) {
  const { toast } = useToast();
  const create = useCreateTicket();

  const [type, setType] = useState<TicketType>('Suggestion');
  const [platform, setPlatform] = useState<TicketPlatform>('Web');
  const [subject, setSubject] = useState('');
  const [description, setDescription] = useState('');
  const [createdByName, setCreatedByName] = useState('');

  const busy = create.isPending;
  const valid = subject.trim().length > 0 && description.trim().length > 0;

  async function submit() {
    if (!valid) {
      toast({ title: 'Eksik alan', description: 'Konu ve açıklama zorunludur.', variant: 'destructive' });
      return;
    }
    try {
      // CRM support tickets are global (projectId=null): the CRM has no Liftdesk tenant-project
      // GUIDs to bind to. Name the tenant in the subject/description instead.
      await create.mutateAsync({
        type,
        platform,
        subject: subject.trim(),
        description: description.trim(),
        projectId: null,
        createdByName: createdByName.trim() || undefined,
      });
      toast({ title: 'Talep oluşturuldu', description: subject.trim() });
      onClose();
    } catch (err) {
      toast({
        title: 'Oluşturulamadı',
        description: errorMessage(err, 'Talep oluşturulamadı. Lütfen tekrar deneyin.'),
        variant: 'destructive',
      });
    }
  }

  return (
    <Dialog open onOpenChange={(o) => !o && !busy && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Yeni Talep Aç</DialogTitle>
          <DialogDescription>
            Telefon/e-posta ile gelen geri bildirim veya öneriyi kaydedin. AI ajanı analiz edecek.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-3">
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1">
              <label className="text-xs font-medium text-muted-foreground">Tür</label>
              <Select value={type} onValueChange={(v) => setType(v as TicketType)}>
                <SelectTrigger className="h-9"><SelectValue /></SelectTrigger>
                <SelectContent>
                  {TYPE_OPTIONS.map((o) => <SelectItem key={o.value} value={o.value}>{o.label}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1">
              <label className="text-xs font-medium text-muted-foreground">Platform</label>
              <Select value={platform} onValueChange={(v) => setPlatform(v as TicketPlatform)}>
                <SelectTrigger className="h-9"><SelectValue /></SelectTrigger>
                <SelectContent>
                  {PLATFORM_OPTIONS.map((o) => <SelectItem key={o.value} value={o.value}>{o.label}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
          </div>

          <div className="space-y-1">
            <label className="text-xs font-medium text-muted-foreground">Konu</label>
            <Input value={subject} onChange={(e) => setSubject(e.target.value)} placeholder="Kısa başlık" />
          </div>

          <div className="space-y-1">
            <label className="text-xs font-medium text-muted-foreground">Açıklama</label>
            <Textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Talebin detayı — müşteri ne istiyor?"
              rows={4}
            />
          </div>

          <div className="space-y-1">
            <label className="text-xs font-medium text-muted-foreground">Açan (opsiyonel)</label>
            <Input
              value={createdByName}
              onChange={(e) => setCreatedByName(e.target.value)}
              placeholder="Örn: Destek: Ayşe"
            />
          </div>
        </div>

        <DialogFooter className="gap-2">
          <Button variant="outline" size="sm" onClick={onClose} disabled={busy}>Vazgeç</Button>
          <Button size="sm" onClick={submit} disabled={busy || !valid}>
            {busy ? <Loader2 className="h-4 w-4 mr-1.5 animate-spin" /> : <Plus className="h-4 w-4 mr-1.5" />}
            {busy ? 'Oluşturuluyor...' : 'Oluştur'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── List card ─────────────────────────────────────────────────────────────────

function TicketCard({ ticket, onOpen }: { ticket: Ticket; onOpen: (id: string) => void }) {
  return (
    <button
      onClick={() => onOpen(ticket.id)}
      className="w-full text-left"
    >
      <Card className="border-border/50 transition-colors hover:border-primary/40 hover:bg-accent/30">
        <CardContent className="p-4 space-y-2">
          <div className="flex flex-wrap items-center gap-2">
            <TypeBadge type={ticket.type} />
            <StatusBadge status={ticket.status} />
            {ticket.agentComment && (
              <Pill className="border-violet-500/30 bg-violet-500/10 text-violet-300">
                <Sparkles className="h-3 w-3" /> AI
              </Pill>
            )}
            <span className="ml-auto text-xs text-muted-foreground whitespace-nowrap">
              {formatDate(ticket.createdAt)}
            </span>
          </div>
          <p className="font-semibold text-sm text-foreground">{ticket.subject}</p>
          <p className="text-sm text-muted-foreground line-clamp-2">{ticket.description}</p>
          <div className="flex flex-wrap gap-x-3 gap-y-1 text-xs text-muted-foreground pt-1">
            <span className="inline-flex items-center gap-1">
              <Building2 className="h-3 w-3" /> {ticket.projectName ?? 'Global'}
            </span>
            <span className="inline-flex items-center gap-1">
              <User className="h-3 w-3" /> {ticket.createdByName}
            </span>
            <span>{PLATFORM_LABEL[ticket.platform] ?? ticket.platform}</span>
          </div>
        </CardContent>
      </Card>
    </button>
  );
}

// ── Page ────────────────────────────────────────────────────────────────────

export function TicketsPage() {
  const [status, setStatus] = useState('');
  const [type, setType] = useState('all');
  const [platform, setPlatform] = useState('all');
  const [page, setPage] = useState(1);
  const [openId, setOpenId] = useState<string | null>(null);
  const [showCreate, setShowCreate] = useState(false);

  const { data, isLoading, isFetching, refetch, isError, error } = useTickets({
    status: status || undefined,
    type: type === 'all' ? undefined : type,
    platform: platform === 'all' ? undefined : platform,
    page,
    pageSize: 20,
  });

  const tickets = data?.items ?? [];

  // Clamp the page down when the result set shrinks (e.g. after approving the last item on a
  // trailing page, or a background poll removing rows) so the operator is never stranded on an
  // empty page with the pagination controls hidden.
  useEffect(() => {
    if (data && data.totalPages > 0 && page > data.totalPages) {
      setPage(data.totalPages);
    }
  }, [data, page]);

  function resetToFirstPage<T>(setter: (v: T) => void) {
    return (v: T) => { setter(v); setPage(1); };
  }

  return (
    <div className="p-6 space-y-6 max-w-4xl mx-auto">
      {/* Header */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold text-foreground flex items-center gap-2">
            <MessageSquareText className="h-6 w-6" /> Talep & Öneriler
          </h1>
          <p className="text-sm text-muted-foreground mt-1">
            Liftdesk geri bildirim/öneri hattı · AI değerlendirir · onayla/reddet · PR insanda merge edilir
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="outline" size="sm" onClick={() => refetch()} disabled={isFetching}>
            <RefreshCw className={`h-4 w-4 mr-1.5 ${isFetching ? 'animate-spin' : ''}`} /> Yenile
          </Button>
          <Button size="sm" onClick={() => setShowCreate(true)}>
            <Plus className="h-4 w-4 mr-1.5" /> Yeni Talep
          </Button>
        </div>
      </div>

      {/* Status chips */}
      <div className="flex flex-wrap gap-1 rounded-lg border border-border/50 bg-muted/30 p-1">
        {STATUS_FILTERS.map((s) => {
          const active = status === s.value;
          return (
            <button
              key={s.value || 'all'}
              onClick={() => { setStatus(s.value); setPage(1); }}
              className={cn(
                'rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                active
                  ? 'bg-primary text-primary-foreground shadow-sm'
                  : 'text-muted-foreground hover:text-foreground hover:bg-accent'
              )}
            >
              {s.label}
            </button>
          );
        })}
      </div>

      {/* Secondary filters */}
      <div className="flex flex-wrap gap-2">
        <Select value={type} onValueChange={resetToFirstPage(setType)}>
          <SelectTrigger className="h-9 w-full sm:w-44"><SelectValue placeholder="Tür" /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Tüm Türler</SelectItem>
            {TYPE_OPTIONS.map((o) => <SelectItem key={o.value} value={o.value}>{o.label}</SelectItem>)}
          </SelectContent>
        </Select>
        <Select value={platform} onValueChange={resetToFirstPage(setPlatform)}>
          <SelectTrigger className="h-9 w-full sm:w-48"><SelectValue placeholder="Platform" /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Tüm Platformlar</SelectItem>
            {PLATFORM_OPTIONS.map((o) => <SelectItem key={o.value} value={o.value}>{o.label}</SelectItem>)}
          </SelectContent>
        </Select>
      </div>

      {/* Content */}
      {isLoading ? (
        <div className="space-y-4">
          {Array.from({ length: 3 }).map((_, i) => (
            <Card key={i} className="border-border/50">
              <CardContent className="p-4 space-y-3">
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
            <p className="font-medium">Talepler yüklenemedi.</p>
            <p className="text-sm text-muted-foreground mt-1">
              {errorMessage(error, 'Liftdesk ticket servisine bağlanılamadı.')}
            </p>
          </CardContent>
        </Card>
      ) : tickets.length === 0 ? (
        <Card className="border-border/50">
          <CardContent className="p-12 text-center text-muted-foreground">
            <Inbox className="h-10 w-10 mx-auto mb-3 opacity-40" />
            <p className="font-medium text-foreground">Kayıt yok</p>
            <p className="text-sm mt-1">Seçilen filtrelere uyan talep bulunamadı.</p>
          </CardContent>
        </Card>
      ) : (
        <>
          <div className="space-y-3">
            {tickets.map((t) => <TicketCard key={t.id} ticket={t} onOpen={setOpenId} />)}
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

      {openId && <TicketDetailDialog ticketId={openId} onClose={() => setOpenId(null)} />}
      {showCreate && <CreateTicketDialog onClose={() => setShowCreate(false)} />}
    </div>
  );
}
