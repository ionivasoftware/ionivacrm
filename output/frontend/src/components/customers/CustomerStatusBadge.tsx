import { cn } from '@/lib/utils';
import type { CustomerStatus, CustomerLabel } from '@/types';

export const STATUS_LABELS: Record<CustomerStatus, string> = {
  Lead: 'Lead',
  Active: 'Aktif',
  Demo: 'Demo',
  Churned: 'Kayıp',
  Passive: 'Pasif',
};

const STATUS_CLASSES: Record<CustomerStatus, string> = {
  Lead: 'bg-indigo-500/15 text-indigo-400 border-indigo-500/30',
  Active: 'bg-green-500/15 text-green-400 border-green-500/30',
  Demo: 'bg-violet-500/15 text-violet-400 border-violet-500/30',
  Churned: 'bg-red-500/15 text-red-400 border-red-500/30',
  Passive: 'bg-slate-500/15 text-slate-400 border-slate-500/30',
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

// ── Label Badge ───────────────────────────────────────────────────────────────

export const LABEL_LABELS: Record<CustomerLabel, string> = {
  YuksekPotansiyel: '⭐ Yüksek Potansiyel',
  Potansiyel: '🔵 Potansiyel',
  Notr: '⚪ Nötr',
  Vasat: '🟡 Vasat',
  Kotu: '🔴 Kötü',
};

const LABEL_CLASSES: Record<CustomerLabel, string> = {
  YuksekPotansiyel: 'bg-yellow-500/15 text-yellow-400 border-yellow-500/30',
  Potansiyel: 'bg-blue-500/15 text-blue-400 border-blue-500/30',
  Notr: 'bg-slate-500/15 text-slate-400 border-slate-500/30',
  Vasat: 'bg-amber-500/15 text-amber-400 border-amber-500/30',
  Kotu: 'bg-red-500/15 text-red-400 border-red-500/30',
};

interface CustomerLabelBadgeProps {
  label: CustomerLabel;
  className?: string;
}

export function CustomerLabelBadge({ label, className }: CustomerLabelBadgeProps) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap',
        LABEL_CLASSES[label],
        className
      )}
    >
      {LABEL_LABELS[label]}
    </span>
  );
}
