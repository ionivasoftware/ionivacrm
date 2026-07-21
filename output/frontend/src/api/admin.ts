import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse } from '@/types';

// ── Types ──────────────────────────────────────────────────────────────────────

export interface AdminUser {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  fullName: string;
  isSuperAdmin: boolean;
  isActive: boolean;
  createdAt: string;
  projectRoles: { projectId: string; projectName: string; role: string }[];
}

export interface CreateUserRequest {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
  isSuperAdmin: boolean;
}

export interface AssignRoleRequest {
  projectId: string;
  role: string;
}

export interface UpdateUserRequest {
  firstName: string;
  lastName: string;
  isActive: boolean;
  isSuperAdmin: boolean;
}

export interface AdminProject {
  id: string;
  name: string;
  description: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
  emsBaseUrl: string | null;
  emsApiKey: string | null;
  rezervAlBaseUrl: string | null;
  rezervAlApiKey: string | null;
  liftdeskBaseUrl: string | null;
  liftdeskApiKey: string | null;
  smsCount: number;
}

export interface AddSmsResult {
  companyId: string;
  smsCount: number;
  added: number;
}

export interface CreateProjectRequest { name: string; description?: string }
export interface UpdateProjectRequest {
  name: string;
  description?: string;
  isActive: boolean;
  emsBaseUrl?: string | null;
  emsApiKey?: string | null;
  rezervAlBaseUrl?: string | null;
  rezervAlApiKey?: string | null;
}
export interface SetProjectApiKeysRequest {
  emsBaseUrl: string | null;
  emsApiKey: string | null;
  rezervAlBaseUrl: string | null;
  rezervAlApiKey: string | null;
  liftdeskBaseUrl: string | null;
  liftdeskApiKey: string | null;
}

// ── Users ──────────────────────────────────────────────────────────────────────

export function useAdminUsers() {
  return useQuery({
    queryKey: ['adminUsers'],
    queryFn: async () => {
      const res = await apiClient.get<ApiResponse<AdminUser[]>>('/users');
      return res.data.data;
    },
  });
}

export function useCreateUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (data: CreateUserRequest) => {
      const res = await apiClient.post<ApiResponse<AdminUser>>('/users', data);
      return res.data.data;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['adminUsers'] }),
  });
}

export function useUpdateUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...data }: UpdateUserRequest & { id: string }) => {
      const res = await apiClient.put<ApiResponse<AdminUser>>(`/users/${id}`, data);
      return res.data.data;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['adminUsers'] }),
  });
}

export function useDeleteUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      await apiClient.delete(`/users/${id}`);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['adminUsers'] }),
  });
}

export function useAssignRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ userId, ...body }: AssignRoleRequest & { userId: string }) => {
      await apiClient.post(`/users/${userId}/roles`, body);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['adminUsers'] }),
  });
}

/** SuperAdmin resets a user's password. Omit `newPassword` to have the server generate a strong one.
 *  Returns the effective password once so it can be handed to the user. No list invalidation needed. */
export function useResetUserPassword() {
  return useMutation({
    mutationFn: async ({ id, newPassword }: { id: string; newPassword?: string }) => {
      const res = await apiClient.post<ApiResponse<{ password: string }>>(
        `/users/${id}/reset-password`,
        { newPassword: newPassword?.trim() ? newPassword : null }
      );
      return res.data.data;
    },
  });
}

// ── Projects ───────────────────────────────────────────────────────────────────

export function useAdminProjects() {
  return useQuery({
    queryKey: ['adminProjects'],
    queryFn: async () => {
      const res = await apiClient.get<ApiResponse<AdminProject[]>>('/projects');
      return res.data.data;
    },
  });
}

export function useCreateProject() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (data: CreateProjectRequest) => {
      const res = await apiClient.post<ApiResponse<AdminProject>>('/projects', data);
      return res.data.data;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['adminProjects'] }),
  });
}

export function useUpdateProject() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...data }: UpdateProjectRequest & { id: string }) => {
      const res = await apiClient.put<ApiResponse<AdminProject>>(`/projects/${id}`, data);
      return res.data.data;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['adminProjects'] }),
  });
}

export function useSetProjectApiKeys() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...data }: SetProjectApiKeysRequest & { id: string }) => {
      const res = await apiClient.put<ApiResponse<AdminProject>>(`/projects/${id}/api-keys`, data);
      return res.data.data;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['adminProjects'] }),
  });
}

// ── SMS Credits ────────────────────────────────────────────────────────────────

export function useAddProjectSms() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, count }: { id: string; count: number }) => {
      const res = await apiClient.post<ApiResponse<AddSmsResult>>(
        `/crm/companies/${id}/add-sms`,
        { count }
      );
      return res.data.data;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['adminProjects'] }),
  });
}
