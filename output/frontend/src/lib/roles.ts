import { useAuthStore } from '@/stores/authStore';

/**
 * Returns true if the current user can access finance/accounting features.
 * Allowed roles: SuperAdmin (all projects) or Accounting role in currentProject.
 */
export function useCanAccessFinance(): boolean {
  const { user, currentProjectId } = useAuthStore();
  if (user?.isSuperAdmin) return true;
  if (!currentProjectId) return false;
  return user?.projectRoles[currentProjectId] === 'Accounting';
}
