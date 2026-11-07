import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse } from '@/types';

// ── Liftdesk haftalık sürüm notları — "Yenilikler" ekranı ─────────────────────────────────────
// EMS tek bir salt-okunur uçtan JSON verir (sözleşme "Sürüm Notları API (CRM)"); CRM sunucusu proxy'ler
// (anahtar tarayıcıya inmez, yük 10 dk bellekte), ekran olduğu gibi basar: sections/items sırası anlamlı,
// metinler görüntülemeye hazır, tarihler yyyy-MM-dd düz metin. Bölüm anahtarı ve madde türü açık uçlu string.

export type ReleaseItemType = 'new' | 'improvement' | 'fix';

export interface ReleaseItem {
  /** new | improvement | fix — bilinmeyen tür nötr rozetle gösterilir. */
  type: ReleaseItemType | string | null;
  title: string | null;
  /** İsteğe bağlı ikinci satır. */
  note: string | null;
}

export interface ReleaseSection {
  /** faults, documents, notifications, maintenance, survey, assembly, revision, accounting, lists, onboarding, international, general … */
  key: string | null;
  title: string | null;
  items: ReleaseItem[] | null;
}

export interface Release {
  /** Kararlı kimlik — "görüldü" anahtarı. */
  id: string;
  title: string | null;
  /** yyyy-MM-dd */
  periodStart: string | null;
  periodEnd: string | null;
  summary: string | null;
  sections: ReleaseSection[] | null;
}

export interface ReleaseNotes {
  language: string;
  generatedAt: string | null;
  /** En yeni en başta. */
  releases: Release[];
  lastSeenReleaseId: string | null;
  /** Kullanıcının görmediği sürüm sayısı (ilk n öğe). */
  unseenCount: number;
}

export interface ReleaseNotesStatus {
  configured: boolean;
  latestReleaseId: string | null;
  latestTitle: string | null;
  latestPeriodEnd: string | null;
  lastSeenReleaseId: string | null;
  unseenCount: number;
}

const KEY = ['release-notes'] as const;

export function useReleaseNotes() {
  return useQuery({
    queryKey: [...KEY, 'all'],
    queryFn: async () => (await apiClient.get<ApiResponse<ReleaseNotes>>('/release-notes')).data.data,
    staleTime: 5 * 60_000,
    retry: 1,
  });
}

/** Menü rozeti — sunucu Liftdesk'e ulaşamazsa unseenCount 0 döner, hata üretmez. */
export function useReleaseNotesStatus(enabled = true) {
  return useQuery({
    queryKey: [...KEY, 'status'],
    queryFn: async () => (await apiClient.get<ApiResponse<ReleaseNotesStatus>>('/release-notes/status')).data.data,
    enabled,
    staleTime: 5 * 60_000,
    refetchInterval: 10 * 60_000,
    retry: 1,
  });
}

/** Ekran açılınca en üstteki sürüm görüldü sayılır; rozet ve liste önbelleği yerinde güncellenir. */
export function useMarkReleaseNotesSeen() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (releaseId: string) =>
      (await apiClient.post<ApiResponse<ReleaseNotesStatus>>('/release-notes/seen', { releaseId })).data.data,
    onSuccess: (status) => {
      qc.setQueryData<ReleaseNotesStatus>([...KEY, 'status'], status);
      qc.setQueryData<ReleaseNotes | undefined>([...KEY, 'all'], (old) =>
        old ? { ...old, lastSeenReleaseId: status.lastSeenReleaseId, unseenCount: status.unseenCount } : old,
      );
    },
  });
}

/** "31 Ekim – 7 Kasım 2026"; tek tarih varsa onu, hiçbiri yoksa boş. Tarihler UTC'siz düz gün olarak okunur. */
export function formatReleasePeriod(start: string | null, end: string | null): string {
  const day = (s: string, opts: Intl.DateTimeFormatOptions) =>
    /^\d{4}-\d{2}-\d{2}$/.test(s)
      ? new Intl.DateTimeFormat('tr-TR', { timeZone: 'UTC', ...opts }).format(new Date(`${s}T00:00:00Z`))
      : s;
  const full: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'long', year: 'numeric' };
  if (!start && !end) return '';
  if (!start || !end) return day((start ?? end)!, full);
  const sameYear = start.slice(0, 4) === end.slice(0, 4);
  return `${day(start, sameYear ? { day: 'numeric', month: 'long' } : full)} – ${day(end, full)}`;
}
