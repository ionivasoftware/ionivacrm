import { useEffect, useRef, useState } from 'react';
import type { AxiosError } from 'axios';
import {
  Sparkles, AlertTriangle, FileText, Bell, Wrench, ClipboardList, Hammer, RefreshCw, Receipt, List, Rocket, Globe, Tag,
  CalendarDays, ChevronDown, ChevronUp, Loader2,
} from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
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

const TYPE_META: Record<string, { label: string; className: string }> = {
  new: { label: 'Yeni', className: 'bg-blue-500/15 text-blue-400 border-blue-500/30' },
  improvement: { label: 'İyileştirme', className: 'bg-teal-500/15 text-teal-400 border-teal-500/30' },
  fix: { label: 'Düzeltme', className: 'bg-amber-500/15 text-amber-400 border-amber-500/30' },
};
const TYPE_ORDER = ['new', 'improvement', 'fix'] as const;

function typeMeta(type: string | null): { label: string; className: string } {
  const key = (type ?? '').trim().toLowerCase();
  return TYPE_META[key] ?? { label: type?.trim() || 'Değişiklik', className: 'bg-slate-500/15 text-slate-400 border-slate-500/30' };
}

/** Haritada olan anahtarı, yoksa `_unknown`'ı döndürür — ikon bileşeni üye erişimiyle seçilir (render içinde üretilmez). */
function sectionIconKey(key: string | null): string {
  const k = (key ?? '').trim().toLowerCase();
  return k in SECTION_ICONS ? k : '_unknown';
}

function items(release: Release): ReleaseItem[] {
  return (release.sections ?? []).flatMap((s) => s.items ?? []);
}

/** "3 yeni · 2 iyileştirme · 1 düzeltme" — sırası sabit, sıfırlar yazılmaz, bilinmeyen türler 'değişiklik'. */
function countsLabel(release: Release): string {
  const all = items(release);
  const counts = new Map<string, number>();
  for (const it of all) {
    const key = (it.type ?? '').trim().toLowerCase();
    const k = (TYPE_ORDER as readonly string[]).includes(key) ? key : 'other';
    counts.set(k, (counts.get(k) ?? 0) + 1);
  }
  const parts: string[] = [];
  for (const k of TYPE_ORDER) if (counts.get(k)) parts.push(`${counts.get(k)} ${TYPE_META[k].label.toLowerCase()}`);
  if (counts.get('other')) parts.push(`${counts.get('other')} değişiklik`);
  return parts.join(' · ');
}

function errorMessage(err: unknown, fallback: string): string {
  const ax = err as AxiosError<ApiResponse<unknown>>;
  return ax?.response?.data?.message || ax?.response?.data?.errors?.[0] || fallback;
}

// ── Parçalar ───────────────────────────────────────────────────────────────────────────────────

function TypeBadge({ type }: { type: string | null }) {
  const m = typeMeta(type);
  return (
    <span className={cn('inline-flex items-center rounded-full border px-2 py-0.5 text-[11px] font-semibold whitespace-nowrap', m.className)}>
      {m.label}
    </span>
  );
}

function SectionBlock({ section }: { section: ReleaseSection }) {
  const Icon = SECTION_ICONS[sectionIconKey(section.key)];
  const list = (section.items ?? []).filter((it) => it.title && it.title.trim());
  if (list.length === 0) return null;
  return (
    <section className="space-y-2">
      <h3 className="flex items-center gap-2 text-sm font-semibold text-foreground">
        <span className="inline-flex h-7 w-7 items-center justify-center rounded-md bg-muted text-muted-foreground">
          <Icon className="h-4 w-4" />
        </span>
        {section.title?.trim() || 'Genel'}
      </h3>
      <ul className="space-y-2 pl-1">
        {list.map((it, i) => (
          <li key={i} className="flex items-start gap-2.5">
            <TypeBadge type={it.type} />
            <div className="min-w-0 text-sm">
              <p className="text-foreground leading-snug">{it.title}</p>
              {it.note && <p className="text-muted-foreground text-xs mt-0.5 leading-snug">{it.note}</p>}
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
}

function ReleaseBody({ release }: { release: Release }) {
  const sections = (release.sections ?? []).filter((s) => (s.items ?? []).some((it) => it.title && it.title.trim()));
  if (sections.length === 0) return <p className="text-sm text-muted-foreground">Bu sürümde listelenen madde yok.</p>;
  return (
    <div className="grid gap-5 md:grid-cols-2">
      {sections.map((s, i) => <SectionBlock key={`${s.key ?? 'x'}-${i}`} section={s} />)}
    </div>
  );
}

function PeriodChip({ release }: { release: Release }) {
  const text = formatReleasePeriod(release.periodStart, release.periodEnd);
  if (!text) return null;
  return (
    <span className="inline-flex items-center gap-1.5 text-xs text-muted-foreground">
      <CalendarDays className="h-3.5 w-3.5" />
      {text}
    </span>
  );
}

function NewDot({ title = 'Son ziyaretinizden sonra yayınlandı' }: { title?: string }) {
  return <span className="inline-block h-2 w-2 rounded-full bg-blue-500 ring-2 ring-card" title={title} aria-label={title} />;
}

/** En yeni sürüm: açık, tam içerik. */
function LatestRelease({ release, isNew }: { release: Release; isNew: boolean }) {
  const counts = countsLabel(release);
  return (
    <Card>
      <CardContent className="p-5 sm:p-6 space-y-5">
        <div className="space-y-1.5">
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <PeriodChip release={release} />
            {isNew && (
              <span className="inline-flex items-center gap-1.5 text-xs font-medium text-blue-400">
                <NewDot /> Yeni sürüm
              </span>
            )}
          </div>
          <h2 className="text-xl font-semibold text-foreground">{release.title?.trim() || 'Yenilikler ve İyileştirmeler'}</h2>
          {release.summary && <p className="text-sm text-muted-foreground max-w-prose">{release.summary}</p>}
          {counts && <p className="text-xs text-muted-foreground">{counts}</p>}
        </div>
        <ReleaseBody release={release} />
      </CardContent>
    </Card>
  );
}

/** Önceki sürüm: başlık satırı, tıklayınca içerik. */
function PastRelease({ release, isNew }: { release: Release; isNew: boolean }) {
  const [open, setOpen] = useState(false);
  const counts = countsLabel(release);
  const panelId = `release-${release.id}`;
  return (
    <Card>
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-expanded={open}
        aria-controls={panelId}
        className="w-full flex items-center gap-3 p-4 text-left hover:bg-accent/50 rounded-lg transition-colors"
      >
        <div className="min-w-0 flex-1 space-y-0.5">
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <PeriodChip release={release} />
            {isNew && <NewDot />}
          </div>
          <p className="text-sm font-medium text-foreground truncate">{release.title?.trim() || 'Yenilikler ve İyileştirmeler'}</p>
          {counts && <p className="text-xs text-muted-foreground">{counts}</p>}
        </div>
        {open ? <ChevronUp className="h-4 w-4 text-muted-foreground shrink-0" /> : <ChevronDown className="h-4 w-4 text-muted-foreground shrink-0" />}
      </button>
      {open && (
        <CardContent id={panelId} className="px-4 pb-5 pt-0 space-y-4">
          {release.summary && <p className="text-sm text-muted-foreground max-w-prose">{release.summary}</p>}
          <ReleaseBody release={release} />
        </CardContent>
      )}
    </Card>
  );
}

/**
 * Yüklenmiş veriyle çalışan gövde. Görülmemiş sayısı ilk gelen veriden bir kez alınır (useState başlangıcı): ekran
 * açılır açılmaz "görüldü" işaretlenir ve önbellek sıfırlanır, ama bu ziyarette "yeni" noktaları yerinde kalır.
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
      <Card>
        <CardContent className="p-8 text-center text-sm text-muted-foreground">Henüz yayınlanmış sürüm notu yok.</CardContent>
      </Card>
    );
  }

  const past = data.releases.slice(1);
  return (
    <div className="space-y-6">
      <LatestRelease release={latest} isNew={unseenAtOpen > 0} />
      {past.length > 0 && (
        <div className="space-y-3">
          <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wider">Önceki sürümler</h2>
          {past.map((r, i) => <PastRelease key={r.id} release={r} isNew={i + 1 < unseenAtOpen} />)}
        </div>
      )}
    </div>
  );
}

// ── Sayfa ──────────────────────────────────────────────────────────────────────────────────────

export function ReleaseNotesPage() {
  const { data, isLoading, isError, error, refetch, isFetching } = useReleaseNotes();

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h1 className="text-2xl font-bold text-foreground flex items-center gap-2">
            <Sparkles className="h-6 w-6 text-primary" />
            Yenilikler
          </h1>
          <p className="text-sm text-muted-foreground mt-1 max-w-prose">
            Liftdesk'te her hafta yayınlanan özellikler, iyileştirmeler ve düzeltmeler. Müşteriyle görüşmeden önce "yeni ne var" diye buradan bakın.
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={() => refetch()} disabled={isFetching} className="self-start">
          {isFetching ? <Loader2 className="h-4 w-4 mr-2 animate-spin" /> : <RefreshCw className="h-4 w-4 mr-2" />}
          Yenile
        </Button>
      </div>

      {isLoading && (
        <div className="space-y-4">
          <Skeleton className="h-48 w-full rounded-lg" />
          <Skeleton className="h-16 w-full rounded-lg" />
          <Skeleton className="h-16 w-full rounded-lg" />
        </div>
      )}

      {isError && (
        <Card>
          <CardContent className="p-6 flex flex-col items-start gap-3 sm:flex-row sm:items-center">
            <AlertTriangle className="h-5 w-5 text-amber-400 shrink-0" />
            <div className="flex-1 text-sm">
              <p className="font-medium text-foreground">Sürüm notları alınamadı</p>
              <p className="text-muted-foreground">{errorMessage(error, 'Liftdesk\'e ulaşılamadı. Biraz sonra tekrar deneyin.')}</p>
            </div>
            <Button variant="outline" size="sm" onClick={() => refetch()}>Tekrar dene</Button>
          </CardContent>
        </Card>
      )}

      {data && <ReleaseList data={data} />}
    </div>
  );
}
