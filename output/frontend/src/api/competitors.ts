import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse } from '@/types';

// ── Rakip firmalar ──────────────────────────────────────────────────────────
// Görüşme kaydında "farklı firma ile çalışıyor" seçilince listeden seçilir; listede yoksa buradan açılır.
// Proje (tenant) bazlı: Liftdesk rakipleri ile RezervAl rakipleri ayrı.

export interface Competitor {
  id: string;
  projectId: string;
  name: string;
  website: string | null;
  notes: string | null;
  /** Bu firmayla çalıştığı bilinen müşteri sayısı. */
  customerCount: number;
  createdAt: string;
}

const KEY = ['competitors'] as const;

export function useCompetitors(projectId?: string | null) {
  return useQuery({
    queryKey: [...KEY, projectId ?? ''],
    queryFn: async () => (await apiClient.get<ApiResponse<Competitor[]>>('/competitors', { params: { projectId } })).data.data ?? [],
    enabled: !!projectId,
    staleTime: 60_000,
    retry: 1,
  });
}

export function useCreateCompetitor() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: { projectId: string; name: string; website?: string; notes?: string }) =>
      (await apiClient.post<ApiResponse<Competitor>>('/competitors', body)).data.data,
    onSuccess: () => { qc.invalidateQueries({ queryKey: [...KEY] }); },
  });
}

export function useUpdateCompetitor() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...body }: { id: string; projectId: string; name: string; website?: string | null; notes?: string | null }) =>
      (await apiClient.put<ApiResponse<Competitor>>(`/competitors/${id}`, body)).data.data,
    onSuccess: () => { qc.invalidateQueries({ queryKey: [...KEY] }); },
  });
}

export function useDeleteCompetitor() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => (await apiClient.delete<ApiResponse<{ deleted: boolean }>>(`/competitors/${id}`)).data.data,
    onSuccess: () => { qc.invalidateQueries({ queryKey: [...KEY] }); qc.invalidateQueries({ queryKey: ['customer'] }); qc.invalidateQueries({ queryKey: ['contactHistory'] }); },
  });
}
