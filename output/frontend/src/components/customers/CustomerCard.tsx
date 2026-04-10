import { useLocation, useNavigate } from 'react-router-dom';
import { Phone, Mail, StickyNote, MoreVertical, MapPin, FolderOpen, Clock } from 'lucide-react';
import { CustomerStatusBadge, CustomerLabelBadge } from './CustomerStatusBadge';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { useAuthStore } from '@/stores/authStore';
import type { Customer, ContactType } from '@/types';

const SEGMENT_LABELS: Record<string, string> = {
  SME: 'KOBİ',
  Enterprise: 'Kurumsal',
  Startup: 'Startup',
  Government: 'Kamu',
  Individual: 'Bireysel',
};

interface CustomerCardProps {
  customer: Customer;
  onQuickAction?: (customerId: string, type: ContactType) => void;
}

export function CustomerCard({ customer, onQuickAction }: CustomerCardProps) {
  const navigate = useNavigate();
  const location = useLocation();
  const { user, projectNames } = useAuthStore();
  const initials = customer.companyName.slice(0, 2).toUpperCase();
  // Show project badge for SuperAdmin who can see multiple projects
  const showProjectBadge = user?.isSuperAdmin && Object.keys(projectNames).length > 1;
  const projectName = projectNames[customer.projectId];

  // Pass the current list URL (with filters) as navigation state so the
  // detail page's "back" button can return to the same filtered view.
  function openDetail() {
    navigate(`/customers/${customer.id}`, {
      state: { from: location.pathname + location.search },
    });
  }

  return (
    <div
      onClick={openDetail}
      className="flex items-center justify-between p-4 rounded-lg border border-border hover:bg-muted/40 cursor-pointer transition-colors"
    >
      {/* Left: avatar + info */}
      <div className="flex items-center gap-3 min-w-0 flex-1">
        {customer.logoUrl ? (
          <img
            src={customer.logoUrl}
            alt={customer.companyName}
            className="w-10 h-10 rounded-full object-cover flex-shrink-0 border border-border"
          />
        ) : (
          <div className="w-10 h-10 rounded-full bg-primary/10 flex items-center justify-center flex-shrink-0">
            <span className="text-primary text-sm font-semibold">{initials}</span>
          </div>
        )}
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2 flex-wrap">
            <p className="font-medium text-foreground truncate leading-tight">
              {customer.companyName}
            </p>
            {customer.label && (
              <CustomerLabelBadge label={customer.label} className="hidden sm:inline-flex" />
            )}
          </div>
          <div className="flex items-center gap-2 mt-0.5">
            {customer.contactName && (
              <p className="text-sm text-muted-foreground truncate">
                {customer.contactName}
              </p>
            )}
            {customer.contactName && customer.phone && (
              <span className="text-muted-foreground/40 text-xs hidden sm:inline">·</span>
            )}
            {customer.phone && (
              <p className="text-sm text-muted-foreground truncate hidden sm:block">
                {customer.phone}
              </p>
            )}
            {!customer.contactName && !customer.phone && customer.email && (
              <p className="text-sm text-muted-foreground truncate">{customer.email}</p>
            )}
            {!customer.contactName && !customer.phone && !customer.email && (
              <p className="text-sm text-muted-foreground">—</p>
            )}
          </div>
        </div>
      </div>

      {/* Right: last activity + badges + actions */}
      <div
        className="flex items-center gap-2 flex-shrink-0 ml-3"
        onClick={(e) => e.stopPropagation()}
      >
        {customer.lastActivityDate && (
          <span className="text-xs text-muted-foreground hidden md:flex items-center gap-1" title="Son aktivite">
            <Clock className="h-3 w-3" />
            {new Intl.DateTimeFormat('tr-TR', { day: 'numeric', month: 'short' }).format(
              new Date(customer.lastActivityDate)
            )}
          </span>
        )}
        {showProjectBadge && projectName && (
          <span className="text-xs text-muted-foreground hidden lg:inline px-2 py-0.5 rounded-full border border-primary/30 bg-primary/5 gap-1 flex items-center">
            <FolderOpen className="h-3 w-3 inline" /> {projectName}
          </span>
        )}
        {customer.segment && (
          <span className="text-xs text-muted-foreground hidden lg:inline px-2 py-0.5 rounded-full border border-border">
            {SEGMENT_LABELS[customer.segment] ?? customer.segment}
          </span>
        )}
        {customer.assignedUserName && (
          <div className="hidden md:flex items-center gap-1">
            <div className="w-6 h-6 rounded-full bg-muted flex items-center justify-center">
              <span className="text-xs text-muted-foreground font-medium">
                {customer.assignedUserName[0]}
              </span>
            </div>
          </div>
        )}
        <CustomerStatusBadge status={customer.status} />
        {onQuickAction && (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button
                variant="ghost"
                size="sm"
                className="h-8 w-8 p-0 text-muted-foreground hover:text-foreground"
              >
                <MoreVertical className="h-4 w-4" />
                <span className="sr-only">Hızlı işlemler</span>
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-44">
              <DropdownMenuItem
                onClick={() => onQuickAction(customer.id, 'Call')}
                className="gap-2"
              >
                <Phone className="h-4 w-4" />
                Arama Kaydet
              </DropdownMenuItem>
              <DropdownMenuItem
                onClick={() => onQuickAction(customer.id, 'Email')}
                className="gap-2"
              >
                <Mail className="h-4 w-4" />
                E-posta Kaydet
              </DropdownMenuItem>
              <DropdownMenuItem
                onClick={() => onQuickAction(customer.id, 'Visit')}
                className="gap-2"
              >
                <MapPin className="h-4 w-4" />
                Ziyaret Ekle
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              <DropdownMenuItem
                onClick={() => onQuickAction(customer.id, 'Note')}
                className="gap-2"
              >
                <StickyNote className="h-4 w-4" />
                Not Ekle
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        )}
      </div>
    </div>
  );
}
