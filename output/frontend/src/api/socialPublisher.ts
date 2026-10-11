import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse } from '@/types';

// ── Sosyal medya publisher: hesap bağlama + yayın günlüğü ──────────────────────
// Token'lar yalnız CRM DB'sinde şifreli; bu API token döndürmez. Yayınlama arka planda
// (SocialPublisherService) koşar; buradaki uçlar durum, günlük ve insan kararları içindir.

export type SocialProvider = 'Facebook' | 'Instagram' | 'X';
export type SocialAccountStatus = 'Active' | 'Expiring' | 'Expired' | 'Revoked' | 'Error';
export type SocialPublishJobStatus = 'Pending' | 'InProgress' | 'Succeeded' | 'Failed' | 'Blocked' | 'Skipped';

export interface SocialAccount {
  id: string;
  provider: SocialProvider | string;
  label: string;
  /** tr | en | en-GB | null (eşleme yok → bu hesaba hiç gönderi gitmez). */
  language: string | null;
  externalId: string;
  externalName: string | null;
  parentExternalId: string | null;
  status: SocialAccountStatus | string;
  tokenExpiresAt: string | null;
  hasRefreshToken: boolean;
  scopes: string | null;
  lastError: string | null;
  lastVerifiedAt: string | null;
  isEnabled: boolean;
  connectedBy: string | null;
  createdAt: string;
}

export interface SocialPublisherConfig {
  metaConfigured: boolean;
  xConfigured: boolean;
  publicApiBaseUrl: string;
  publicApiBaseUrlFromConfig: boolean;
  metaRedirectUri: string;
  xRedirectUri: string;
  publishHourLocal: number;
  timeZone: string;
  maxLateDays: number;
  intervalMinutes: number;
  publisherEnabled: boolean;
  tokenKeyConfigured: boolean;
  languages: string[];
}

export interface MetaPendingPage {
  pageId: string;
  name: string;
  instagramId: string | null;
  instagramUsername: string | null;
}

export interface SocialPublishRunSummary {
  startedAt: string;
  finishedAt: string;
  ran: boolean;
  postsSeen: number;
  jobsCreated: number;
  jobsSucceeded: number;
  jobsFailed: number;
  jobsBlocked: number;
  stamped: number;
  notes: string[];
  error: string | null;
}

export interface SocialPublisherWarning { level: 'error' | 'warning' | 'info' | string; message: string }

export interface SocialPublisherStatus {
  enabled: boolean;
  publishHourLocal: number;
  timeZone: string;
  maxLateDays: number;
  intervalMinutes: number;
  counts: { pending: number; inProgress: number; succeeded: number; failed: number; blocked: number; skipped: number; awaitingStamp: number };
  lastRun: SocialPublishRunSummary | null;
  warnings: SocialPublisherWarning[];
}

export interface SocialPublishJob {
  id: string;
  postId: string;
  topic: string | null;
  scheduledFor: string | null;
  kind: string | null;
  accountId: string;
  accountLabel: string;
  provider: SocialProvider | string;
  accountName: string | null;
  language: string;
  status: SocialPublishJobStatus | string;
  attempts: number;
  nextAttemptAt: string | null;
  startedAt: string | null;
  finishedAt: string | null;
  lastError: string | null;
  platformPostId: string | null;
  imageUrl: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface SocialPublishPostSummary {
  postId: string;
  scheduledFor: string;
  topic: string;
  kind: string;
  firstSeenAt: string;
  allDoneAt: string | null;
  stampedAt: string | null;
  stampError: string | null;
  note: string | null;
  jobCount: number;
  succeededCount: number;
  problemCount: number;
}

const ACCOUNTS = ['social-accounts'] as const;
const PUBLISHER = ['social-publisher'] as const;

function useInvalidateAll() {
  const qc = useQueryClient();
  return () => {
    qc.invalidateQueries({ queryKey: [...ACCOUNTS] });
    qc.invalidateQueries({ queryKey: [...PUBLISHER] });
  };
}

// ── Hesaplar ──────────────────────────────────────────────────────────────────

export function useSocialAccounts() {
  return useQuery({
    queryKey: [...ACCOUNTS, 'list'],
    queryFn: async () => (await apiClient.get<ApiResponse<SocialAccount[]>>('/social-accounts')).data.data ?? [],
    staleTime: 30_000,
    retry: 1,
  });
}

export function useSocialPublisherConfig() {
  return useQuery({
    queryKey: [...ACCOUNTS, 'config'],
    queryFn: async () => (await apiClient.get<ApiResponse<SocialPublisherConfig>>('/social-accounts/config')).data.data,
    staleTime: 5 * 60_000,
    retry: 1,
  });
}

/** Meta OAuth başlat — dönen url'e tarayıcı yönlendirilir (tam sayfa). */
export function useMetaConnect() {
  return useMutation({
    mutationFn: async () => (await apiClient.post<ApiResponse<{ url: string; state: string }>>('/social-accounts/meta/connect')).data.data,
  });
}

export function useMetaPending(state: string | null) {
  return useQuery({
    queryKey: [...ACCOUNTS, 'meta-pending', state],
    queryFn: async () =>
      (await apiClient.get<ApiResponse<{ pages: MetaPendingPage[]; userTokenExpiresAt: string | null }>>(`/social-accounts/meta/pending/${state}`)).data.data,
    enabled: !!state,
    retry: false,
    staleTime: 60_000,
  });
}

export interface MetaCompleteInput { state: string; pageId: string; language: string | null; connectFacebook: boolean; connectInstagram: boolean; label?: string }

export function useMetaComplete() {
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: async (input: MetaCompleteInput) =>
      (await apiClient.post<ApiResponse<SocialAccount[]>>('/social-accounts/meta/complete', input)).data.data,
    onSuccess: invalidate,
  });
}

export function useXConnect() {
  return useMutation({
    mutationFn: async () => (await apiClient.post<ApiResponse<{ url: string; state: string }>>('/social-accounts/x/connect')).data.data,
  });
}

export function useUpdateSocialAccount() {
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: async ({ id, ...body }: { id: string; label?: string; language?: string; isEnabled?: boolean }) =>
      (await apiClient.put<ApiResponse<SocialAccount>>(`/social-accounts/${id}`, body)).data.data,
    onSuccess: invalidate,
  });
}

export function useDeleteSocialAccount() {
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: async (id: string) => { await apiClient.delete(`/social-accounts/${id}`); },
    onSuccess: invalidate,
  });
}

export function useVerifySocialAccount() {
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: async (id: string) => (await apiClient.post<ApiResponse<SocialAccount>>(`/social-accounts/${id}/verify`)).data.data,
    onSuccess: invalidate,
  });
}

/** GERÇEK, herkese açık test gönderisi — Instagram için imageUrl zorunlu. */
export function useTestSocialPost() {
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: async ({ id, text, imageUrl }: { id: string; text: string; imageUrl?: string }) =>
      (await apiClient.post<ApiResponse<{ platformPostId: string }>>(`/social-accounts/${id}/test`, { text, imageUrl: imageUrl || null })).data.data,
    onSuccess: invalidate,
  });
}

// ── Publisher ─────────────────────────────────────────────────────────────────

export function usePublisherStatus() {
  return useQuery({
    queryKey: [...PUBLISHER, 'status'],
    queryFn: async () => (await apiClient.get<ApiResponse<SocialPublisherStatus>>('/social-publisher/status')).data.data,
    refetchInterval: 60_000,
    staleTime: 20_000,
    retry: 1,
  });
}

export function usePublishJobs(f: { postId?: string | null; status?: string; limit?: number }, enabled = true) {
  return useQuery({
    queryKey: [...PUBLISHER, 'jobs', f.postId ?? null, f.status ?? '', f.limit ?? 100],
    queryFn: async () =>
      (await apiClient.get<ApiResponse<SocialPublishJob[]>>('/social-publisher/jobs', {
        params: { postId: f.postId || undefined, status: f.status || undefined, limit: f.limit ?? 100 },
      })).data.data ?? [],
    enabled,
    staleTime: 15_000,
    retry: 1,
  });
}

export function usePublishPosts(limit = 50) {
  return useQuery({
    queryKey: [...PUBLISHER, 'posts', limit],
    queryFn: async () => (await apiClient.get<ApiResponse<SocialPublishPostSummary[]>>('/social-publisher/posts', { params: { limit } })).data.data ?? [],
    staleTime: 15_000,
    retry: 1,
  });
}

export function useRunPublisher() {
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: async () => (await apiClient.post<ApiResponse<SocialPublishRunSummary>>('/social-publisher/run')).data.data,
    onSuccess: invalidate,
  });
}

export function useRetryPublishJob() {
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: async (id: string) => (await apiClient.post<ApiResponse<SocialPublishJob>>(`/social-publisher/jobs/${id}/retry`)).data.data,
    onSuccess: invalidate,
  });
}

export function useSkipPublishJob() {
  const invalidate = useInvalidateAll();
  return useMutation({
    mutationFn: async (id: string) => (await apiClient.post<ApiResponse<SocialPublishJob>>(`/social-publisher/jobs/${id}/skip`)).data.data,
    onSuccess: invalidate,
  });
}
