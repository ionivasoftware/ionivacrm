import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse } from '@/types';

// ── Kullanıcının Gmail'i ile tanıtım maili ────────────────────────────────────

export interface MailSettings { address: string | null; senderName: string | null; bccSelf: boolean; hasPassword: boolean; configured: boolean }

export interface MarketingTemplate { key: string; fileName: string; subjects: string[]; language: string | null }
export interface MarketingAttachment { fileName: string; sizeBytes: number }
export interface MarketingMaterials { templates: MarketingTemplate[]; attachments: MarketingAttachment[] }

export interface MailSendResult { success: boolean; message: string; to: string | null; subject: string | null; attachments: string[] | null }

const KEY = ['marketing'] as const;

export function useMailSettings() {
  return useQuery({
    queryKey: [...KEY, 'mail-settings'],
    queryFn: async () => (await apiClient.get<ApiResponse<MailSettings>>('/marketing/mail-settings')).data.data,
    staleTime: 60_000,
    retry: 1,
  });
}

export function useSaveMailSettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: { address: string; password?: string; senderName?: string; bccSelf: boolean; allowNonGmail?: boolean }) =>
      (await apiClient.put<ApiResponse<MailSettings>>('/marketing/mail-settings', body)).data.data,
    onSuccess: () => qc.invalidateQueries({ queryKey: [...KEY] }),
  });
}

export function useClearMailSettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => (await apiClient.delete<ApiResponse<MailSettings>>('/marketing/mail-settings')).data.data,
    onSuccess: () => qc.invalidateQueries({ queryKey: [...KEY] }),
  });
}

export function useTestMail() {
  return useMutation({
    mutationFn: async () => (await apiClient.post<ApiResponse<MailSendResult>>('/marketing/mail-settings/test')).data.data,
  });
}

export function useMarketingMaterials(enabled = true) {
  return useQuery({
    queryKey: [...KEY, 'materials'],
    queryFn: async () => (await apiClient.get<ApiResponse<MarketingMaterials>>('/marketing/materials')).data.data,
    enabled,
    staleTime: 5 * 60_000,
    retry: 1,
  });
}

export function useSendIntroEmail() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ customerId, ...body }: { customerId: string; templateKey?: string; subjectIndex?: number; note?: string; to?: string }) =>
      (await apiClient.post<ApiResponse<MailSendResult>>(`/marketing/customers/${customerId}/intro-email`, body)).data.data,
    onSuccess: (_r, v) => { qc.invalidateQueries({ queryKey: ['contactHistory', v.customerId] }); qc.invalidateQueries({ queryKey: ['dashboard'] }); },
  });
}
