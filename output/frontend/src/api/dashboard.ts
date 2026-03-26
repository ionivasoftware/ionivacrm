import { useQuery } from '@tanstack/react-query';
import { apiClient } from './client';
import { useAuthStore } from '@/stores/authStore';
import type { ApiResponse, DashboardStats, RecentActivity } from '@/types';

export function useDashboardStats() {
  const projectId = useAuthStore((s) => s.currentProjectId);
  return useQuery({
    queryKey: ['dashboard', 'stats', projectId],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<DashboardStats>>(
        '/dashboard/stats',
        { params: { projectId } }
      );
      return response.data.data;
    },
    enabled: !!projectId,
    staleTime: 2 * 60 * 1000,
    retry: 1,
  });
}

export function useNotifications() {
  return useQuery({
    queryKey: ['dashboard', 'notifications'],
    queryFn: async () => {
      const response = await apiClient.get<ApiResponse<RecentActivity[]>>(
        '/dashboard/notifications'
      );
      return response.data.data ?? [];
    },
    staleTime: 60 * 1000,
    retry: 1,
  });
}
