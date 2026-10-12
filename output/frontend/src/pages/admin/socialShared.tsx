import { NavLink } from 'react-router-dom';
import { Facebook, Instagram, Twitter, Check, X, Clock, Loader2, AlertTriangle, SkipForward, Megaphone, Send, UserRound } from 'lucide-react';
import { cn } from '@/lib/utils';
import { usePublishJobs, type SocialPublishJob } from '@/api/socialPublisher';

// Ortak parçalar: Sosyal Medya sekme çubuğu, platform/durum rozetleri, gönderi başına yayın durumu.

export const PROVIDER_LABEL: Record<string, string> = { Facebook: 'Facebook', Instagram: 'Instagram', X: 'X' };

export function ProviderIcon({ provider, className }: { provider: string; className?: string }) {
  const cls = cn('h-4 w-4', className);
  if (provider === 'Facebook') return <Facebook className={cls} />;
  if (provider === 'Instagram') return <Instagram className={cls} />;
  if (provider === 'X') return <Twitter className={cls} />;
  return <Megaphone className={cls} />;
}

export const JOB_STATUS: Record<string, { label: string; className: string; icon: React.ComponentType<{ className?: string }> }> = {
  Pending:    { label: 'Bekliyor',     className: 'bg-slate-500/15 text-slate-300 border-slate-500/30', icon: Clock },
  InProgress: { label: 'Sürüyor',      className: 'bg-sky-500/15 text-sky-300 border-sky-500/30', icon: Loader2 },
  Succeeded:  { label: 'Yayınlandı',   className: 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30', icon: Check },
  Failed:     { label: 'Yeniden denenecek', className: 'bg-amber-500/15 text-amber-300 border-amber-500/30', icon: AlertTriangle },
  Blocked:    { label: 'Bloke',        className: 'bg-red-500/15 text-red-400 border-red-500/30', icon: X },
  Skipped:    { label: 'Atlandı',      className: 'bg-slate-500/15 text-slate-400 border-slate-500/30', icon: SkipForward },
};

export function JobStatusPill({ status, title }: { status: string; title?: string }) {
  const cfg = JOB_STATUS[status] ?? { label: status, className: 'bg-slate-500/15 text-slate-400 border-slate-500/30', icon: Clock };
  const Icon = cfg.icon;
  return (
    <span title={title} className={`inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-[11px] font-semibold ${cfg.className}`}>
      <Icon className={cn('h-3 w-3', status === 'InProgress' && 'animate-spin')} /> {cfg.label}
    </span>
  );
}

export function fmtDateTime(iso: string | null | undefined): string {
  if (!iso) return '—';
  return new Date(iso).toLocaleString('tr-TR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

/** Sosyal Medya bölümünün iki sayfası arasında geçiş. */
export function SocialNav() {
  const item = 'px-3 py-1.5 rounded-md text-sm font-medium transition-colors';
  return (
    <div className="inline-flex items-center gap-1 rounded-lg border border-border bg-muted/30 p-1">
      <NavLink to="/admin/social-posts" end className={({ isActive }) => cn(item, isActive ? 'bg-background text-foreground shadow-sm' : 'text-muted-foreground hover:text-foreground')}>
        Onay kuyruğu
      </NavLink>
      <NavLink to="/admin/social-posts/publisher" className={({ isActive }) => cn(item, isActive ? 'bg-background text-foreground shadow-sm' : 'text-muted-foreground hover:text-foreground')}>
        <span className="inline-flex items-center gap-1.5"><Send className="h-3.5 w-3.5" /> Yayınlama &amp; Hesaplar</span>
      </NavLink>
      <NavLink to="/admin/social-posts/profiles" className={({ isActive }) => cn(item, isActive ? 'bg-background text-foreground shadow-sm' : 'text-muted-foreground hover:text-foreground')}>
        <span className="inline-flex items-center gap-1.5"><UserRound className="h-3.5 w-3.5" /> Profiller</span>
      </NavLink>
    </div>
  );
}

/** Onaylı/yayınlanmış gönderinin platform bazlı yayın sonucu (onay ekranı detayında). */
export function PublishJobsBlock({ postId }: { postId: string }) {
  const { data: jobs = [], isLoading } = usePublishJobs({ postId, limit: 20 });
  if (isLoading) return <p className="text-xs text-muted-foreground">Yayın durumu yükleniyor…</p>;
  if (jobs.length === 0) {
    return (
      <div className="rounded-md border border-border bg-muted/20 px-3 py-2 text-xs text-muted-foreground">
        Henüz yayın işi yok — publisher yayın günü {`+`} saatinde eşlenmiş hesaplar için iş açar.
      </div>
    );
  }
  return (
    <div className="rounded-md border border-border px-3 py-2 space-y-1.5">
      <div className="text-xs font-medium text-foreground">Yayın durumu</div>
      {jobs.map((j: SocialPublishJob) => (
        <div key={j.id} className="flex flex-wrap items-center gap-2 text-xs">
          <ProviderIcon provider={j.provider} className="text-muted-foreground" />
          <span className="font-medium">{j.accountLabel}</span>
          <span className="font-mono text-[10px] text-muted-foreground">{j.language}</span>
          <JobStatusPill status={j.status} title={j.lastError ?? undefined} />
          {j.finishedAt && j.status === 'Succeeded' && <span className="text-muted-foreground">{fmtDateTime(j.finishedAt)}</span>}
          {j.platformPostId && <span className="font-mono text-[10px] text-muted-foreground" title="Platform gönderi id">#{j.platformPostId}</span>}
          {j.lastError && j.status !== 'Succeeded' && <span className="text-red-400 truncate max-w-[360px]" title={j.lastError}>{j.lastError}</span>}
        </div>
      ))}
    </div>
  );
}
