import { useAuthStore } from '@/stores/authStore';

/**
 * Returns true if the current user can access finance/accounting features.
 * Allowed: SuperAdmin, or the Accounting role in ANY project.
 *
 * Deliberately any-project, matching the backend "VendorInvoiceAccess" policy (which scans the whole
 * roles claim). Gating on the *selected* project instead would hide the screen from someone who is
 * Accounting in project B while project A happens to be selected — and the default selection is just
 * the first role row, whose order the API does not guarantee.
 */
export function useCanAccessFinance(): boolean {
  const { user } = useAuthStore();
  if (user?.isSuperAdmin) return true;
  return Object.values(user?.projectRoles ?? {}).includes('Accounting');
}
