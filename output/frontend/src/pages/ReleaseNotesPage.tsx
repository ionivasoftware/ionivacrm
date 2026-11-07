import { useEffect, useRef, useState } from 'react';
import type { AxiosError } from 'axios';
import {
  Sparkles, AlertTriangle, FileText, Bell, Wrench, ClipboardList, Hammer, RefreshCw, Receipt, List, Rocket, Globe, Tag,
  CalendarDays, ChevronDown, ChevronUp, Loader2,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { cn } from '@/lib/utils';
import type { ApiResponse } from '@/types';
import {
  useReleaseNotes, useMarkReleaseNotesSeen, formatReleasePeriod,
  type Release, type ReleaseItem, type ReleaseNotes, type ReleaseSection,
} from '@/api/releaseNotes';

// ── Eşlemeler (sözleşme §2) ─────────────────────────────────────────────────────────────────────
// Bölüm anahtarı → ikon; bilinmeyen anahtar genel ikona düşer. Madde türü → rozet; bilinmeyen tür nötr.
// Sayfa okunur bir sütun (max-w-3xl, ortalı) olarak kurulur; telefon/tablet için tek sütun, dokunma payı 44px.

const SECTION_ICONS: Record<string, React.ComponentType<{ className?: string }>> = {
  faults: AlertTriangle,
  documents: FileText,
  notifications: Bell,
  maintenance: Wrench,
  survey: ClipboardList,
  assembly: Hammer,
  revision: RefreshCw,
  accounting: Receipt,
  lists: List,
  onboarding: Rocket,
  international: Globe,
  general: Sparkles,
  /** Bilinmeyen anahtar buraya düşer (sözleşme: "bilinmeyen anahtara genel ikon"). */
  _unknown: Tag,
};

type KnownType = 'new' | 'improvement' | 'fix';
const TYPE_ORDER: readonly KnownType[] = ['new', 'improvement', 'fix'];
const TYPE_META: Record<KnownType, { label: string; badge: string; dot: string }> = {
  new: { label: 'Yeni', badge: 'bg-blue-500/15 text-blue-400 border-blue-500/30', dot: 'bg-blue-500' },
  improvement: { label: 'İyileştirme', badge: 'bg-teal-500/15 text-teal-400 border-teal-500/30', dot: 'bg-teal-500' },
  fix: { label: 'Düzeltme', badge: 'bg-amber-500/15 text-amber-400 border-amber-500/30', dot: 'bg-amber-500' },
};
const NEUTRAL_BADGE = 'bg-slate-500/15 text-slate-400 border-slate-500/30';

function knownType(type: string | null): KnownType | null {
  const key = (type ?? '').trim().toLowerCase();
  return (TYPE_ORDER as readonly string[]).includes(key) ? (key as KnownType) : null;
}

/** Haritada olan anahtarı, yoksa `_unknown`'ı döndürür — ikon bileşeni üye erişimiyle seçilir (render içinde üretilmez). */
function sectionIconKey(key: string | null): string {
  const k = (key ?? '').trim().toLowerCase();
  return k in SECTION_ICONS ? k : '_unknown';
}

function visibleItems(section: ReleaseSection): ReleaseItem[] {
  return (section.items ?? []).filter((it) => it.title && it.title.trim());
}
function visibleSections(release: Release): ReleaseSection[] {
  return (release.sections ?? []).filter((s) => visibleItems(s).length > 0);
}

interface TypeCounts { new: number; improvement: number; fix: number; other: number; total: number }
function countByType(release: Release): TypeCounts {
  const c: TypeCounts = { new: 0, improvement: 0, fix: 0, other: 0, total: 0 };
  for (const s of visibleSections(release)) {
    for (const it of visibleItems(s)) {
      const k = knownType(it.type);
      if (k) c[k] += 1; else c.other += 1;
      c.total += 1;
    }
  }
  return c;
}

/** "3 yeni · 2 iyileştirme · 1 düzeltme" — sabit sıra, sıfırlar yazılmaz, bilinmeyen türler 'değişiklik'. */
function countsLabel(c: TypeCounts): string {
  const parts = TYPE_ORDER.filter((k) => c[k] > 0).map((k) => `${c[k]} ${TYPE_META[k].label.toLowerCase()}`);
  if (c.other > 0) parts.push(`${c.other} değişiklik`);
  return parts.join(' · ');
}

function errorMessage(err: unknown, fallback: string): string {
  const ax = err as AxiosError<ApiResponse<unknown>>;
  return ax?.response?.data?.message || ax?.response?.data?.errors?.[0] || fallback;
}

// ── Parçalar ───────────────────────────────────────────────────────────────────────────────────

function TypeBadge({ type, className }: { type: string | null; className?: string }) {
  const k = knownType(type);
  const label = k ? TYPE_META[k].label : (type?.trim() || 'Değişiklik');
  return (
    <span className={cn('inline-flex items-center rounded-full border px-2 py-0.5 text-[11px] font-semibold whitespace-nowrap', k ? TYPE_META[k].badge : NEUTRAL_BADGE, className)}>
      {label}
    </span>
  );
}

function PeriodChip({ release, className }: { release: Release; className?: string }) {
  const text = formatReleasePeriod(release.periodStart, release.periodEnd);
  if (!text) return null;
  return (
    <span className={cn('inline-flex items-center gap-1.5 text-xs text-muted-foreground', className)}>
      <CalendarDays className="h-3.5 w-3.5 shrink-0" />
      {text}
    </span>
  );
}

function NewPill() {
  return (
    <span className="inline-flex items-center gap-1.5 rounded-full border border-blue-500/30 bg-blue-500/10 px-2.5 py-0.5 text-[11px] font-semibold text-blue-400">
      <span className="h-1.5 w-1.5 rounded-full bg-blue-500" />
      Yeni sürüm
    </span>
  );
}

/** Üç sayaç kutusu — telefonda da üç sütun (kısa sayılar). */
function CountTiles({ counts }: { counts: TypeCounts }) {
  return (
    <div className="grid grid-cols-3 gap-2 sm:gap-3">
      {TYPE_ORDER.map((k) => (
        <div key={k} className="rounded-xl border border-border bg-background/60 px-2 py-2.5 text-center sm:px-3">
          <p className="text-lg font-semibold tabular-nums text-foreground sm:text-xl">{counts[k]}</p>
          <p className="mt-0.5 flex items-center justify-center gap-1.5 text-[10px] font-medium uppercase tracking-wide text-muted-foreground sm:text-[11px]">
            <span className={cn('h-1.5 w-1.5 shrink-0 rounded-full', TYPE_META[k].dot)} />
            {TYPE_META[k].label}
          </p>
        </div>
      ))}
    </div>
  );
}

function SectionBlock({ section }: { section: ReleaseSection }) {
  const Icon = SECTION_ICONS[sectionIconKey(section.key)];
  const list = visibleItems(section);
  if (list.length === 0) return null;
  return (
    <section className="space-y-2.5">
      <h3 className="flex items-center gap-2.5 text-sm font-semibold text-foreground">
        <span className="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border border-border bg-card text-muted-foreground">
          <Icon className="h-4 w-4" />
        </span>
        {section.title?.trim() || 'Genel'}
      </h3>
      <ul className="divide-y divide-border overflow-hidden rounded-xl border border-border bg-card">
        {list.map((it, i) => (
          <li key={i} className="flex flex-col gap-1.5 p-3 sm:flex-row sm:items-start sm:gap-3 sm:p-4">
            <TypeBadge type={it.type} className="self-start sm:mt-0.5 sm:w-24 sm:justify-center" />
            <div className="min-w-0 flex-1">
              <p className="text-sm leading-snug text-foreground">{it.title}</p>
              {it.note && <p className="mt-1 text-xs leading-snug text-muted-foreground">{it.note}</p>}
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
}

function ReleaseSections({ release }: { release: Release }) {
  const sections = visibleSections(release);
  if (sections.length === 0) return <p className="text-sm text-muted-foreground">Bu sürümde listelenen madde yok.</p>;
  return (
    <div className="space-y-6">
      {sections.map((s, i) => <SectionBlock key={`${s.key ?? 'x'}-${i}`} section={s} />)}
    </div>
  );
}

/** En yeni sürüm: başlık bloğu + sayaçlar, altında bölümler. */
function LatestRelease({ release, isNew }: { release: Release; isNew: boolean }) {
  const counts = countByType(release);
  return (
    <article className="overflow-hidden rounded-2xl border border-border bg-card shadow-sm">
      <div className="space-y-5 p-5 sm:p-7">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <PeriodChip release={release} />
          {isNew && <NewPill />}
        </div>
        <div className="space-y-2">
          <h2 className="text-xl font-semibold tracking-tight text-foreground sm:text-2xl">{release.title?.trim() || 'Yenilikler ve İyileştirmeler'}</h2>
          {release.summary && <p className="text-sm text-muted-foreground sm:text-base">{release.summary}</p>}
        </div>
        {counts.total > 0 && <CountTiles counts={counts} />}
      </div>
      <div className="border-t border-border bg-muted/30 p-5 sm:p-7">
        <ReleaseSections release={release} />
      </div>
    </article>
  );
}

/** Önceki sürüm: zaman çizelgesi noktası + katlanır kart. */
function PastRelease({ release, isNew }: { release: Release; isNew: boolean }) {
  const [open, setOpen] = useState(false);
  const counts = countsLabel(countByType(release));
  const panelId = `release-${release.id}`;
  return (
    <li className="relative">
      <span
        aria-hidden
        className={cn('absolute -left-[22px] top-[18px] h-3 w-3 rounded-full ring-4 ring-background', isNew ? 'bg-blue-500' : 'bg-border')}
      />
      <div className="overflow-hidden rounded-xl border border-border bg-card">
        <button
          type="button"
          onClick={() => setOpen((o) => !o)}
          aria-expanded={open}
          aria-controls={panelId}
          className="flex min-h-[44px] w-full items-center gap-3 p-4 text-left transition-colors hover:bg-accent/50"
        >
          <div className="min-w-0 flex-1 space-y-1">
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
              <PeriodChip release={release} />
              {isNew && <span className="text-[11px] font-semibold text-blue-400">Yeni</span>}
            </div>
            <p className="truncate text-sm font-medium text-foreground">{release.title?.trim() || 'Yenilikler ve İyileştirmeler'}</p>
            {counts && <p className="text-xs text-muted-foreground">{counts}</p>}
          </div>
          {open ? <ChevronUp className="h-4 w-4 shrink-0 text-muted-foreground" /> : <ChevronDown className="h-4 w-4 shrink-0 text-muted-foreground" />}
        </button>
        {open && (
          <div id={panelId} className="space-y-5 border-t border-border bg-muted/30 p-4 sm:p-5">
            {release.summary && <p className="text-sm text-muted-foreground">{release.summary}</p>}
            <ReleaseSections release={release} />
          </div>
        )}
      </div>
    </li>
  );
}

/**
 * Yüklenmiş veriyle çalışan gövde. Görülmemiş sayısı ilk gelen veriden bir kez alınır (useState başlangıcı): ekran
 * açılır açılmaz "görüldü" işaretlenir ve önbellek sıfırlanır, ama bu ziyarette "yeni" işaretleri yerinde kalır.
 */
function ReleaseList({ data }: { data: ReleaseNotes }) {
  const [unseenAtOpen] = useState(data.unseenCount);
  const mark = useMarkReleaseNotesSeen();
  const markedRef = useRef<string | null>(null);
  const latest = data.releases[0];

  useEffect(() => {
    if (!latest) return;
    if (data.lastSeenReleaseId === latest.id) return;
    if (markedRef.current === latest.id) return;
    markedRef.current = latest.id;
    mark.mutate(latest.id);
  }, [latest, data.lastSeenReleaseId, mark]);

  if (!latest) {
    return (
      <div className="rounded-2xl border border-dashed border-border p-10 text-center text-sm text-muted-foreground">
        Henüz yayınlanmış sürüm notu yok.
      </div>
    );
  }

  const past = data.releases.slice(1);
  return (
    <div className="space-y-8">
      <LatestRelease release={latest} isNew={unseenAtOpen > 0} />
      {past.length > 0 && (
        <section className="space-y-4">
          <h2 className="px-1 text-xs font-semibold uppercase tracking-wider text-muted-foreground">Önceki sürümler</h2>
          <ol className="relative space-y-3 pl-7 before:absolute before:bottom-4 before:left-[11px] before:top-4 before:w-px before:bg-border">
            {past.map((r, i) => <PastRelease key={r.id} release={r} isNew={i + 1 < unseenAtOpen} />)}
          </ol>
        </section>
      )}
    </div>
  );
}

// ── Sayfa ──────────────────────────────────────────────────────────────────────────────────────

export function ReleaseNotesPage() {
  const { data, isLoading, isError, error, refetch, isFetching } = useReleaseNotes();
  const updated = data?.generatedAt ? formatReleasePeriod(data.generatedAt, null) : null;

  return (
    <div className="mx-auto w-full max-w-3xl space-y-8 pb-10 pt-2 sm:pt-6">
      <header className="space-y-3 text-center">
        <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-primary/10 text-primary">
          <Sparkles className="h-6 w-6" />
        </div>
        <h1 className="text-2xl font-bold tracking-tight text-foreground sm:text-3xl">Yenilikler</h1>
        <p className="mx-auto max-w-prose text-sm text-muted-foreground sm:text-base">
          Liftdesk'te her hafta yayınlanan özellikler, iyileştirmeler ve düzeltmeler. Müşteriyle görüşmeden önce "yeni ne var" diye buradan bakın.
        </p>
        <div className="flex items-center justify-center gap-3 text-xs text-muted-foreground">
          {updated && <span>Güncelleme: {updated}</span>}
          <Button variant="ghost" size="sm" onClick={() => refetch()} disabled={isFetching} className="h-8 px-2.5 text-xs">
            {isFetching ? <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" /> : <RefreshCw className="mr-1.5 h-3.5 w-3.5" />}
            Yenile
          </Button>
        </div>
      </header>

      {isLoading && (
        <div className="space-y-4">
          <Skeleton className="h-64 w-full rounded-2xl" />
          <Skeleton className="h-16 w-full rounded-xl" />
          <Skeleton className="h-16 w-full rounded-xl" />
        </div>
      )}

      {isError && (
        <div className="flex flex-col items-start gap-3 rounded-2xl border border-border bg-card p-5 sm:flex-row sm:items-center sm:p-6">
          <AlertTriangle className="h-5 w-5 shrink-0 text-amber-400" />
          <div className="flex-1 text-sm">
            <p className="font-medium text-foreground">Sürüm notları alınamadı</p>
            <p className="text-muted-foreground">{errorMessage(error, 'Liftdesk\'e ulaşılamadı. Biraz sonra tekrar deneyin.')}</p>
          </div>
          <Button variant="outline" size="sm" onClick={() => refetch()}>Tekrar dene</Button>
        </div>
      )}

      {data && <ReleaseList data={data} />}
    </div>
  );
}
