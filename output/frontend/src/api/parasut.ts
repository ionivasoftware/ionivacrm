import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse } from '@/types';

// ── Types ──────────────────────────────────────────────────────────────────────

export interface ParasutStatus {
  isConnected: boolean;
  companyId: number | null;
  username: string | null;
  tokenExpiresAt: string | null;
}

export interface ConnectParasutRequest {
  projectId: string;
  companyId: number;
  clientId: string;
  clientSecret: string;
  username: string;
  password: string;
}

export interface ConnectParasutResponse {
  projectId: string;
  companyId: number;
  username: string;
  isConnected: boolean;
  tokenExpiresAt: string;
}

export interface ParasutContact {
  id: string;
  name: string;
  email: string | null;
  phone: string | null;
  contactType: string;
  accountType: string;
  taxNumber: string | null;
}

export interface ParasutContactsDto {
  items: ParasutContact[];
  totalCount: number;
  totalPages: number;
  currentPage: number;
}

export interface ParasutInvoice {
  id: string;
  issueDate: string;
  dueDate: string;
  currency: string;
  grossTotal: number;
  netTotal: number;
  totalPaid: number;
  remaining: number;
  description: string | null;
  archivingStatus: string | null;
}

export interface ParasutInvoicesDto {
  items: ParasutInvoice[];
  totalCount: number;
  totalPages: number;
  currentPage: number;
}

// ── API hooks ─────────────────────────────────────────────────────────────────

export function useParasutStatus(projectId: string | null) {
  return useQuery({
    queryKey: ['parasutStatus', projectId],
    queryFn: async () => {
      const res = await apiClient.get<ApiResponse<ParasutStatus>>(
        `/parasut/status?projectId=${projectId}`
      );
      return res.data.data;
    },
    enabled: !!projectId,
    staleTime: 30_000,
  });
}

export function useConnectParasut() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (data: ConnectParasutRequest) => {
      const res = await apiClient.post<ApiResponse<ConnectParasutResponse>>(
        '/parasut/connect',
        data
      );
      return res.data.data;
    },
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ['parasutStatus', vars.projectId] });
    },
  });
}

export function useDisconnectParasut() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (projectId: string) => {
      await apiClient.delete(`/parasut/disconnect/${projectId}`);
    },
    onSuccess: (_data, projectId) => {
      qc.invalidateQueries({ queryKey: ['parasutStatus', projectId] });
    },
  });
}

export interface SyncContactResponse {
  customerId: string;
  parasutContactId: string;
  parasutContactName: string;
  wasCreated: boolean;
}

export function useSyncContactToParasut() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ projectId, customerId }: { projectId: string; customerId: string }) => {
      const res = await apiClient.post<ApiResponse<SyncContactResponse>>('/parasut/contacts/sync', {
        projectId,
        customerId,
      });
      return res.data.data;
    },
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ['customer', vars.customerId] });
    },
  });
}

export interface LinkParasutContactResponse {
  customerId: string;
  parasutContactId: string;
  parasutContactName: string;
}

export function useLinkParasutContact() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({
      projectId,
      customerId,
      parasutContactId,
    }: {
      projectId: string;
      customerId: string;
      parasutContactId: string;
    }) => {
      const res = await apiClient.post<ApiResponse<LinkParasutContactResponse>>(
        '/parasut/contacts/link',
        { projectId, customerId, parasutContactId }
      );
      return res.data.data;
    },
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ['customer', vars.customerId] });
    },
  });
}

export interface InvoiceLine {
  description?: string;
  quantity: number;
  unitPrice: number;
  vatRate: number;
  discountValue?: number;
  discountType?: string;
  unit?: string;
}

export interface CreateInvoiceRequest {
  projectId: string;
  parasutContactId?: string;
  issueDate: string;
  dueDate: string;
  currency: string;
  description?: string;
  invoiceSeries?: string;
  invoiceId?: number;
  lines: InvoiceLine[];
}

export interface CreateInvoiceResponse {
  parasutInvoiceId: string;
  issueDate: string;
  dueDate: string;
  grossTotal: number;
  netTotal: number;
  currency: string;
}

export function useCreateParasutInvoice() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (data: CreateInvoiceRequest) => {
      const res = await apiClient.post<ApiResponse<CreateInvoiceResponse>>('/parasut/invoices', data);
      return res.data.data;
    },
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ['parasutInvoices', vars.projectId] });
    },
  });
}

export function useParasutContacts(
  projectId: string | null,
  page = 1,
  enabled = false,
  search = ''
) {
  return useQuery({
    queryKey: ['parasutContacts', projectId, page, search],
    queryFn: async () => {
      let url = `/parasut/contacts?projectId=${projectId}&page=${page}&pageSize=25`;
      if (search) url += `&search=${encodeURIComponent(search)}`;
      const res = await apiClient.get<ApiResponse<ParasutContactsDto>>(url);
      return res.data.data;
    },
    enabled: !!projectId && enabled,
    staleTime: 30_000,
  });
}

export function useParasutInvoices(projectId: string | null, page = 1, enabled = false) {
  return useQuery({
    queryKey: ['parasutInvoices', projectId, page],
    queryFn: async () => {
      const res = await apiClient.get<ApiResponse<ParasutInvoicesDto>>(
        `/parasut/invoices?projectId=${projectId}&page=${page}&pageSize=25`
      );
      return res.data.data;
    },
    enabled: !!projectId && enabled,
  });
}
