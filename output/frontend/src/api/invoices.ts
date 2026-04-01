import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import { useAuthStore } from '@/stores/authStore';
import type { ApiResponse, Invoice, CreateCrmInvoiceRequest } from '@/types';

// ── CRM Invoice Queries ──────────────────────────────────────────────────────

export function useInvoices() {
  const projectId = useAuthStore((s) => s.currentProjectId);
  return useQuery({
    queryKey: ['invoices', projectId],
    queryFn: async () => {
      const res = await apiClient.get<ApiResponse<Invoice[]>>(
        `/invoices?projectId=${projectId}`
      );
      return res.data.data;
    },
    enabled: !!projectId,
    staleTime: 30_000,
  });
}

export function useInvoice(id: string | null) {
  return useQuery({
    queryKey: ['invoice', id],
    queryFn: async () => {
      const res = await apiClient.get<ApiResponse<Invoice>>(`/invoices/${id}`);
      return res.data.data;
    },
    enabled: !!id,
  });
}

// ── CRM Invoice Mutations ────────────────────────────────────────────────────

export function useCreateInvoice() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (data: CreateCrmInvoiceRequest) => {
      const res = await apiClient.post<ApiResponse<Invoice>>('/invoices', data);
      return res.data.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['invoices'] });
    },
  });
}

export function useTransferToParasut() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (invoiceId: string) => {
      const res = await apiClient.post<ApiResponse<Invoice>>(
        `/invoices/${invoiceId}/transfer-to-parasut`
      );
      return res.data.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['invoices'] });
    },
  });
}
