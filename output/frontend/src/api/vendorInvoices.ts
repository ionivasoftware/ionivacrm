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
  hasPdf: boolean;
}

/**
 * Opens the invoice PDF: an external hosted link directly, or the stored PDF fetched with auth
 * (a plain new-tab navigation can't send the JWT, so the blob is fetched then opened as an object URL).
 */
export async function openInvoicePdf(inv: Pick<VendorInvoice, 'id' | 'pdfUrl' | 'hasPdf'>): Promise<void> {
  if (inv.pdfUrl) {
    window.open(inv.pdfUrl, '_blank', 'noopener');
    return;
  }
  if (!inv.hasPdf) return;
  const win = window.open('', '_blank'); // open synchronously to dodge popup blockers
  try {
    const res = await apiClient.get(`/vendor-invoices/${inv.id}/pdf`, { responseType: 'blob' });
    // Force the application/pdf MIME type so the browser displays it inline instead of downloading.
    const blob = new Blob([res.data as BlobPart], { type: 'application/pdf' });
    const url = URL.createObjectURL(blob);
    if (win) win.location.href = url;
    else window.open(url, '_blank', 'noopener');
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
  } catch {
    win?.close();
  }
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

/** Uploads (or replaces) the PDF file for an invoice. */
export function useUploadPdf() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ id, file }: { id: string; file: File }) => {
      const form = new FormData();
      form.append('file', file);
      // The apiClient defaults to Content-Type: application/json — remove it so the browser sets
      // multipart/form-data WITH the boundary (otherwise the server returns 415).
      await apiClient.post<ApiResponse<object>>(`/vendor-invoices/${id}/pdf`, form, {
        headers: { 'Content-Type': undefined },
      });
    },
    onSuccess: invalidate,
  });
}

export function useDeleteInvoice() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async (id: string) => {
      await apiClient.delete<ApiResponse<object>>(`/vendor-invoices/${id}`);
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

export interface AutoExpectItem {
  provider: string;
  status: 'expected' | 'skipped' | 'failed' | 'received';
  amount: number | null;
  currency: string | null;
  message: string | null;
}

export interface AutoExpectSummary {
  year: number;
  month: number;
  items: AutoExpectItem[];
}

/** Pulls each configured provider's cost for a period and upserts Expected rows (Phase 2). */
export function useAutoExpect() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ year, month }: { year: number; month: number }) => {
      const res = await apiClient.post<ApiResponse<AutoExpectSummary>>('/vendor-invoices/auto-expect', { year, month });
      return res.data.data;
    },
    onSuccess: invalidate,
  });
}

export interface EmailCollectItem {
  provider: string;
  year: number;
  month: number;
  amount: number | null;
  currency: string | null;
  invoiceNumber: string | null;
  subject: string;
  emailDate: string;
  status: 'received' | 'preview' | 'no-amount' | 'failed' | 'unmatched';
  message: string | null;
}

export interface EmailCollectSummary {
  scanned: number;
  matched: number;
  received: number;
  items: EmailCollectItem[];
}

/** Scans the accounting mailbox for invoice e-mails (Phase 3). `dryRun` previews without writing. */
export function useCollectEmails() {
  const invalidate = useInvalidate();
  return useMutation({
    mutationFn: async ({ dryRun }: { dryRun: boolean }) => {
      const res = await apiClient.post<ApiResponse<EmailCollectSummary>>(
        `/vendor-invoices/collect-emails?dryRun=${dryRun}`,
        {}
      );
      return res.data.data;
    },
    onSuccess: (_data, vars) => {
      if (!vars.dryRun) invalidate();
    },
  });
}
