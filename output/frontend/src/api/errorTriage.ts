import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse } from '@/types';

/** Where a triaged error card originated. */
export type ErrorTriageSource = 'Rezerval' | 'Liftdesk';

/**
 * A triaged error card surfaced from an error-triage queue.
 * `id` is the source system's identifier (Rezerval: numeric string, Liftdesk: guid) —
 * only meaningful together with `source`.
 */
export interface ErrorTriageCard {
  id: string;
  source: ErrorTriageSource;
  fingerprint: string | null;
  occurrenceCount: number;
  /** Triaged | Approved | Rejected | Fixed — Liftdesk additionally: Fixing | Failed */
  status: string;
  severity: string | null;
  rootCause: string | null;
  suggestedFix: string | null;
  sourceFile: string | null;
  exception: string | null;
  typeName: string | null;
  /** Liftdesk only: which app produced the error (Backend, Frontend, ...). */
  component: string | null;
  createdOn: string | null;
  updatedOn: string | null;
  approvedBy: string | null;
  /** Liftdesk only: why the card was rejected. */
  rejectReason: string | null;
  /** Liftdesk only: PR link once the fix agent delivers. */
  fixPrUrl: string | null;
  /** Liftdesk only: why the fix agent gave up. */
  failReason: string | null;
}

export interface ErrorTriageParams {
  status?: string;
  page?: number;
  pageSize?: number;
}

export interface ErrorTriageResult {
  cards: ErrorTriageCard[];
  /** Partial-failure note from the API (e.g. one source unreachable), null when all sources answered. */
  warning: string | null;
}

/** Lists error cards from all configured sources (SuperAdmin only). Proxied through the CRM API. */
export function useErrorTriage(params: ErrorTriageParams) {
  return useQuery({
    queryKey: ['errorTriage', params],
    queryFn: async (): Promise<ErrorTriageResult> => {
      const p = new URLSearchParams();
      p.set('status', params.status ?? 'Triaged');
      p.set('page', String(params.page ?? 1));
      p.set('pageSize', String(params.pageSize ?? 50));
      const res = await apiClient.get<ApiResponse<ErrorTriageCard[]>>(`/error-triage?${p}`);
      return { cards: res.data.data ?? [], warning: res.data.message ?? null };
    },
    refetchInterval: 60_000,
  });
}

export interface UpdateErrorTriageInput {
  source: ErrorTriageSource;
  id: string;
  status: 'Approved' | 'Rejected';
  /** Sent to Liftdesk on reject; the API defaults it when empty. */
  rejectReason?: string;
}

/** Approves or rejects a card on its source system. `approvedBy` is derived server-side from the JWT. */
export function useUpdateErrorTriageStatus() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ source, id, status, rejectReason }: UpdateErrorTriageInput) => {
      const res = await apiClient.patch<ApiResponse<ErrorTriageCard>>(
        `/error-triage/${source}/${id}/status`,
        { status, rejectReason }
      );
      return res.data.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['errorTriage'] });
    },
  });
}
