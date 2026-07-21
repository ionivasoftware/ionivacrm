import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse } from '@/types';

// ── Types ─────────────────────────────────────────────────────────────────────

/** A subscription plan (fixed tier). Prices are NET TL (VAT added server-side on charge). */
export interface PricingPlan {
  id: string;
  name: string;
  /** Standart | Pro | Prime — read-only (feature gating + iyzico matching). */
  tier: string | null;
  description: string | null;
  priceMonthly: number;
  /** Yearly TOTAL, not monthly-equivalent. */
  priceYearly: number;
  /** 0 = unlimited. */
  maxUsers: number;
  /** 0 = unlimited. */
  maxElevators: number;
  isActive: boolean;
  iyzicoProductReferenceCode: string | null;
  iyzicoPlanReferenceCodeMonthly: string | null;
  iyzicoPlanReferenceCodeYearly: string | null;
  createdAt: string | null;
}

export interface SmsPackage {
  id: string;
  name: string;
  smsCount: number;
  price: number;
  isActive: boolean;
  createdAt: string | null;
}

export interface UpdatePlanRequest {
  name: string;
  description: string | null;
  priceMonthly: number;
  priceYearly: number;
  maxUsers: number;
  maxElevators: number;
  isActive: boolean;
}

export interface CreateSmsPackageRequest {
  name: string;
  smsCount: number;
  price: number;
}

export interface UpdateSmsPackageRequest {
  name: string;
  smsCount: number;
  price: number;
  isActive: boolean;
}

// ── Subscription plans ──────────────────────────────────────────────────────

/** Lists all subscription plans (incl. inactive). SuperAdmin only, proxied via the CRM API. */
export function usePricingPlans() {
  return useQuery({
    queryKey: ['pricingPlans'],
    queryFn: async () => {
      const res = await apiClient.get<ApiResponse<PricingPlan[]>>('/pricing/plans');
      return res.data.data ?? [];
    },
  });
}

/** Updates a subscription plan (full replace). */
export function useUpdatePlan() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...data }: UpdatePlanRequest & { id: string }) => {
      const res = await apiClient.put<ApiResponse<PricingPlan>>(`/pricing/plans/${id}`, data);
      return res.data.data;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['pricingPlans'] }),
  });
}

// ── SMS packages ────────────────────────────────────────────────────────────

/** Lists all SMS packages (incl. inactive). */
export function useSmsPackages() {
  return useQuery({
    queryKey: ['smsPackages'],
    queryFn: async () => {
      const res = await apiClient.get<ApiResponse<SmsPackage[]>>('/pricing/sms-packages');
      return res.data.data ?? [];
    },
  });
}

export function useCreateSmsPackage() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (data: CreateSmsPackageRequest) => {
      const res = await apiClient.post<ApiResponse<SmsPackage>>('/pricing/sms-packages', data);
      return res.data.data;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['smsPackages'] }),
  });
}

export function useUpdateSmsPackage() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...data }: UpdateSmsPackageRequest & { id: string }) => {
      const res = await apiClient.put<ApiResponse<SmsPackage>>(`/pricing/sms-packages/${id}`, data);
      return res.data.data;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['smsPackages'] }),
  });
}

/** Soft-deletes (deactivates) an SMS package. */
export function useDeleteSmsPackage() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await apiClient.delete(`/pricing/sms-packages/${id}`);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['smsPackages'] }),
  });
}
