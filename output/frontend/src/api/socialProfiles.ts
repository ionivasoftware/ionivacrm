import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse } from '@/types';

// ── Sosyal hesap PROFİL metinleri ve görselleri ───────────────────────────────
// Varsayılanlar sunucuda; operatör düzenler. Facebook alanları Pages API ile sayfaya basılır
// (pages_manage_metadata), Instagram'ın profil yazma API'si yok → kopyala + "Uyguladım".

export type SocialProfileProvider = 'Facebook' | 'Instagram';

export interface SocialProfile {
  provider: SocialProfileProvider | string;
  language: string;
  fields: Record<string, string>;
  /** Henüz kayıt yok, ekranda görünen sunucu varsayılanı. */
  isDefault: boolean;
  profileImageUrl: string | null;
  coverImageUrl: string | null;
  appliedAt: string | null;
  applyError: string | null;
  updatedAt: string | null;
  updatedBy: string | null;
  /** Facebook + bu dile eşlenmiş etkin sayfa bağlı → "Facebook'a uygula" mümkün. */
  canApply: boolean;
  connectedAccountLabel: string | null;
  /** API ile sayfaya basılan anahtarlar (Facebook: about, description, website, phone, email). */
  pushableKeys: string[];
}

const KEY = ['social-profiles'] as const;

function useInvalidate() {
  const qc = useQueryClient();
  return () => qc.invalidateQueries({ queryKey: [...KEY] });
}

export function useSocialProfiles(language = 'tr') {
  return useQuery({
    queryKey: [...KEY, language],
    queryFn: async () => (await apiClient.get<ApiResponse<SocialProfile[]>>('/social-profiles', { params: { language } })).data.data ?? [],
    staleTime: 30_000,
    retry: 1,
  });
}

export function useUpdateSocialProfile() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ provider, language, fields }: { provider: string; language: string; fields: Record<string, string> }) =>
      (await apiClient.put<ApiResponse<SocialProfile>>(`/social-profiles/${provider.toLowerCase()}`, { fields }, { params: { language } })).data.data,
    onSuccess: invalidate,
  });
}

export function useResetSocialProfile() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ provider, language }: { provider: string; language: string }) =>
      (await apiClient.post<ApiResponse<SocialProfile>>(`/social-profiles/${provider.toLowerCase()}/reset`, null, { params: { language } })).data.data,
    onSuccess: invalidate,
  });
}

export function useUploadSocialProfileImage() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ provider, language, slot, file }: { provider: string; language: string; slot: 'profile' | 'cover'; file: File }) => {
      const form = new FormData();
      form.append('file', file);
      return (await apiClient.post<ApiResponse<SocialProfile>>(`/social-profiles/${provider.toLowerCase()}/images/${slot}`, form, {
        params: { language }, headers: { 'Content-Type': 'multipart/form-data' },
      })).data.data;
    },
    onSuccess: invalidate,
  });
}

export function useDeleteSocialProfileImage() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ provider, language, slot }: { provider: string; language: string; slot: 'profile' | 'cover' }) =>
      (await apiClient.delete<ApiResponse<SocialProfile>>(`/social-profiles/${provider.toLowerCase()}/images/${slot}`, { params: { language } })).data.data,
    onSuccess: invalidate,
  });
}

/** Facebook: hakkında/açıklama/web/telefon/e-posta + profil fotoğrafını sayfaya basar. */
export function useApplySocialProfile() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ provider, language }: { provider: string; language: string }) =>
      (await apiClient.post<ApiResponse<SocialProfile>>(`/social-profiles/${provider.toLowerCase()}/apply`, null, { params: { language } })).data.data,
    onSuccess: invalidate,
    onError: invalidate,
  });
}

/** Elle uyguladım (Instagram; Facebook kapak için de). */
export function useMarkSocialProfileApplied() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ provider, language }: { provider: string; language: string }) =>
      (await apiClient.post<ApiResponse<SocialProfile>>(`/social-profiles/${provider.toLowerCase()}/mark-applied`, null, { params: { language } })).data.data,
    onSuccess: invalidate,
  });
}
