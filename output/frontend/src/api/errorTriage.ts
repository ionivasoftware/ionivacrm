import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse } from '@/types';

/** A triaged error card surfaced from the RezervAl error-triage queue. */
export interface ErrorTriageCard {
  triageId: number;
  errorLogId: number;
  fingerprint: string | null;
  occurrenceCount: number;
  status: string;
  severity: string | null;
  rootCause: string | null;
  suggestedFix: string | null;
  sourceFile: string | null;
  exception: string | null;
  typeName: string | null;
  createdOn: string | null;
  updatedOn: string | null;
  approvedBy: string | null;
}

export interface ErrorTriageParams {
  status?: string;
  page?: number;
  pageSize?: number;
}

/** Lists triaged error cards (SuperAdmin only). Proxied through the CRM API. */
export function useErrorTriage(params: ErrorTriageParams) {
  return useQuery({
    queryKey: ['errorTriage', params],
    queryFn: async () => {
      const p = new URLSearchParams();
      p.set('status', params.status ?? 'Triaged');
      p.set('page', String(params.page ?? 1));
      p.set('pageSize', String(params.pageSize ?? 50));
      const res = await apiClient.get<ApiResponse<ErrorTriageCard[]>>(`/error-triage?${p}`);
      return res.data.data ?? [];
    },
    refetchInterval: 60_000,
  });
}

/** Approves or rejects a card. `approvedBy` is derived server-side from the JWT. */
export function useUpdateErrorTriageStatus() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ triageId, status }: { triageId: number; status: 'Approved' | 'Rejected' }) => {
      const res = await apiClient.patch<ApiResponse<ErrorTriageCard>>(
        `/error-triage/${triageId}/status`,
        { status }
      );
      return res.data.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['errorTriage'] });
    },
  });
}
