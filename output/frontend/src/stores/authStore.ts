import { create } from 'zustand';
import { subscribeWithSelector } from 'zustand/middleware';
import type { AuthUser, LoginRequest, LoginResponse, Project, UserRole } from '@/types';
import { apiClient, setAccessToken } from '@/api/client';
import type { ApiResponse } from '@/types';

// ── Helpers ───────────────────────────────────────────────────────────────────

/** Backend returns projectRoles as an array [{projectId, projectName, role}].
 *  AuthUser.projectRoles is typed as Record<string, UserRole> (dict).
 *  This helper normalises both formats to the dict format. */
function normalizeProjectRoles(raw: unknown): Record<string, UserRole> {
  if (!raw) return {};
  if (Array.isArray(raw)) {
    return Object.fromEntries(
      (raw as { projectId: string; role: string }[]).map((r) => [r.projectId, r.role as UserRole])
    );
  }
  if (typeof raw === 'object') return raw as Record<string, UserRole>;
  return {};
}

/** Extracts projectId→name map from the raw projectRoles array in the login response. */
function projectNamesFromRoles(raw: unknown): Record<string, string> {
  if (!Array.isArray(raw)) return {};
  return Object.fromEntries(
    (raw as { projectId: string; projectName: string }[])
      .filter((r) => r.projectName)
      .map((r) => [r.projectId, r.projectName])
  );
}

async function fetchAllProjects(): Promise<Project[]> {
  try {
    const res = await apiClient.get<ApiResponse<Project[]>>('/projects');
    return res.data.data ?? [];
  } catch {
    return [];
  }
}

// ── Store ─────────────────────────────────────────────────────────────────────

interface AuthState {
  user: AuthUser | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  currentProjectId: string | null;
  projectNames: Record<string, string>;
  /** All project IDs — for SuperAdmin includes all projects; for others only own projects */
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

      const { accessToken, user: rawUser } = response.data.data;
      setAccessToken(accessToken);

      // Normalise projectRoles: backend may return array [{projectId, projectName, role}]
      const rawRoles = (rawUser as any).projectRoles;
      const projectRoles = normalizeProjectRoles(rawRoles);
      const user: AuthUser = { ...rawUser, projectRoles };

      // Build projectNames from the login response (no extra API call needed for non-admins)
      let projectNames = projectNamesFromRoles(rawRoles);
      let allProjectIds = Object.keys(projectRoles);

      // SuperAdmin: also fetch all projects so the switcher shows every tenant
      if (user.isSuperAdmin) {
        const projects = await fetchAllProjects();
        if (projects.length > 0) {
          projectNames = Object.fromEntries(projects.map((p) => [p.id, p.name]));
          allProjectIds = projects.map((p) => p.id);
        }
      }

      const defaultProject = allProjectIds[0] ?? null;

      localStorage.setItem('accessToken', accessToken);
      localStorage.setItem('user', JSON.stringify(user)); // store normalised form

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
        const savedToken = localStorage.getItem('accessToken');
        const savedUser = localStorage.getItem('user');
        if (savedToken && savedUser) {
          const rawUser = JSON.parse(savedUser);
          setAccessToken(savedToken);

          // Handle both old (array) and new (dict) formats stored in localStorage
          const rawRoles = rawUser.projectRoles;
          const projectRoles = normalizeProjectRoles(rawRoles);
          const user: AuthUser = { ...rawUser, projectRoles };

          let projectNames = projectNamesFromRoles(rawRoles);
          let allProjectIds = Object.keys(projectRoles);

          if (user.isSuperAdmin) {
            const projects = await fetchAllProjects();
            if (projects.length > 0) {
              projectNames = Object.fromEntries(projects.map((p) => [p.id, p.name]));
              allProjectIds = projects.map((p) => p.id);
            }
          }

          const projectIds = Object.keys(projectRoles);
          const defaultProject = projectIds[0] ?? allProjectIds[0] ?? null;

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
