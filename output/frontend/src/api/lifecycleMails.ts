import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse } from '@/types';

// ── Demo yaşam döngüsü e-postaları (TR dışı Liftdesk deneme hesapları, SuperAdmin) ──────────
// Hoş geldin → nasıl gidiyor → deneme bitti hatırlatması; dil müşterinin ülkesinden, gönderen firma
// destek posta kutusu (Railway LifecycleMail__Smtp__*). Zamanlayıcı saatlik; buradan izleme + elle gönderim.

export type LifecycleStep = 'Welcome' | 'CheckIn' | 'TrialEnded';

export interface LifecycleOptions { checkInAfterDays: number; trialEndedAfterDays: number; maxAgeDays: number; sendHourStart: number; sendHourEnd: number; excludeCountries: string }

export interface LifecycleRunSummary { startedAt: string; finishedAt: string; candidates: number; sent: number; failed: number; deferred: number; notes: string[] }

export interface LifecycleStatus {
  enabled: boolean; smtpConfigured: boolean; from: string | null;
  /** Hangi ayar bloğundan: 'LifecycleMail' | 'EmailCollector' (destek kutusu) | 'Notifications' */
  smtpSource: string | null; folder: string | null; languages: string[];
  options: LifecycleOptions; sentByStep: Record<string, number>; lastRun: LifecycleRunSummary | null;
}

export interface LifecycleCandidate {
  customerId: string; companyName: string; email: string | null; country: string | null; language: string; status: string;
  createdAt: string; expirationDate: string | null; dueStep: LifecycleStep | null; skipReason: string | null; sentSteps: LifecycleStep[]; inWindow: boolean;
}

export interface LifecycleMailRow {
  id: string; customerId: string; companyName: string; country: string | null; customerStatus: string | null; step: LifecycleStep | string;
  language: string; to: string; subject: string; sentAt: string; status: 'Sent' | 'Failed' | string; error: string | null; triggeredBy: string;
}

export interface LifecycleMailPage { items: LifecycleMailRow[]; totalCount: number; page: number; pageSize: number }

export interface LifecycleTestResult { error: string | null; to: string; language: string; subject: string | null; from: string | null; smtpSource: string | null }

const KEY = ['lifecycle-mails'] as const;

function useInvalidate() {
  const qc = useQueryClient();
  return () => { qc.invalidateQueries({ queryKey: [...KEY] }); };
}

export function useLifecycleStatus() {
  return useQuery({ queryKey: [...KEY, 'status'], queryFn: async () => (await apiClient.get<ApiResponse<LifecycleStatus>>('/lifecycle-mails/status')).data.data, staleTime: 30_000, retry: 1 });
}

export function useLifecycleCandidates() {
  return useQuery({ queryKey: [...KEY, 'candidates'], queryFn: async () => (await apiClient.get<ApiResponse<LifecycleCandidate[]>>('/lifecycle-mails/candidates')).data.data ?? [], staleTime: 30_000, retry: 1 });
}

export function useLifecycleMails(step: LifecycleStep | '', page: number) {
  return useQuery({
    queryKey: [...KEY, 'list', step, page],
    queryFn: async () => (await apiClient.get<ApiResponse<LifecycleMailPage>>('/lifecycle-mails', { params: { step: step || undefined, page, pageSize: 30 } })).data.data,
    staleTime: 15_000, retry: 1,
  });
}

export function useLifecyclePreview(customerId: string | null, step: LifecycleStep | null) {
  return useQuery({
    queryKey: [...KEY, 'preview', customerId, step],
    queryFn: async () => (await apiClient.get<ApiResponse<{ subject: string; html: string }>>('/lifecycle-mails/preview', { params: { customerId, step } })).data.data,
    enabled: !!customerId && !!step, staleTime: 60_000, retry: 0,
  });
}

export function useSendLifecycleMail() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ customerId, step, force }: { customerId: string; step: LifecycleStep; force?: boolean }) =>
      (await apiClient.post<ApiResponse<{ sent: boolean }>>(`/lifecycle-mails/customers/${customerId}/send`, { step, force: force ?? false })).data.data,
    onSuccess: invalidate,
  });
}

/** Şablonu örnek verilerle verilen adrese gönderir ("[TEST]" konu). Müşteri kaydı tutulmaz — SMTP ve şablon kontrolü. */
export function useSendLifecycleTest() {
  return useMutation({
    mutationFn: async ({ to, step, language }: { to: string; step: LifecycleStep; language?: string }) =>
      (await apiClient.post<ApiResponse<LifecycleTestResult>>('/lifecycle-mails/test', { to, step, language })).data.data,
  });
}

export function useRunLifecycleCycle() {
  const invalidate = useInvalidate();
  return useMutation({ mutationFn: async () => (await apiClient.post<ApiResponse<LifecycleRunSummary>>('/lifecycle-mails/run')).data.data, onSuccess: invalidate });
}
