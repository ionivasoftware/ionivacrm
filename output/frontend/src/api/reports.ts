import { useQuery } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse, CustomerStatus } from '@/types';

// ── Firma ve Lead Raporları (GET /reports/companies) ──────────────────────────

export interface StatusCounts {
  lead: number;
  active: number;
  demo: number;
  churned: number;
  passive: number;
  total: number;
}

/** `country` ISO-3166 alpha-2; null = bilinmiyor. */
export interface CountryCompanyCounts extends StatusCounts {
  country: string | null;
}

export interface CountryCount {
  country: string | null;
  count: number;
}

/** `competitorId` null = "farklı firma ile çalışıyor, adı verilmedi" kovası. */
export interface CompetitorUsage extends StatusCounts {
  competitorId: string | null;
  name: string;
  website: string | null;
  countries: CountryCount[];
}

export interface CompetitorCustomer {
  customerId: string;
  companyName: string;
  status: CustomerStatus;
  country: string | null;
  /** 'record' = kayıt ülkesi, 'phone' = telefonun uluslararası kodundan, null = bilinmiyor. */
  countrySource: 'record' | 'phone' | null;
  competitorId: string | null;
  competitorName: string | null;
  lastContactAt: string | null;
}

export interface CompanyReport {
  totals: StatusCounts;
  byCountry: CountryCompanyCounts[];
  countryFromPhone: number;
  countryUnknown: number;
  usingCompetitor: number;
  usingCompetitorUnnamed: number;
  competitors: CompetitorUsage[];
  competitorCustomers: CompetitorCustomer[];
}

export function useCompanyReport(projectId: string | null | undefined) {
  return useQuery({
    queryKey: ['reports', 'companies', projectId],
    queryFn: async () =>
      (await apiClient.get<ApiResponse<CompanyReport>>('/reports/companies', { params: { projectId } })).data.data,
    enabled: !!projectId,
    staleTime: 60 * 1000,
  });
}
