import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse, ParasutProduct, ParasutProductKey, ParasutProductListItem } from '@/types';

// ── Types ──────────────────────────────────────────────────────────────────────

export interface ParasutStatus {
  isConnected: boolean;
  companyId: number | null;
  username: string | null;
  tokenExpiresAt: string | null;
}

export interface ConnectParasutRequest {
  /** Pass null to create a global (project-independent) connection. */
  projectId: string | null;
  companyId: number;
  clientId: string;
  clientSecret: string;
  username: string;
  password: string;
}

export interface ConnectParasutResponse {
  projectId: string | null;
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

/**
 * Fetches Paraşüt connection status.
 * Pass `null`/omit `projectId` to query the global connection directly.
 * Pass a project id to query that project (with fallback to global).
 */
export function useParasutStatus(projectId?: string | null) {
  return useQuery({
    queryKey: ['parasutStatus', projectId ?? 'global'],
    queryFn: async () => {
      const url = projectId
        ? `/parasut/status?projectId=${projectId}`
        : `/parasut/status`;
      const res = await apiClient.get<ApiResponse<ParasutStatus>>(url);
      return res.data.data;
    },
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
    onSuccess: () => {
      // Invalidate every parasutStatus cache entry — global + per-project consumers all need to refetch.
      qc.invalidateQueries({ queryKey: ['parasutStatus'] });
    },
  });
}

/**
 * Disconnects a Paraşüt connection.
 * Pass `null`/omit to disconnect the global connection.
 */
export function useDisconnectParasut() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (projectId?: string | null) => {
      const url = projectId
        ? `/parasut/disconnect?projectId=${projectId}`
        : `/parasut/disconnect`;
      await apiClient.delete(url);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['parasutStatus'] });
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
      parasutContactName,
    }: {
      projectId: string;
      customerId: string;
      parasutContactId: string;
      parasutContactName?: string;
    }) => {
      const res = await apiClient.post<ApiResponse<LinkParasutContactResponse>>(
        '/parasut/contacts/link',
        { projectId, customerId, parasutContactId, parasutContactName }
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

// ── Paraşüt Product Mapping ──────────────────────────────────────────────────

export interface SaveParasutProductRequest {
  projectId: string;
  productKey: ParasutProductKey;
  productName: string;
  parasutProductId: string;
  parasutProductName?: string;
  unitPrice: number;
  /** Tax rate as decimal: 0.20 = 20% */
  taxRate: number;
  /** EMS product ID to match incoming EMS payments to this product */
  emsProductId?: string;
}

/** Fetch saved CRM→Paraşüt product mappings from our DB */
export function useParasutProducts(projectId: string | null) {
  return useQuery({
    queryKey: ['parasutProducts', projectId],
    queryFn: async () => {
      const res = await apiClient.get<ApiResponse<ParasutProduct[]>>(
        `/crm/parasut-products?projectId=${projectId}`
      );
      return res.data.data;
    },
    enabled: !!projectId,
    staleTime: 30_000,
  });
}

/** Fetch live product list from Paraşüt API */
export function useParasutProductList(projectId: string | null, enabled = false) {
  return useQuery({
    queryKey: ['parasutProductList', projectId],
    queryFn: async () => {
      const res = await apiClient.get<ApiResponse<ParasutProductListItem[]>>(
        `/crm/parasut/products?projectId=${projectId}`
      );
      return res.data.data;
    },
    enabled: !!projectId && enabled,
    staleTime: 5 * 60_000,
  });
}

/** Create or update a CRM→Paraşüt product mapping (POST creates, PUT updates by id) */
export function useSaveParasutProduct() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ existingId, ...data }: SaveParasutProductRequest & { existingId?: string }) => {
      if (existingId) {
        const res = await apiClient.put<ApiResponse<ParasutProduct>>(
          `/crm/parasut-products/${existingId}`,
          data
        );
        return res.data.data;
      }
      const res = await apiClient.post<ApiResponse<ParasutProduct>>(
        '/crm/parasut-products',
        data
      );
      return res.data.data;
    },
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ['parasutProducts', vars.projectId] });
    },
  });
}

/** @deprecated Use useSaveParasutProduct instead */
export function useUpsertParasutProduct() {
  return useSaveParasutProduct();
}

export function useParasutContactInvoices(
  projectId: string | null,
  parasutContactId: string | null | undefined,
  page = 1
) {
  return useQuery({
    queryKey: ['parasutContactInvoices', projectId, parasutContactId, page],
    queryFn: async () => {
      const res = await apiClient.get<ApiResponse<ParasutInvoicesDto>>(
        `/parasut/contacts/${encodeURIComponent(parasutContactId!)}/invoices?projectId=${projectId}&page=${page}&pageSize=10`
      );
      return res.data.data;
    },
    enabled: !!projectId && !!parasutContactId,
    staleTime: 60_000,
  });
}
