import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import { useAuthStore } from '@/stores/authStore';
import type { ApiResponse, ContactType } from '@/types';

// ── Görüşme Takvimi: randevular + CSO planı ──────────────────────────────────

export type AppointmentKind = 'NewCall' | 'FollowUpCall' | 'SatisfactionCall' | 'Meeting' | 'Training' | 'Demo' | 'Other';
export type AppointmentSource = 'Manual' | 'Agent';
export type AppointmentStatus = 'Planned' | 'Completed' | 'Skipped' | 'NoAnswer';

export const CALL_KINDS: AppointmentKind[] = ['NewCall', 'FollowUpCall', 'SatisfactionCall'];

export const KIND_LABEL: Record<AppointmentKind, string> = {
  NewCall: 'Yeni arama',
  FollowUpCall: 'Takip araması',
  SatisfactionCall: 'Memnuniyet araması',
  Meeting: 'Toplantı',
  Training: 'Eğitim',
  Demo: 'Demo',
  Other: 'Diğer',
};

export const STATUS_LABEL: Record<AppointmentStatus, string> = {
  Planned: 'Planlı',
  Completed: 'Yapıldı',
  Skipped: 'Atlandı',
  NoAnswer: 'Ulaşılamadı',
};

export interface Appointment {
  id: string;
  projectId: string;
  customerId: string;
  customerName: string;
  customerPhone: string | null;
  customerContactName: string | null;
  customerStatus: string;
  customerLabel: string | null;
  customerLegacyId: string | null;
  kind: AppointmentKind | string;
  source: AppointmentSource | string;
  status: AppointmentStatus | string;
  /** UTC ISO. */
  scheduledAt: string;
  durationMinutes: number;
  title: string | null;
  note: string | null;
  /** Planlayıcı gerekçesi ("Lead · Yüksek potansiyel · 45 gün görüşülmedi"). */
  reason: string | null;
  score: number;
  assignedUserId: string | null;
  assignedUserName: string | null;
  contactHistoryId: string | null;
  completedAt: string | null;
  skipReason: string | null;
  createdAt: string;
}

export interface CallPlanStatus {
  newCallsPerDay: number;
  followUpsPerDay: number;
  satisfactionPerDay: number;
  horizonBusinessDays: number;
  timeZone: string;
  runHourLocal: number;
  enabled: boolean;
  lastPlanDate: string | null;
  lastRanAt: string | null;
  lastCreatedCount: number | null;
  lastSummary: string | null;
  canRun: boolean;
}

export interface CallPlanRunResult {
  projectId: string;
  planDate: string;
  created: number;
  alreadyRan: boolean;
  summary: string;
}

const KEY = ['appointments'] as const;

function useInvalidate() {
  const qc = useQueryClient();
  return () => {
    qc.invalidateQueries({ queryKey: [...KEY] });
    qc.invalidateQueries({ queryKey: ['contactHistory'] });
    qc.invalidateQueries({ queryKey: ['dashboard'] });
  };
}

export interface AppointmentFilters {
  /** yyyy-MM-dd yerel gün, dâhil. */
  from: string;
  to: string;
  assignedUserId?: string | null;
  status?: AppointmentStatus | '';
  kind?: AppointmentKind | '';
}

export function useAppointments(f: AppointmentFilters, enabled = true) {
  const projectId = useAuthStore((s) => s.currentProjectId);
  return useQuery({
    queryKey: [...KEY, 'list', projectId, f.from, f.to, f.assignedUserId ?? null, f.status ?? '', f.kind ?? ''],
    queryFn: async () =>
      (await apiClient.get<ApiResponse<Appointment[]>>('/appointments', {
        params: {
          from: f.from, to: f.to, projectId: projectId || undefined,
          assignedUserId: f.assignedUserId || undefined, status: f.status || undefined, kind: f.kind || undefined,
        },
      })).data.data ?? [],
    enabled,
    staleTime: 15_000,
    retry: 1,
  });
}

export function useCustomerAppointments(customerId: string | null, limit = 20) {
  return useQuery({
    queryKey: [...KEY, 'customer', customerId, limit],
    queryFn: async () => (await apiClient.get<ApiResponse<Appointment[]>>(`/appointments/customer/${customerId}`, { params: { limit } })).data.data ?? [],
    enabled: !!customerId,
    staleTime: 15_000,
    retry: 1,
  });
}

export interface CreateAppointmentInput {
  customerId: string;
  kind: AppointmentKind;
  /** UTC ISO. */
  scheduledAt: string;
  durationMinutes?: number;
  title?: string;
  note?: string;
  assignedUserId?: string | null;
}

export function useCreateAppointment() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async (input: CreateAppointmentInput) => (await apiClient.post<ApiResponse<Appointment>>('/appointments', input)).data.data,
    onSuccess: invalidate,
  });
}

export function useUpdateAppointment() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ id, ...body }: { id: string; kind?: AppointmentKind; scheduledAt?: string; durationMinutes?: number; title?: string; note?: string; assignedUserId?: string | null; status?: 'Planned' }) =>
      (await apiClient.put<ApiResponse<Appointment>>(`/appointments/${id}`, body)).data.data,
    onSuccess: invalidate,
  });
}

export interface CompleteAppointmentInput {
  id: string;
  type: ContactType;
  subject?: string;
  content?: string;
  outcome?: string;
  reached: boolean;
  satisfactionScore?: number | null;
  /** Söz verilen takip (UTC ISO) → o güne takip randevusu açılır. */
  nextContactAt?: string | null;
  /** Ulaşılamadı ise yeniden planlama zamanı (UTC ISO). */
  rescheduleAt?: string | null;
  contactedAt?: string;
}

export interface CompleteAppointmentResult { appointment: Appointment; contactHistoryId: string; followUp: Appointment | null }

export function useCompleteAppointment() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ id, ...body }: CompleteAppointmentInput) =>
      (await apiClient.post<ApiResponse<CompleteAppointmentResult>>(`/appointments/${id}/complete`, body)).data.data,
    onSuccess: invalidate,
  });
}

export function useSkipAppointment() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ id, reason }: { id: string; reason?: string }) =>
      (await apiClient.post<ApiResponse<Appointment>>(`/appointments/${id}/skip`, { reason: reason || null })).data.data,
    onSuccess: invalidate,
  });
}

export function useDeleteAppointment() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async (id: string) => { await apiClient.delete(`/appointments/${id}`); },
    onSuccess: invalidate,
  });
}

export function useCallPlanStatus() {
  const projectId = useAuthStore((s) => s.currentProjectId);
  return useQuery({
    queryKey: [...KEY, 'plan-status', projectId],
    queryFn: async () => (await apiClient.get<ApiResponse<CallPlanStatus>>('/appointments/plan/status', { params: { projectId: projectId || undefined } })).data.data,
    staleTime: 30_000,
    retry: 1,
  });
}

export function useRunCallPlan() {
  const invalidate = useInvalidate();
  const projectId = useAuthStore((s) => s.currentProjectId);
  return useMutation({
    mutationFn: async () => (await apiClient.post<ApiResponse<CallPlanRunResult>>('/appointments/plan/run', null, { params: { projectId: projectId || undefined, force: true } })).data.data,
    onSuccess: invalidate,
  });
}

// ── Tarih yardımcıları (yerel gün) ───────────────────────────────────────────

export function localDay(d: Date): string {
  const y = d.getFullYear(); const m = String(d.getMonth() + 1).padStart(2, '0'); const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}
export function addDays(day: string, delta: number): string {
  const [y, m, d] = day.split('-').map(Number);
  const dt = new Date(y, m - 1, d + delta);
  return localDay(dt);
}
/** Pazartesi'ye çek. */
export function startOfWeek(day: string): string {
  const [y, m, d] = day.split('-').map(Number);
  const dt = new Date(y, m - 1, d);
  const dow = (dt.getDay() + 6) % 7; // Pzt=0
  return addDays(day, -dow);
}
