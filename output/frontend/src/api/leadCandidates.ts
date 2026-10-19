import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse } from '@/types';

// ── Lead ajanı aday kuyruğu (SuperAdmin) ─────────────────────────────────────

export type LeadCandidateStatus = 'Pending' | 'Approved' | 'Rejected' | 'Duplicate';

export interface LeadCandidate {
  id: string;
  projectId: string;
  companyName: string;
  legalTitle: string | null;
  phone: string | null;
  email: string | null;
  website: string | null;
  address: string | null;
  city: string | null;
  district: string | null;
  taxNumber: string | null;
  mersisNo: string | null;
  source: string;
  sourceUrl: string | null;
  snippet: string | null;
  confidence: number;
  status: LeadCandidateStatus | string;
  duplicateOfCustomerId: string | null;
  possibleMatchCustomerId: string | null;
  matchNotes: string | null;
  agentRunId: string | null;
  searchCity: string | null;
  reviewedBy: string | null;
  reviewedAt: string | null;
  rejectReason: string | null;
  createdCustomerId: string | null;
  createdAt: string;
}

export interface LeadCandidatePage { items: LeadCandidate[]; totalCount: number; page: number; pageSize: number }

export interface LeadMeta {
  counts: { pending: number; approved: number; rejected: number; duplicate: number };
  projects: { id: string; name: string }[];
  defaultProjectId: string | null;
  agentKeyConfigured: boolean;
}

export interface LeadAgentRun {
  runId: string; startedAt: string; finishedAt: string | null; cities: string;
  found: number; created: number; duplicates: number; existing: number; invalid: number; notes: string | null;
}

const KEY = ['lead-candidates'] as const;

function useInvalidate() {
  const qc = useQueryClient();
  return () => { qc.invalidateQueries({ queryKey: [...KEY] }); qc.invalidateQueries({ queryKey: ['customers'] }); };
}

export function useLeadCandidates(f: { status: LeadCandidateStatus | ''; search: string; page: number; pageSize?: number }) {
  return useQuery({
    queryKey: [...KEY, 'list', f.status, f.search, f.page, f.pageSize ?? 25],
    queryFn: async () => (await apiClient.get<ApiResponse<LeadCandidatePage>>('/lead-candidates', {
      params: { status: f.status || undefined, search: f.search || undefined, page: f.page, pageSize: f.pageSize ?? 25 },
    })).data.data,
    staleTime: 15_000,
    retry: 1,
  });
}

export function useLeadMeta() {
  return useQuery({
    queryKey: [...KEY, 'meta'],
    queryFn: async () => (await apiClient.get<ApiResponse<LeadMeta>>('/lead-candidates/meta')).data.data,
    staleTime: 30_000,
    retry: 1,
  });
}

export function usePendingLeadCount(enabled = true) {
  return useQuery({
    queryKey: [...KEY, 'pending-count'],
    queryFn: async () => (await apiClient.get<ApiResponse<{ count: number }>>('/lead-candidates/pending-count')).data.data?.count ?? 0,
    enabled,
    refetchInterval: 300_000,
    retry: 1,
  });
}

export function useLeadAgentRuns(limit = 10) {
  return useQuery({
    queryKey: [...KEY, 'runs', limit],
    queryFn: async () => (await apiClient.get<ApiResponse<LeadAgentRun[]>>('/lead-candidates/runs', { params: { limit } })).data.data ?? [],
    staleTime: 60_000,
    retry: 1,
  });
}

export interface ApproveLeadInput {
  id: string; projectId?: string; companyName?: string; contactName?: string; phone?: string; email?: string; address?: string; taxNumber?: string; label?: string; segment?: string;
}

export function useApproveLead() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ id, ...body }: ApproveLeadInput) =>
      (await apiClient.post<ApiResponse<{ candidate: LeadCandidate; customerId: string }>>(`/lead-candidates/${id}/approve`, body)).data.data,
    onSuccess: invalidate,
  });
}

export function useRejectLead() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ id, reason }: { id: string; reason?: string }) =>
      (await apiClient.post<ApiResponse<LeadCandidate>>(`/lead-candidates/${id}/reject`, { reason: reason || null })).data.data,
    onSuccess: invalidate,
  });
}

export function useRejectManyLeads() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ ids, reason }: { ids: string[]; reason?: string }) =>
      (await apiClient.post<ApiResponse<{ rejected: number }>>('/lead-candidates/reject-many', { ids, reason: reason || null })).data.data,
    onSuccess: invalidate,
  });
}

export function useMarkLeadDuplicate() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ id, customerId }: { id: string; customerId: string }) =>
      (await apiClient.post<ApiResponse<LeadCandidate>>(`/lead-candidates/${id}/duplicate`, { customerId })).data.data,
    onSuccess: invalidate,
  });
}

export function useReopenLead() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async (id: string) => (await apiClient.post<ApiResponse<LeadCandidate>>(`/lead-candidates/${id}/reopen`)).data.data,
    onSuccess: invalidate,
  });
}
