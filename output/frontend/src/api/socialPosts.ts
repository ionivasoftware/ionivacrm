import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse } from '@/types';

// ── Liftdesk sosyal medya gönderi kuyruğu (docs/crm-social-media-api.md, Liftdesk deposu) ──────
// Ajan taslak üretir (Pending), onay burada verilir, YAYINLAMA ayrı iş (publisher). Enum adları
// Liftdesk ile birebir; bilinmeyen değer gelirse ekran kırılmasın diye tipler string'e açık.

export type SocialPostKind = 'News' | 'Info' | 'Product' | 'Holiday' | 'Commemoration';
export type SocialPostStatus = 'Pending' | 'Approved' | 'Rejected' | 'Published';
export type SocialPostLanguage = 'tr' | 'en' | 'en-GB';

/** Sözleşme sırası: tr → en → en-GB (en-GB "lift", en "elevator"). */
export const SOCIAL_LANGUAGES: SocialPostLanguage[] = ['tr', 'en', 'en-GB'];

export interface SocialPostVariant {
  language: string;
  /** Şablon kartın başlığı (≤ 80). */
  cardTitle: string;
  /** Facebook/Instagram metni — hashtag'siz (≤ 4000). */
  body: string;
  /** X metni — hashtag DAHİL ≤ 280. */
  bodyShort: string;
  hashtags: string;
}

export interface SocialPost {
  id: string;
  kind: SocialPostKind | string;
  status: SocialPostStatus | string;
  slotKey: string;
  /** Yayın GÜNÜ, 00:00Z — saat politikası publisher'da. */
  scheduledFor: string;
  topic: string;
  holidayName: string | null;
  /** Satır başına bir URL (yalnız News). */
  sourceUrls: string | null;
  /** Doluysa publisher bunu kullanır; boşsa şablon kart. */
  customImageUrl: string | null;
  approvedAt: string | null;
  rejectedAt: string | null;
  rejectReason: string | null;
  publishedAt: string | null;
  createdAt: string;
  updatedAt: string;
  variants: SocialPostVariant[] | null;
}

export interface SocialPostPage {
  items: SocialPost[] | null;
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface SocialPostFilters {
  status: SocialPostStatus | '';
  from: string;
  to: string;
  page: number;
  pageSize: number;
}

const KEY = ['social-posts'] as const;

export function useSocialPosts(f: SocialPostFilters) {
  return useQuery({
    queryKey: [...KEY, 'list', f.status, f.from, f.to, f.page, f.pageSize],
    queryFn: async () => {
      const r = await apiClient.get<ApiResponse<SocialPostPage>>('/social-posts', {
        params: {
          status: f.status || undefined,
          from: f.from || undefined,
          to: f.to || undefined,
          page: f.page,
          pageSize: f.pageSize,
        },
      });
      return r.data.data;
    },
    staleTime: 30 * 1000,
    retry: 1,
  });
}

export function useSocialPost(id: string | null) {
  return useQuery({
    queryKey: [...KEY, 'detail', id],
    queryFn: async () => {
      const r = await apiClient.get<ApiResponse<SocialPost>>(`/social-posts/${id}`);
      return r.data.data;
    },
    enabled: !!id,
    staleTime: 15 * 1000,
    retry: 1,
  });
}

/**
 * Menü rozeti — bekleyen taslak sayısı. `enabled` SuperAdmin dışı kullanıcıda false olmalı (uç
 * SuperAdmin'e kısıtlı, 403 döner). Liftdesk erişilemezse sunucu 0 döner; gürültü yapmaz.
 */
export function usePendingSocialPostCount(enabled = true) {
  return useQuery({
    queryKey: [...KEY, 'pending-count'],
    queryFn: async () => {
      const r = await apiClient.get<ApiResponse<{ count: number }>>('/social-posts/pending-count');
      return r.data.data?.count ?? 0;
    },
    enabled,
    refetchInterval: 300_000,
    retry: 1,
  });
}

/**
 * Şablon kart PNG'si — <img src> Bearer taşıyamadığından blob olarak çekilir; bileşen object URL
 * üretir. Anahtar post'un updatedAt'ini içerir: başlık değişince kart da yenilenir.
 */
export function useSocialPostCard(id: string, lang: string, version: string, enabled = true) {
  return useQuery({
    queryKey: [...KEY, 'card', id, lang, version],
    queryFn: async () => {
      const r = await apiClient.get<Blob>(`/social-posts/${id}/card`, {
        params: { lang },
        responseType: 'blob',
      });
      return r.data;
    },
    enabled,
    staleTime: 5 * 60 * 1000,
    retry: 1,
  });
}

function useInvalidate() {
  const qc = useQueryClient();
  return () => qc.invalidateQueries({ queryKey: [...KEY] });
}

export interface UpdateSocialPostInput {
  id: string;
  topic?: string;
  /** "" → şablon karta dön. */
  customImageUrl?: string;
  /** Yalnız değişen diller; gönderilmeyen dil olduğu gibi kalır. */
  variants?: SocialPostVariant[];
}

export function useUpdateSocialPost() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ id, ...body }: UpdateSocialPostInput) => {
      const r = await apiClient.put<ApiResponse<SocialPost>>(`/social-posts/${id}`, body);
      return r.data.data;
    },
    onSuccess: invalidate,
  });
}

export function useApproveSocialPost() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async (id: string) => {
      const r = await apiClient.post<ApiResponse<SocialPost>>(`/social-posts/${id}/approve`);
      return r.data.data;
    },
    onSuccess: invalidate,
  });
}

export function useRejectSocialPost() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ id, reason }: { id: string; reason?: string }) => {
      const r = await apiClient.post<ApiResponse<SocialPost>>(`/social-posts/${id}/reject`, { reason: reason || null });
      return r.data.data;
    },
    onSuccess: invalidate,
  });
}

export function useDeleteSocialPost() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async (id: string) => {
      await apiClient.delete<ApiResponse<{ deleted: boolean }>>(`/social-posts/${id}`);
    },
    onSuccess: invalidate,
  });
}

/** "Kendi görselini yükle" — dosya CRM'de saklanır, Liftdesk'e URL yazılır. PNG/JPEG/WebP ≤ 5 MB. */
export function useUploadSocialPostImage() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ id, file }: { id: string; file: File }) => {
      const form = new FormData();
      form.append('file', file);
      const r = await apiClient.post<ApiResponse<SocialPost>>(`/social-posts/${id}/image`, form, {
        headers: { 'Content-Type': 'multipart/form-data' },
      });
      return r.data.data;
    },
    onSuccess: invalidate,
  });
}
