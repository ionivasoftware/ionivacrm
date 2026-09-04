import { useQuery } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse } from '@/types';

// ── Types (docs/crm-backup-api.md §5) ─────────────────────────────────────────

export type BackupKind = 'Backup' | 'Verify' | 'Mirror';
export type BackupRunStatus = 'Running' | 'Succeeded' | 'Failed';

export interface BackupRun {
  id: string;
  kind: BackupKind | string;
  status: BackupRunStatus | string;
  backupName: string | null;
  /** UTC — yerel saate çevrilmeli. */
  startedAt: string;
  completedAt: string | null;
  durationSeconds: number | null;
  /** Şifrelenmiş arşiv boyutu (byte). */
  sizeBytes: number | null;
  /** Kaynak veritabanı boyutu (byte). */
  databaseSizeBytes: number | null;
  archiveEntries: number | null;
  destination: string | null;
  /** Şeması SABİT DEĞİL — bilinmeyen alanlar yok sayılır, eksik alanda patlanmaz. */
  sourceCounts: Record<string, unknown> | null;
  /** 'full' (veri dahil) | 'schema' (yalnız şema — zayıf hâl). */
  verifyMode: string | null;
  countsMatched: boolean | null;
  message: string | null;
  /** GitHub Actions koşu bağlantısı — "Logu aç". */
  runUrl: string | null;
  triggeredBy: string | null;
}

export interface BackupStatus {
  isHealthy: boolean;
  /** Türkçe, doğrudan ekranda gösterilebilir. */
  problems: string[] | null;
  lastBackup: BackupRun | null;
  lastSuccessfulBackup: BackupRun | null;
  hoursSinceLastSuccessfulBackup: number | null;
  lastVerify: BackupRun | null;
  lastSuccessfulVerify: BackupRun | null;
  hoursSinceLastSuccessfulVerify: number | null;
  lastMirror: BackupRun | null;
  failuresLast7Days: number;
  latestBackupSizeBytes: number | null;
  latestDatabaseSizeBytes: number | null;
}

// ── Hooks ─────────────────────────────────────────────────────────────────────

/**
 * Pano kartı durumu. Sözleşme 5 dakikada bir yenilemeyi öneriyor.
 *
 * `enabled` ile yalnız SuperAdmin'de çağrılır — uç SuperAdmin korumalı, aksi hâlde
 * panoda herkese 403 düşer.
 */
export function useBackupStatus(enabled = true) {
  return useQuery({
    queryKey: ['backups', 'status'],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<BackupStatus>>('/backups/status');
      return response.data.data;
    },
    enabled,
    refetchInterval: 5 * 60 * 1000,
    staleTime: 60 * 1000,
    retry: 1,
  });
}

/** Koşu geçmişi (yeniden eskiye). kind boşsa tüm türler döner. */
export function useBackupRuns(kind: string | null, limit = 50, enabled = true) {
  return useQuery({
    queryKey: ['backups', 'runs', kind, limit],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<BackupRun[]>>('/backups', {
        params: { kind: kind || undefined, limit },
      });
      return response.data.data ?? [];
    },
    enabled,
    staleTime: 60 * 1000,
    retry: 1,
  });
}

// ── Ortak biçimlendiriciler ───────────────────────────────────────────────────

/** Byte → GB/MB. Sözleşme 1024³ kullanılmasını şart koşuyor. */
export function formatBytes(bytes: number | null | undefined): string {
  if (bytes == null) return '—';
  const gb = bytes / 1024 ** 3;
  if (gb >= 1) return `${gb.toFixed(2)} GB`;
  const mb = bytes / 1024 ** 2;
  return `${mb.toFixed(0)} MB`;
}

/** "6 saat önce" / "2 gün önce". */
export function formatHoursAgo(hours: number | null | undefined): string {
  if (hours == null) return 'bilinmiyor';
  if (hours < 1) return 'az önce';
  if (hours < 24) return `${Math.round(hours)} saat önce`;
  return `${Math.round(hours / 24)} gün önce`;
}

/** UTC damgasını yerel saate çevirir (sözleşme: tüm tarihler UTC). */
export function formatUtc(iso: string | null | undefined): string {
  if (!iso) return '—';
  const d = new Date(iso.endsWith('Z') || iso.includes('+') ? iso : `${iso}Z`);
  return d.toLocaleString('tr-TR');
}

export function formatDuration(seconds: number | null | undefined): string {
  if (seconds == null) return '—';
  if (seconds < 60) return `${seconds} sn`;
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return s === 0 ? `${m} dk` : `${m} dk ${s} sn`;
}
