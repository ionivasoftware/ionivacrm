import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse } from '@/types';

// ── Liftdesk kurumsal site blog kuyruğu (docs/crm-site-blog-api.md, Liftdesk deposu) ──────────
// Ajan haftada bir konu yazar (her yerel ayar için bir varyant, Pending), onay burada verilir,
// yayını EMS tetikler (site yeniden derlenir). Yerel ayar listesi SABİT DEĞİL: GET locales.

export type SiteBlogVariantStatus = 'Pending' | 'Approved' | 'Published' | 'Rejected' | 'Retired';
export type SiteBlogSource = 'Queue' | 'Rotation' | 'Manual';
export type TopicRequestStatus = 'Open' | 'Taken' | 'Done' | 'Dropped';

export interface SiteLocale {
  key: string; displayName: string; language: string; spelling: string; country: string | null; lang: string;
  home: string; blogPath: string; faqHeading: string; howItWorksHeading: string; writerEnabled: boolean;
}

export interface SiteBlogVariant {
  id: string; locale: string; slug: string; url: string; title: string; description: string;
  /** Listede null, detayda dolu. */
  bodyMarkdown: string | null;
  relatedPageSlug: string | null;
  /** yyyy-MM-dd */
  publishDate: string; updatedDate: string | null; words: number;
  status: SiteBlogVariantStatus | string;
  approvedAt: string | null; rejectedAt: string | null; rejectReason: string | null; publishedAt: string | null; liveAt: string | null; updatedAt: string;
}

export interface SiteBlogPost {
  id: string; group: string; topic: string; source: SiteBlogSource | string; topicRequestId: string | null;
  sourceUrls: string[] | null; createdAt: string; overallStatus: SiteBlogVariantStatus | string; variants: SiteBlogVariant[] | null;
}

export interface SiteBlogPage { items: SiteBlogPost[] | null; totalCount: number; page: number; pageSize: number }

export interface TopicRequest { id: string; topic: string; group: string | null; note: string | null; status: TopicRequestStatus | string; takenByPostId: string | null; createdAt: string }

export interface PublishResult { triggered: boolean; reason: string | null; awaitingPublish: number; triggeredAt: string | null }

export interface PublishStatus {
  triggerConfigured: boolean; lastTriggeredAt: string | null; lastTriggerOutcome: string | null; awaitingPublish: number;
  awaitingLive: { variantId: string; locale: string; slug: string; url: string; publishedAt: string; hoursWaiting: number }[] | null;
  warnings: string[] | null;
}

/** Konu kuyruğundaki `group` = çözüm sayfası grubu (yalnız `related` seçimine yarar). Sabit liste (sözleşme §5.5). */
export const SOLUTION_GROUPS = ['maintenance-tracking', 'digital-service-form', 'fault-management', 'contracts', 'quotes', 'qr-labels', 'accounting'] as const;

const KEY = ['site-blog'] as const;

function useInvalidate() {
  const qc = useQueryClient();
  return () => { qc.invalidateQueries({ queryKey: [...KEY] }); };
}

export function useSiteLocales(enabled = true) {
  return useQuery({
    queryKey: [...KEY, 'locales'],
    queryFn: async () => (await apiClient.get<ApiResponse<SiteLocale[]>>('/site-blog/locales')).data.data ?? [],
    enabled, staleTime: 10 * 60_000, retry: 1,
  });
}

export interface SiteBlogFilter { status: SiteBlogVariantStatus | ''; locale: string; page: number; pageSize?: number }

export function useSiteBlogPosts(f: SiteBlogFilter) {
  return useQuery({
    queryKey: [...KEY, 'list', f.status, f.locale, f.page, f.pageSize ?? 20],
    queryFn: async () => (await apiClient.get<ApiResponse<SiteBlogPage>>('/site-blog', {
      params: { status: f.status || undefined, locale: f.locale || undefined, page: f.page, pageSize: f.pageSize ?? 20 },
    })).data.data,
    staleTime: 15_000, retry: 1,
  });
}

export function useSiteBlogPost(id: string | null) {
  return useQuery({
    queryKey: [...KEY, 'detail', id],
    queryFn: async () => (await apiClient.get<ApiResponse<SiteBlogPost>>(`/site-blog/${id}`)).data.data,
    enabled: !!id, staleTime: 10_000, retry: 1,
  });
}

export function usePendingSiteBlogCount(enabled = true) {
  return useQuery({
    queryKey: [...KEY, 'pending-count'],
    queryFn: async () => (await apiClient.get<ApiResponse<{ count: number }>>('/site-blog/pending-count')).data.data?.count ?? 0,
    enabled, refetchInterval: 300_000, retry: 1,
  });
}

export interface UpdateVariantInput { variantId: string; title?: string; description?: string; slug?: string; bodyMarkdown?: string; relatedPageSlug?: string; publishDate?: string }

export function useUpdateSiteBlogVariant() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ variantId, ...body }: UpdateVariantInput) => (await apiClient.put<ApiResponse<SiteBlogPost>>(`/site-blog/variants/${variantId}`, body)).data.data,
    onSuccess: invalidate,
  });
}

export type VariantAction = 'approve' | 'retire';

export function useSiteBlogVariantAction() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ variantId, action }: { variantId: string; action: VariantAction }) => (await apiClient.post<ApiResponse<SiteBlogPost>>(`/site-blog/variants/${variantId}/${action}`)).data.data,
    onSuccess: invalidate,
  });
}

export function useRejectSiteBlogVariant() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ variantId, reason }: { variantId: string; reason?: string }) => (await apiClient.post<ApiResponse<SiteBlogPost>>(`/site-blog/variants/${variantId}/reject`, { reason: reason || null })).data.data,
    onSuccess: invalidate,
  });
}

export function useApproveAllSiteBlog() {
  const invalidate = useInvalidate();
  return useMutation({ mutationFn: async (postId: string) => (await apiClient.post<ApiResponse<SiteBlogPost>>(`/site-blog/${postId}/approve-all`)).data.data, onSuccess: invalidate });
}

export function useDeleteSiteBlogPost() {
  const invalidate = useInvalidate();
  return useMutation({ mutationFn: async (postId: string) => (await apiClient.delete<ApiResponse<{ deleted: boolean }>>(`/site-blog/${postId}`)).data.data, onSuccess: invalidate });
}

export function usePublishStatus(enabled = true) {
  return useQuery({
    queryKey: [...KEY, 'publish-status'],
    queryFn: async () => (await apiClient.get<ApiResponse<PublishStatus>>('/site-blog/publish-status')).data.data,
    enabled, staleTime: 30_000, refetchInterval: 120_000, retry: 1,
  });
}

export function usePublishSite() {
  const invalidate = useInvalidate();
  return useMutation({ mutationFn: async () => (await apiClient.post<ApiResponse<PublishResult>>('/site-blog/publish')).data.data, onSuccess: invalidate });
}

export function useTopicRequests(status: TopicRequestStatus | '' = '', enabled = true) {
  return useQuery({
    queryKey: [...KEY, 'topic-requests', status],
    queryFn: async () => (await apiClient.get<ApiResponse<TopicRequest[]>>('/site-blog/topic-requests', { params: { status: status || undefined } })).data.data ?? [],
    enabled, staleTime: 30_000, retry: 1,
  });
}

export function useCreateTopicRequest() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async (body: { topic: string; group?: string; note?: string }) => (await apiClient.post<ApiResponse<TopicRequest>>('/site-blog/topic-requests', body)).data.data,
    onSuccess: invalidate,
  });
}

export function useDropTopicRequest() {
  const invalidate = useInvalidate();
  return useMutation({ mutationFn: async (id: string) => (await apiClient.delete<ApiResponse<TopicRequest>>(`/site-blog/topic-requests/${id}`)).data.data, onSuccess: invalidate });
}
