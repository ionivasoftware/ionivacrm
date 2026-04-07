import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse, Invoice, CreateCrmInvoiceRequest, UpdateCrmInvoiceRequest } from '@/types';

// ── CRM Invoice Queries ──────────────────────────────────────────────────────

export function useInvoices() {
  return useQuery({
    queryKey: ['invoices'],
    queryFn: async () => {
      const res = await apiClient.get<ApiResponse<Invoice[]>>('/invoices');
      return res.data.data;
    },
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

export function useUpdateInvoice() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...data }: UpdateCrmInvoiceRequest) => {
      const res = await apiClient.put<ApiResponse<Invoice>>(`/invoices/${id}`, data);
      return res.data.data;
    },
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: ['invoices'] });
      qc.invalidateQueries({ queryKey: ['invoice', variables.id] });
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

export function useDeleteInvoice() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (invoiceId: string) => {
      await apiClient.delete(`/invoices/${invoiceId}`);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['invoices'] });
    },
  });
}

export function useMergeInvoices() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (params: { invoiceIds: string[]; title?: string }) => {
      const res = await apiClient.post<ApiResponse<Invoice>>('/invoices/merge', {
        invoiceIds: params.invoiceIds,
        title: params.title ?? null,
      });
      return res.data.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['invoices'] });
    },
  });
}
