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
  Opportunity,
  CreateOpportunityRequest,
  UpdateOpportunityRequest,
} from '@/types';

// ── Customer CRUD ─────────────────────────────────────────────────────────────

export function useCustomers(params: CustomerListParams = {}) {
  const projectId = useAuthStore((s) => s.currentProjectId);
  return useQuery({
    queryKey: ['customers', projectId, params],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<PaginatedResponse<Customer>>>(
        '/customers',
        { params: { ...params, projectId } }
      );
      return response.data.data;
    },
    enabled: !!projectId,
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
  return useMutation({
    mutationFn: async (data: CreateCustomerRequest) => {
      const response = await apiClient.post<ApiResponse<Customer>>('/customers', data);
      return response.data.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['customers'] });
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
    },
  });
}

// ── Contact History ───────────────────────────────────────────────────────────

export function useContactHistory(customerId: string) {
  return useQuery({
    queryKey: ['contactHistory', customerId],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<PaginatedResponse<ContactHistory>>>(
        `/customers/${customerId}/contact-history`,
        { params: { pageSize: 50, pageNumber: 1 } }
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
        `/customers/${data.customerId}/contact-history`,
        data
      );
      return response.data.data;
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['contactHistory', variables.customerId] });
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
        { params: { pageSize: 50, pageNumber: 1 } }
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
        { params: { pageSize: 50, pageNumber: 1 } }
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
    },
  });
}
