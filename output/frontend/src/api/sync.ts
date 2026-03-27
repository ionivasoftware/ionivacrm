import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse } from '@/types';

export interface SyncLog {
  id: string;
  projectId: string;
  source: string;        // 'SaasA' | 'SaasB'
  direction: string;     // 'Inbound' | 'Outbound'
  entityType: string;
  entityId: string | null;
  status: string;        // 'Pending' | 'Success' | 'Failed' | 'Retrying'
  errorMessage: string | null;
  retryCount: number;
  syncedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface SyncLogsResult {
  items: SyncLog[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

export interface SyncLogsParams {
  page?: number;
  pageSize?: number;
  source?: string;
  status?: string;
}

export function useSyncLogs(params: SyncLogsParams) {
  return useQuery({
    queryKey: ['syncLogs', params],
    queryFn: async () => {
      const p = new URLSearchParams();
      p.set('page', String(params.page ?? 1));
      p.set('pageSize', String(params.pageSize ?? 20));
      if (params.source) p.set('source', params.source);
      if (params.status) p.set('status', params.status);
      const res = await apiClient.get<ApiResponse<SyncLogsResult>>(`/sync/logs?${p}`);
      return res.data.data;
    },
    refetchInterval: 30_000, // auto-refresh every 30 seconds
  });
}

export function useTriggerSync() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      const res = await apiClient.post<ApiResponse<{ jobId: string }>>('/sync/trigger');
      return res.data.data;
    },
    onSuccess: () => {
      setTimeout(() => qc.invalidateQueries({ queryKey: ['syncLogs'] }), 3000);
    },
  });
}
