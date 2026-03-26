import { create } from 'zustand';
import { subscribeWithSelector } from 'zustand/middleware';
import type { AuthUser, LoginRequest, LoginResponse, Project } from '@/types';
import { apiClient, setAccessToken } from '@/api/client';
import type { ApiResponse } from '@/types';

async function fetchProjects(): Promise<Project[]> {
  try {
    const res = await apiClient.get<ApiResponse<Project[]>>('/projects');
    return res.data.data ?? [];
  } catch {
    return [];
  }
}

interface AuthState {
  user: AuthUser | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  currentProjectId: string | null;
  projectNames: Record<string, string>;
  /** All project IDs fetched from /projects (used by SuperAdmin to list all projects) */
  allProjectIds: string[];

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
    projectNames: {},
    allProjectIds: [],

    login: async (credentials: LoginRequest) => {
      const response = await apiClient.post<ApiResponse<LoginResponse>>(
        '/auth/login',
        credentials
      );

      const { accessToken, user } = response.data.data;
      setAccessToken(accessToken);
      localStorage.setItem('accessToken', accessToken);
      localStorage.setItem('user', JSON.stringify(user));

      // Fetch all projects to get names + resolve SuperAdmin's default project
      const projects = await fetchProjects();
      const projectNames = Object.fromEntries(projects.map(p => [p.id, p.name]));
      const allProjectIds = projects.map(p => p.id);
      const projectIds = Object.keys(user.projectRoles);
      const defaultProject = projectIds[0] ?? projects[0]?.id ?? null;

      set({
        user,
        isAuthenticated: true,
        currentProjectId: defaultProject,
        projectNames,
        allProjectIds,
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
          projectNames: {},
          allProjectIds: [],
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
          const projects = await fetchProjects();
          const projectNames = Object.fromEntries(projects.map(p => [p.id, p.name]));
          const allProjectIds = projects.map(p => p.id);
          const projectIds = Object.keys(user.projectRoles || {});
          const defaultProject = projectIds[0] ?? projects[0]?.id ?? null;
          set({
            user,
            isAuthenticated: true,
            currentProjectId: defaultProject,
            projectNames,
            allProjectIds,
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
