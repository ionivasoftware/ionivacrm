import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse } from '@/types';

export type VendorInvoiceStatus = 'Expected' | 'Received' | 'Reconciled' | 'Mismatch' | 'Missing';
export type VendorBillingType = 'Usage' | 'Fixed';

/** A vendor-invoice reconciliation record (company operational cost). */
export interface VendorInvoice {
  id: string;
  provider: string;
  periodYear: number;
  periodMonth: number;
  billingType: VendorBillingType;
  status: VendorInvoiceStatus;
  expectedAmount: number | null;
  receivedAmount: number | null;
  currency: string | null;
  invoiceNumber: string | null;
  pdfUrl: string | null;
  dueDay: number;
  dueDate: string;
  expectedOn: string | null;
  receivedOn: string | null;
  alertedOn: string | null;
  notes: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface VendorInvoiceParams {
  year?: number;
  month?: number;
  status?: VendorInvoiceStatus;
  provider?: string;
}

export function useVendorInvoices(params: VendorInvoiceParams) {
  return useQuery({
    queryKey: ['vendorInvoices', params],
    queryFn: async () => {
      const p = new URLSearchParams();
      if (params.year)     p.set('year', String(params.year));
      if (params.month)    p.set('month', String(params.month));
      if (params.status)   p.set('status', params.status);
      if (params.provider) p.set('provider', params.provider);
      const res = await apiClient.get<ApiResponse<VendorInvoice[]>>(`/vendor-invoices?${p}`);
      return res.data.data ?? [];
    },
  });
}

/**
 * Count of Missing invoices — drives the red alarm badge.
 * `enabled` MUST be false for non-SuperAdmin users: the endpoint is SuperAdmin-only and would 403.
 */
export function useMissingInvoiceCount(enabled = true) {
  return useQuery({
    queryKey: ['vendorInvoices', 'missing-count'],
    queryFn: async () => {
      const res = await apiClient.get<ApiResponse<{ count: number }>>('/vendor-invoices/missing-count');
      return res.data.data?.count ?? 0;
    },
    enabled,
    refetchInterval: 300_000,
  });
}

function useInvalidate() {
  const qc = useQueryClient();
  return () => qc.invalidateQueries({ queryKey: ['vendorInvoices'] });
}

export interface ExpectBody {
  provider: string;
  year: number;
  month: number;
  expectedAmount?: number | null;
  currency?: string | null;
  dueDay?: number | null;
  billingType?: VendorBillingType | null;
}

export function useExpectInvoice() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async (body: ExpectBody) => {
      const res = await apiClient.post<ApiResponse<VendorInvoice>>('/vendor-invoices/expect', body);
      return res.data.data;
    },
    onSuccess: invalidate,
  });
}

export interface MarkReceivedBody {
  provider: string;
  year: number;
  month: number;
  receivedAmount?: number | null;
  currency?: string | null;
  invoiceNumber?: string | null;
  pdfUrl?: string | null;
}

export function useMarkReceived() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async (body: MarkReceivedBody) => {
      const res = await apiClient.post<ApiResponse<VendorInvoice>>('/vendor-invoices/mark-received', body);
      return res.data.data;
    },
    onSuccess: invalidate,
  });
}

export function useSeedMonth() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ year, month }: { year: number; month: number }) => {
      const res = await apiClient.post<ApiResponse<VendorInvoice[]>>('/vendor-invoices/seed-month', { year, month });
      return res.data.data ?? [];
    },
    onSuccess: invalidate,
  });
}

export interface ReconcileResult {
  missingCount: number;
  missing: VendorInvoice[];
}

export function useReconcile() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async () => {
      const res = await apiClient.post<ApiResponse<ReconcileResult>>('/vendor-invoices/reconcile', {});
      return res.data.data;
    },
    onSuccess: invalidate,
  });
}
