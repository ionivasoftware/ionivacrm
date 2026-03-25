import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import { useAuthStore } from '@/stores/authStore';
import type {
  ApiResponse,
  PaginatedResponse,
  Customer,
  CreateCustomerRequest,
  UpdateCustomerRequest,
  CustomerListParams,
  ContactHistory,
  CreateContactHistoryRequest,
  CustomerTask,
  CreateTaskRequest,
  UpdateTaskRequest,
  TaskStatus,
  Opportunity,
  CreateOpportunityRequest,
  UpdateOpportunityRequest,
  OpportunityStage,
} from '@/types';

// ── Customer CRUD ─────────────────────────────────────────────────────────────

export function useCustomers(params: CustomerListParams = {}) {
  const projectId = useAuthStore((s) => s.currentProjectId);
  return useQuery({
    queryKey: ['customers', projectId, params],
    queryFn: async () => {
      // Backend uses JWT claims to filter by project (not a query param).
      // Map `page` → backend expects `page` (not `pageNumber`).
      // Exclude `undefined` values so they don't show as empty query params.
      const queryParams: Record<string, string | number | undefined> = {};
      if (params.search) queryParams.search = params.search;
      if (params.status) queryParams.status = params.status;
      if (params.segment) queryParams.segment = params.segment;
      if (params.assignedUserId) queryParams.assignedUserId = params.assignedUserId;
      if (params.label) queryParams.label = params.label;
      queryParams.page = params.page ?? 1;
      queryParams.pageSize = params.pageSize ?? 20;

      const response = await apiClient.get<ApiResponse<PaginatedResponse<Customer>>>(
        '/customers',
        { params: queryParams }
      );
      return response.data.data;
    },
    enabled: true, // SuperAdmin can see all customers without projectId
  });
}

export function useCustomer(id: string) {
  return useQuery({
    queryKey: ['customer', id],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<Customer>>(`/customers/${id}`);
      return response.data.data;
    },
    enabled: !!id,
  });
}

export function useCreateCustomer() {
  const queryClient = useQueryClient();
  // Resolve projectId from store — backend requires it in the request body
  const projectId = useAuthStore((s) => s.currentProjectId);

  return useMutation({
    mutationFn: async (data: Omit<CreateCustomerRequest, 'projectId'>) => {
      if (!projectId) throw new Error('Proje seçili değil. Lütfen bir proje seçiniz.');
      const payload: CreateCustomerRequest = { ...data, projectId };
      const response = await apiClient.post<ApiResponse<Customer>>('/customers', payload);
      return response.data.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['customers'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

export function useUpdateCustomer() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: UpdateCustomerRequest) => {
      const response = await apiClient.put<ApiResponse<Customer>>(
        `/customers/${data.id}`,
        data
      );
      return response.data.data;
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['customers'] });
      queryClient.invalidateQueries({ queryKey: ['customer', variables.id] });
    },
  });
}

export function useDeleteCustomer() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await apiClient.delete(`/customers/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['customers'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

// ── All Contact Histories (global) ────────────────────────────────────────────

export interface AllContactHistoriesParams {
  projectId?: string;
  customerId?: string;
  type?: string;
  fromDate?: string;
  toDate?: string;
  page?: number;
  pageSize?: number;
}

export function useAllContactHistories(params: AllContactHistoriesParams = {}) {
  const projectId = useAuthStore((s) => s.currentProjectId);
  return useQuery({
    queryKey: ['allContactHistories', params],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<PaginatedResponse<ContactHistory>>>(
        '/contact-histories',
        { params: { projectId, ...params } }
      );
      return response.data.data;
    },
    enabled: !!projectId,
    staleTime: 60 * 1000,
  });
}

// ── Contact History (per customer) ────────────────────────────────────────────

export function useContactHistory(customerId: string) {
  return useQuery({
    queryKey: ['contactHistory', customerId],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<PaginatedResponse<ContactHistory>>>(
        `/customers/${customerId}/contact-histories`,
        { params: { pageSize: 50, page: 1 } }
      );
      return response.data.data;
    },
    enabled: !!customerId,
  });
}

export function useCreateContactHistory() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: CreateContactHistoryRequest) => {
      const response = await apiClient.post<ApiResponse<ContactHistory>>(
        `/customers/${data.customerId}/contact-histories`,
        data
      );
      return response.data.data;
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['contactHistory', variables.customerId] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

// ── Tasks ─────────────────────────────────────────────────────────────────────

export function useCustomerTasks(customerId: string) {
  return useQuery({
    queryKey: ['customerTasks', customerId],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<PaginatedResponse<CustomerTask>>>(
        `/customers/${customerId}/tasks`,
        { params: { pageSize: 50, page: 1 } }
      );
      return response.data.data;
    },
    enabled: !!customerId,
  });
}

export function useCreateTask() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: CreateTaskRequest) => {
      const response = await apiClient.post<ApiResponse<CustomerTask>>(
        `/customers/${data.customerId}/tasks`,
        data
      );
      return response.data.data;
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['customerTasks', variables.customerId] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

export function useUpdateTask() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: UpdateTaskRequest) => {
      const response = await apiClient.put<ApiResponse<CustomerTask>>(
        `/customers/${data.customerId}/tasks/${data.id}`,
        data
      );
      return response.data.data;
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['customerTasks', variables.customerId] });
    },
  });
}

// ── Opportunities ─────────────────────────────────────────────────────────────

export function useCustomerOpportunities(customerId: string) {
  return useQuery({
    queryKey: ['customerOpportunities', customerId],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<PaginatedResponse<Opportunity>>>(
        `/customers/${customerId}/opportunities`,
        { params: { pageSize: 50, page: 1 } }
      );
      return response.data.data;
    },
    enabled: !!customerId,
  });
}

export function useCreateOpportunity() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: CreateOpportunityRequest) => {
      const response = await apiClient.post<ApiResponse<Opportunity>>(
        `/customers/${data.customerId}/opportunities`,
        data
      );
      return response.data.data;
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['customerOpportunities', variables.customerId] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

export function useUpdateOpportunity() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (data: UpdateOpportunityRequest) => {
      const response = await apiClient.put<ApiResponse<Opportunity>>(
        `/customers/${data.customerId}/opportunities/${data.id}`,
        data
      );
      return response.data.data;
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['customerOpportunities', variables.customerId] });
      queryClient.invalidateQueries({ queryKey: ['allOpportunities'] });
    },
  });
}

// ── Project-level Tasks ────────────────────────────────────────────────────────

export interface AllTasksParams {
  status?: TaskStatus;
  priority?: string;
  page?: number;
  pageSize?: number;
}

export function useAllTasks(params: AllTasksParams = {}) {
  const projectId = useAuthStore((s) => s.currentProjectId);
  return useQuery({
    queryKey: ['allTasks', projectId, params],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<PaginatedResponse<CustomerTask>>>(
        '/tasks',
        { params: { projectId, pageSize: 200, ...params } }
      );
      return response.data.data;
    },
    enabled: !!projectId,
    staleTime: 30 * 1000,
  });
}

export function useUpdateTaskStatus() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ customerId, taskId, status }: { customerId: string; taskId: string; status: TaskStatus }) => {
      const response = await apiClient.patch<ApiResponse<CustomerTask>>(
        `/customers/${customerId}/tasks/${taskId}/status`,
        { status }
      );
      return response.data.data;
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['customerTasks', variables.customerId] });
      queryClient.invalidateQueries({ queryKey: ['allTasks'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

// ── Project-level Opportunities (Pipeline) ────────────────────────────────────

export interface AllOpportunitiesParams {
  stage?: OpportunityStage;
  page?: number;
  pageSize?: number;
}

export function useAllOpportunities(params: AllOpportunitiesParams = {}) {
  const projectId = useAuthStore((s) => s.currentProjectId);
  return useQuery({
    queryKey: ['allOpportunities', projectId, params],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<PaginatedResponse<Opportunity>>>(
        '/pipeline',
        { params: { projectId, pageSize: 200, ...params } }
      );
      return response.data.data;
    },
    enabled: !!projectId,
    staleTime: 30 * 1000,
  });
}

export function useUpdateOpportunityStage() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, stage }: { id: string; stage: OpportunityStage }) => {
      const response = await apiClient.patch<ApiResponse<Opportunity>>(
        `/pipeline/${id}/stage`,
        { stage }
      );
      return response.data.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['allOpportunities'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}
