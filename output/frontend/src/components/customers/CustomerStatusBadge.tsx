import { cn } from '@/lib/utils';
import type { CustomerStatus } from '@/types';

export const STATUS_LABELS: Record<CustomerStatus, string> = {
  Lead: 'Lead',
  Active: 'Aktif',
  Inactive: 'Pasif',
  Churned: 'Kayıp',
};

const STATUS_CLASSES: Record<CustomerStatus, string> = {
  Lead: 'bg-indigo-500/15 text-indigo-400 border-indigo-500/30',
  Active: 'bg-green-500/15 text-green-400 border-green-500/30',
  Inactive: 'bg-amber-500/15 text-amber-400 border-amber-500/30',
  Churned: 'bg-red-500/15 text-red-400 border-red-500/30',
};

interface CustomerStatusBadgeProps {
  status: CustomerStatus;
  className?: string;
}

export function CustomerStatusBadge({ status, className }: CustomerStatusBadgeProps) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap',
        STATUS_CLASSES[status],
        className
      )}
    >
      {STATUS_LABELS[status]}
    </span>
  );
}
