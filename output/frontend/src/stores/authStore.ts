import { create } from 'zustand';
import { subscribeWithSelector } from 'zustand/middleware';
import type { AuthUser, LoginRequest, LoginResponse } from '@/types';
import { apiClient, setAccessToken } from '@/api/client';
import type { ApiResponse } from '@/types';

interface AuthState {
  user: AuthUser | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  currentProjectId: string | null;

  login: (credentials: LoginRequest) => Promise<void>;
  logout: () => Promise<void>;
  setCurrentProject: (projectId: string) => void;
  initializeAuth: () => Promise<void>;
}

export const useAuthStore = create<AuthState>()(
  subscribeWithSelector((set, get) => ({
    user: null,
    isAuthenticated: false,
    isLoading: true,
    currentProjectId: null,

    login: async (credentials: LoginRequest) => {
      const response = await apiClient.post<ApiResponse<LoginResponse>>(
        '/auth/login',
        credentials
      );

      const { accessToken, user } = response.data.data;
      setAccessToken(accessToken);
      localStorage.setItem('accessToken', accessToken);
      localStorage.setItem('user', JSON.stringify(user));

      // Set default project (first available)
      const projectIds = Object.keys(user.projectRoles);
      const defaultProject = projectIds[0] ?? null;

      set({
        user,
        isAuthenticated: true,
        currentProjectId: defaultProject,
      });
    },

    logout: async () => {
      try {
        await apiClient.post('/auth/logout');
      } catch {
        // Ignore errors on logout
      } finally {
        setAccessToken(null);
        localStorage.removeItem('accessToken');
        localStorage.removeItem('user');
        set({
          user: null,
          isAuthenticated: false,
          currentProjectId: null,
        });
      }
    },

    setCurrentProject: (projectId: string) => {
      const { user } = get();
      if (user?.isSuperAdmin || user?.projectRoles[projectId]) {
        set({ currentProjectId: projectId });
      }
    },

    initializeAuth: async () => {
      set({ isLoading: true });
      try {
        // Restore session from localStorage
        const savedToken = localStorage.getItem('accessToken');
        const savedUser = localStorage.getItem('user');
        if (savedToken && savedUser) {
          const user = JSON.parse(savedUser);
          setAccessToken(savedToken);
          const projectIds = Object.keys(user.projectRoles || {});
          const defaultProject = projectIds[0] ?? null;
          set({
            user,
            isAuthenticated: true,
            currentProjectId: defaultProject,
            isLoading: false,
          });
        } else {
          set({ user: null, isAuthenticated: false, isLoading: false });
        }
      } catch {
        setAccessToken(null);
        localStorage.removeItem('accessToken');
        localStorage.removeItem('user');
        set({ user: null, isAuthenticated: false, isLoading: false });
      }
    },
  }))
);
