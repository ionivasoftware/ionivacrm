import { useMutation, useQuery } from '@tanstack/react-query';
import { apiClient } from './client';
import type { ApiResponse, LoginRequest, LoginResponse, AuthUser } from '@/types';

export function useLogin() {
  return useMutation({
    mutationFn: async (credentials: LoginRequest) => {
      const response = await apiClient.post<ApiResponse<LoginResponse>>(
        '/auth/login',
        credentials
      );
      return response.data.data;
    },
  });
}

export function useCurrentUser() {
  return useQuery({
    queryKey: ['auth', 'me'],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<AuthUser>>('/auth/me');
      return response.data.data;
    },
    staleTime: 5 * 60 * 1000, // 5 minutes
    retry: false,
  });
}
