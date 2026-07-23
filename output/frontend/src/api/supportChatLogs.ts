import { useQuery } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse } from '@/types';

// ── DTO (matches Liftdesk EMS SupportChatLogDto) ─────────────────────────────

export interface SupportChatLog {
  id: string;
  projectId: string;
  projectName: string;
  userId: string | null;
  userName: string | null;
  userEmail: string | null;
  question: string;
  answer: string;
  createdAt: string;
}

export interface SupportChatLogPage {
  items: SupportChatLog[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface SupportChatLogParams {
  search?: string;
  /** ISO 8601 UTC — createdAt >= startDate. */
  startDate?: string;
  /** ISO 8601 UTC — createdAt <= endDate (exact; pass next-day 00:00 to include a whole day). */
  endDate?: string;
  page?: number;
  pageSize?: number;
}

/** Lists support-assistant chat logs (SuperAdmin only), proxied through the CRM API. */
export function useSupportChatLogs(params: SupportChatLogParams) {
  return useQuery({
    queryKey: ['supportChatLogs', params],
    queryFn: async (): Promise<SupportChatLogPage> => {
      const p = new URLSearchParams();
      if (params.search) p.set('search', params.search);
      if (params.startDate) p.set('startDate', params.startDate);
      if (params.endDate) p.set('endDate', params.endDate);
      p.set('page', String(params.page ?? 1));
      p.set('pageSize', String(params.pageSize ?? 20));
      const res = await apiClient.get<ApiResponse<SupportChatLogPage>>(`/support-chat-logs?${p}`);
      return (
        res.data.data ?? {
          items: [], totalCount: 0, page: 1, pageSize: 20,
          totalPages: 0, hasPreviousPage: false, hasNextPage: false,
        }
      );
    },
  });
}
