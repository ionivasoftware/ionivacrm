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

export function useParasutContacts(projectId: string | null, page = 1, enabled = false) {
  return useQuery({
    queryKey: ['parasutContacts', projectId, page],
    queryFn: async () => {
      const res = await apiClient.get<ApiResponse<ParasutContactsDto>>(
        `/parasut/contacts?projectId=${projectId}&page=${page}&pageSize=25`
      );
      return res.data.data;
    },
    enabled: !!projectId && enabled,
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
