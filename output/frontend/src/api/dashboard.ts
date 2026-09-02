import { useQuery } from '@tanstack/react-query';
import { apiClient } from './client';
import { useAuthStore } from '@/stores/authStore';
import type { ApiResponse, DashboardStats, RecentActivity } from '@/types';

export function useDashboardStats() {
  const projectId = useAuthStore((s) => s.currentProjectId);
  return useQuery({
    queryKey: ['dashboard', 'stats', projectId],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<DashboardStats>>(
        '/dashboard/stats',
        { params: { projectId } }
      );
      return response.data.data;
    },
    enabled: !!projectId,
    staleTime: 2 * 60 * 1000,
    retry: 1,
  });
}

export function useNotifications() {
  return useQuery({
    queryKey: ['dashboard', 'notifications'],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<RecentActivity[]>>(
        '/dashboard/notifications'
      );
      return response.data.data ?? [];
    },
    staleTime: 60 * 1000,
    retry: 1,
  });
}

// ── Customer usage report (churn dashboard) ────────────────────────────────

export interface UsageReportRow {
  customerId: string;
  companyName: string;
  legacyId: string | null;
  status: string | null;
  snapshotYear: number;
  snapshotMonth: number;
  elevatorCount: number;
  userCount: number;
  lastLoginAt: string | null;
  maintenanceCount: number;
  faultCount: number;
  partChangeOfferCount: number;
  revisionOfferCount: number;
  assemblyOfferCount: number;
  workOrderCount: number;
  /** Ay içinde kesilen fatura — cari-fatura kullanımının "fatura" yarısı. */
  invoiceCount: number;
  /** Ay içinde kaydedilen tahsilat — "cari" yarısı; fatura kesmeyen firmalarda asıl gösterge. */
  collectionCount: number;
  /** "CurrentAccount" | "Invoice" — 0 fatura sayısını doğru yorumlamak için. */
  accountingMode: string | null;
  planTier: string | null;
  planStatus: string | null;
  planMonthlyPrice: number | null;
  expirationDate: string | null;
  capturedAt: string;
}

/** Customer usage report for a month (defaults to current on the server). */
export function useUsageReport(year?: number, month?: number) {
  return useQuery({
    queryKey: ['dashboard', 'usage-report', year, month],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<UsageReportRow[]>>(
        '/dashboard/usage-report',
        { params: { year, month } }
      );
      return response.data.data ?? [];
    },
    staleTime: 5 * 60 * 1000,
    retry: 1,
  });
}
