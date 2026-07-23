import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse } from '@/types';

// ── Enums (must match Liftdesk EMS enum names) ───────────────────────────────

export type TicketStatus =
  | 'New' | 'Triaged' | 'Approved' | 'Rejected' | 'InProgress' | 'Done' | 'Failed';
export type TicketType = 'Feedback' | 'Suggestion';
export type TicketPlatform = 'Web' | 'MobileStaff' | 'CustomerPortal' | 'CustomerMobile';
export type TicketSource = 'Tenant' | 'Crm';

// ── DTO (CRM view — all fields) ──────────────────────────────────────────────

export interface Ticket {
  id: string;
  projectId: string | null;
  projectName: string | null;
  createdByUserId: string | null;
  createdByName: string;
  source: TicketSource | string;
  type: TicketType | string;
  platform: TicketPlatform | string;
  subject: string;
  description: string;
  status: TicketStatus | string;
  /** AI triage — CRM-only. */
  agentComment: string | null;
  agentSuggestedAction: string | null;
  agentAnalyzedAt: string | null;
  /** CRM decision. */
  decisionNote: string | null;
  decidedBy: string | null;
  decidedAt: string | null;
  /** Fix-agent result. */
  resolutionNote: string | null;
  fixBranch: string | null;
  fixPrUrl: string | null;
  failReason: string | null;
  completedAt: string | null;
  createdAt: string;
}

export interface TicketPage {
  items: Ticket[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface TicketListParams {
  status?: string;
  type?: string;
  platform?: string;
  projectId?: string;
  page?: number;
  pageSize?: number;
}

/** Lists tickets (SuperAdmin only), proxied through the CRM API. Polls every 60s (agents run on cron). */
export function useTickets(params: TicketListParams) {
  return useQuery({
    queryKey: ['tickets', params],
    queryFn: async (): Promise<TicketPage> => {
      const p = new URLSearchParams();
      if (params.status) p.set('status', params.status);
      if (params.type) p.set('type', params.type);
      if (params.platform) p.set('platform', params.platform);
      if (params.projectId) p.set('projectId', params.projectId);
      p.set('page', String(params.page ?? 1));
      p.set('pageSize', String(params.pageSize ?? 20));
      const res = await apiClient.get<ApiResponse<TicketPage>>(`/tickets?${p}`);
      return (
        res.data.data ?? {
          items: [], totalCount: 0, page: 1, pageSize: 20,
          totalPages: 0, hasPreviousPage: false, hasNextPage: false,
        }
      );
    },
    refetchInterval: 60_000,
  });
}

/** Fetches a single ticket's full detail. */
export function useTicket(id: string | null) {
  return useQuery({
    queryKey: ['ticket', id],
    queryFn: async () => {
      const res = await apiClient.get<ApiResponse<Ticket>>(`/tickets/${id}`);
      return res.data.data;
    },
    enabled: !!id,
  });
}

export interface CreateTicketInput {
  projectId?: string | null;
  type: TicketType;
  platform: TicketPlatform;
  subject: string;
  description: string;
  createdByName?: string;
}

/** Opens a support ticket on the Liftdesk side (Source=Crm, Status=New). */
export function useCreateTicket() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: CreateTicketInput) => {
      const res = await apiClient.post<ApiResponse<Ticket>>('/tickets', input);
      return res.data.data;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['tickets'] }),
  });
}

export interface UpdateTicketStatusInput {
  id: string;
  status: 'Approved' | 'Rejected';
  decisionNote?: string;
}

/** Approves/rejects a ticket (or re-approves a Failed one). `decidedBy` is derived server-side from the JWT. */
export function useUpdateTicketStatus() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, status, decisionNote }: UpdateTicketStatusInput) => {
      const res = await apiClient.patch<ApiResponse<Ticket>>(`/tickets/${id}/status`, {
        status,
        decisionNote,
      });
      return res.data.data;
    },
    onSuccess: (data) => {
      qc.invalidateQueries({ queryKey: ['tickets'] });
      if (data) qc.setQueryData(['ticket', data.id], data);
    },
  });
}
