import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse } from '@/types';

// ── Global (TR dışı) lead'ler + e-posta outreach (SuperAdmin) ────────────────

export type GlobalLeadStatus = 'Pending' | 'Approved' | 'Rejected' | 'Duplicate';
export type OutreachStatus = 'NotStarted' | 'Active' | 'Replied' | 'Bounced' | 'Unsubscribed' | 'Completed' | 'Paused' | 'Failed';

export interface GlobalLead {
  id: string;
  country: string;
  region: string | null;
  companyName: string;
  contactName: string | null;
  email: string | null;
  phone: string | null;
  website: string | null;
  address: string | null;
  city: string | null;
  source: string;
  sourceUrl: string | null;
  snippet: string | null;
  confidence: number;
  status: GlobalLeadStatus | string;
  possibleMatchLeadId: string | null;
  matchNotes: string | null;
  agentRunId: string | null;
  reviewedBy: string | null;
  reviewedAt: string | null;
  rejectReason: string | null;
  outreachStatus: OutreachStatus | string;
  outreachStep: number;
  nextSendAt: string | null;
  lastSentAt: string | null;
  sentCount: number;
  senderUserId: string | null;
  repliedAt: string | null;
  bouncedAt: string | null;
  unsubscribedAt: string | null;
  lastError: string | null;
  notes: string | null;
  createdAt: string;
}

export interface GlobalLeadPage { items: GlobalLead[]; totalCount: number; page: number; pageSize: number }

export interface GlobalLeadEmail { id: string; step: number; templateKey: string; subject: string; to: string; sentAt: string; status: 'Sent' | 'Failed' | string; error: string | null; sender: string | null }

export interface GlobalLeadDetail { lead: GlobalLead; sender: string | null; emails: GlobalLeadEmail[] }

export interface OutreachRunSummary { startedAt: string; finishedAt: string; due: number; sent: number; replied: number; bounced: number; failed: number; deferred: number; notes: string[] }

export interface GlobalLeadMeta {
  counts: { pending: number; approved: number; rejected: number; duplicate: number };
  outreach: Record<string, number>;
  countries: { code: string; count: number }[];
  supportedCountries: string[];
  sequence: {
    timeZone: string; startHour: number; endHour: number; weekdaysOnly: boolean;
    steps: { template: string; delayDays: number; attachments: boolean; subjects: string[]; exists: boolean }[];
    attachments: string[]; folder: string;
  };
  limits: { dailyCapPerSender: number; enabled: boolean; intervalMinutes: number };
  sender: { ready: boolean; address: string | null; name: string | null };
  agentKeyConfigured: boolean;
  lastRun: OutreachRunSummary | null;
}

export interface GlobalAgentRun { runId: string; startedAt: string; finishedAt: string | null; country: string; regions: string; found: number; created: number; existing: number; invalid: number; notes: string | null }

const KEY = ['global-leads'] as const;

function useInvalidate() {
  const qc = useQueryClient();
  return () => { qc.invalidateQueries({ queryKey: [...KEY] }); };
}

export interface GlobalLeadFilter { status: GlobalLeadStatus | ''; outreach: OutreachStatus | ''; country: string; search: string; page: number; pageSize?: number }

export function useGlobalLeads(f: GlobalLeadFilter) {
  return useQuery({
    queryKey: [...KEY, 'list', f.status, f.outreach, f.country, f.search, f.page, f.pageSize ?? 25],
    queryFn: async () => (await apiClient.get<ApiResponse<GlobalLeadPage>>('/global-leads', {
      params: { status: f.status || undefined, outreach: f.outreach || undefined, country: f.country || undefined, search: f.search || undefined, page: f.page, pageSize: f.pageSize ?? 25 },
    })).data.data,
    staleTime: 15_000,
    retry: 1,
  });
}

export function useGlobalLeadMeta() {
  return useQuery({
    queryKey: [...KEY, 'meta'],
    queryFn: async () => (await apiClient.get<ApiResponse<GlobalLeadMeta>>('/global-leads/meta')).data.data,
    staleTime: 30_000,
    retry: 1,
  });
}

export function usePendingGlobalLeadCount(enabled = true) {
  return useQuery({
    queryKey: [...KEY, 'pending-count'],
    queryFn: async () => (await apiClient.get<ApiResponse<{ count: number }>>('/global-leads/pending-count')).data.data?.count ?? 0,
    enabled,
    refetchInterval: 300_000,
    retry: 1,
  });
}

export function useGlobalLead(id: string | null) {
  return useQuery({
    queryKey: [...KEY, 'detail', id],
    queryFn: async () => (await apiClient.get<ApiResponse<GlobalLeadDetail>>(`/global-leads/${id}`)).data.data,
    enabled: !!id,
    staleTime: 10_000,
    retry: 1,
  });
}

export function useGlobalAgentRuns(limit = 10) {
  return useQuery({
    queryKey: [...KEY, 'runs', limit],
    queryFn: async () => (await apiClient.get<ApiResponse<GlobalAgentRun[]>>('/global-leads/runs', { params: { limit } })).data.data ?? [],
    staleTime: 60_000,
    retry: 1,
  });
}

export interface UpdateGlobalLeadInput { id: string; companyName?: string; contactName?: string; email?: string; phone?: string; website?: string; city?: string; notes?: string }

export function useUpdateGlobalLead() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ id, ...body }: UpdateGlobalLeadInput) => (await apiClient.put<ApiResponse<GlobalLead>>(`/global-leads/${id}`, body)).data.data,
    onSuccess: invalidate,
  });
}

export function useApproveGlobalLead() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ id, ...body }: { id: string; contactName?: string; email?: string; startOutreach?: boolean }) =>
      (await apiClient.post<ApiResponse<GlobalLead>>(`/global-leads/${id}/approve`, body)).data.data,
    onSuccess: invalidate,
  });
}

export function useApproveManyGlobalLeads() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ ids, startOutreach }: { ids: string[]; startOutreach?: boolean }) =>
      (await apiClient.post<ApiResponse<{ approved: number; errors: string[] }>>('/global-leads/approve-many', { ids, startOutreach: startOutreach ?? true })).data.data,
    onSuccess: invalidate,
  });
}

export function useRejectGlobalLead() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ id, reason }: { id: string; reason?: string }) =>
      (await apiClient.post<ApiResponse<GlobalLead>>(`/global-leads/${id}/reject`, { reason: reason || null })).data.data,
    onSuccess: invalidate,
  });
}

export function useRejectManyGlobalLeads() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ ids, reason }: { ids: string[]; reason?: string }) =>
      (await apiClient.post<ApiResponse<{ rejected: number }>>('/global-leads/reject-many', { ids, reason: reason || null })).data.data,
    onSuccess: invalidate,
  });
}

export function useReopenGlobalLead() {
  const invalidate = useInvalidate();
  return useMutation({ mutationFn: async (id: string) => (await apiClient.post<ApiResponse<GlobalLead>>(`/global-leads/${id}/reopen`)).data.data, onSuccess: invalidate });
}

export type OutreachAction = 'start' | 'pause' | 'resume' | 'send-now' | 'check-inbox';

export function useOutreachAction() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ id, action, restart }: { id: string; action: OutreachAction; restart?: boolean }) =>
      (await apiClient.post<ApiResponse<GlobalLead | { replied: boolean; bounced: boolean; detail: string | null; lead: GlobalLead }>>(`/global-leads/${id}/outreach/${action}`, action === 'start' ? { restart: restart ?? false } : undefined)).data.data,
    onSuccess: invalidate,
  });
}

export function useStopOutreach() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ id, reason }: { id: string; reason: 'replied' | 'unsubscribed' | 'bounced' | 'completed' }) =>
      (await apiClient.post<ApiResponse<GlobalLead>>(`/global-leads/${id}/outreach/stop`, { reason })).data.data,
    onSuccess: invalidate,
  });
}

export function useRunOutreachCycle() {
  const invalidate = useInvalidate();
  return useMutation({ mutationFn: async () => (await apiClient.post<ApiResponse<OutreachRunSummary>>('/global-leads/outreach/run')).data.data, onSuccess: invalidate });
}
