import { NavLink, useLocation } from 'react-router-dom';
import {
  LayoutDashboard,
  Users,
  KanbanSquare,
  CheckSquare,
  BarChart3,
  Settings,
  RefreshCw,
  UserCog,
  FolderKanban,
  ChevronLeft,
  ChevronRight,
  FileText,
  ShieldAlert,
  Receipt,
  Tags,
  MessageSquareText,
  MessageCircle,
} from 'lucide-react';
import { cn } from '@/lib/utils';
import { useAuthStore } from '@/stores/authStore';
import { useCanAccessFinance } from '@/lib/roles';
import { useMissingInvoiceCount } from '@/api/vendorInvoices';
import { Button } from '@/components/ui/button';

interface SidebarProps {
  isCollapsed: boolean;
  onToggle: () => void;
  onClose?: () => void;
  isMobile?: boolean;
}

interface NavItem {
  label: string;
  href: string;
  icon: React.ComponentType<{ className?: string }>;
  superAdminOnly?: boolean;
  financeOnly?: boolean;
  /** When set, this nav item shows a live count badge (e.g. missing invoices). */
  badgeKey?: 'missingInvoices';
}

const navItems: NavItem[] = [
  { label: 'Gösterge Paneli', href: '/dashboard', icon: LayoutDashboard },
  { label: 'Müşteriler', href: '/customers', icon: Users },
  { label: 'Pipeline', href: '/pipeline', icon: KanbanSquare },
  { label: 'Görevler', href: '/tasks', icon: CheckSquare },
  { label: 'Gelen Faturalar', href: '/admin/vendor-invoices', icon: Receipt, financeOnly: true, badgeKey: 'missingInvoices' },
  { label: 'Faturalar', href: '/invoices', icon: FileText, financeOnly: true },
  { label: 'Raporlar', href: '/reports', icon: BarChart3 },
  { label: 'Ayarlar', href: '/settings', icon: Settings },
];

const adminNavItems: NavItem[] = [
  { label: 'Kullanıcı Yönetimi', href: '/admin/users', icon: UserCog, superAdminOnly: true },
  { label: 'Proje Yönetimi', href: '/admin/projects', icon: FolderKanban, superAdminOnly: true },
  { label: 'Senkronizasyon', href: '/sync/logs', icon: RefreshCw, superAdminOnly: true },
  { label: 'Hata Onayları', href: '/admin/error-triage', icon: ShieldAlert, superAdminOnly: true },
  { label: 'Talep & Öneriler', href: '/admin/tickets', icon: MessageSquareText, superAdminOnly: true },
  { label: 'Destek Sohbetleri', href: '/admin/support-chat-logs', icon: MessageCircle, superAdminOnly: true },
  { label: 'Fiyat Yönetimi', href: '/admin/pricing', icon: Tags, superAdminOnly: true },
];

export function Sidebar({ isCollapsed, onToggle, onClose, isMobile = false }: SidebarProps) {
  const { user } = useAuthStore();
  const location = useLocation();
  const canAccessFinance = useCanAccessFinance();

  // Live missing-invoice alarm count — for users who can access the invoices screen (SuperAdmin/Accounting).
  const { data: missingInvoices = 0 } = useMissingInvoiceCount(canAccessFinance);

  const badgeFor = (item: NavItem): number | undefined =>
    item.badgeKey === 'missingInvoices' && canAccessFinance ? missingInvoices : undefined;

  const filteredNavItems = navItems.filter(
    (item) => !item.financeOnly || canAccessFinance
  );

  const filteredAdminItems = adminNavItems.filter(
    (item) => !item.superAdminOnly || user?.isSuperAdmin
  );

  return (
    <aside
      className={cn(
        'flex flex-col h-full bg-card border-r border-border transition-all duration-300',
        isCollapsed ? 'w-16' : 'w-64'
      )}
    >
      {/* Logo */}
      <div className={cn(
        'flex items-center h-16 px-4 border-b border-border flex-shrink-0',
        isCollapsed ? 'justify-center' : 'justify-between'
      )}>
        {!isCollapsed && (
          <div className="flex items-center gap-2.5">
            <img src="/logo.png" alt="IONIVA" className="h-8 object-contain" />
            <div className="h-5 w-px bg-border" />
            <span className="text-sm font-semibold text-muted-foreground tracking-wide">CRM</span>
          </div>
        )}
        {isCollapsed && (
          <img src="/logo.png" alt="IONIVA" className="h-7 w-8 object-contain object-left" />
        )}
        {!isMobile && (
          <Button
            variant="ghost"
            size="icon"
            onClick={onToggle}
            className={cn(
              'h-8 w-8 text-muted-foreground hover:text-foreground',
              isCollapsed && 'mt-4 mx-auto'
            )}
            aria-label={isCollapsed ? 'Kenar çubuğunu genişlet' : 'Kenar çubuğunu daralt'}
          >
            {isCollapsed ? (
              <ChevronRight className="h-4 w-4" />
            ) : (
              <ChevronLeft className="h-4 w-4" />
            )}
          </Button>
        )}
        {isMobile && (
          <Button
            variant="ghost"
            size="icon"
            onClick={onClose}
            className="h-8 w-8 text-muted-foreground hover:text-foreground"
            aria-label="Menüyü kapat"
          >
            <ChevronLeft className="h-4 w-4" />
          </Button>
        )}
      </div>

      {/* Nav Items */}
      <nav className="flex-1 overflow-y-auto py-4 px-2 space-y-1">
        {filteredNavItems.map((item) => (
          <SidebarNavItem
            key={item.href}
            item={item}
            isCollapsed={isCollapsed}
            isActive={location.pathname.startsWith(item.href)}
            onClick={isMobile ? onClose : undefined}
          />
        ))}

        {filteredAdminItems.length > 0 && (
          <>
            <div className={cn(
              'pt-4 pb-2',
              isCollapsed ? 'px-0' : 'px-2'
            )}>
              {!isCollapsed && (
                <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
                  Yönetim
                </p>
              )}
              {isCollapsed && <div className="border-t border-border" />}
            </div>
            {filteredAdminItems.map((item) => (
              <SidebarNavItem
                key={item.href}
                item={item}
                isCollapsed={isCollapsed}
                isActive={location.pathname.startsWith(item.href)}
                badge={badgeFor(item)}
                onClick={isMobile ? onClose : undefined}
              />
            ))}
          </>
        )}
      </nav>

      {/* User info at bottom */}
      {!isCollapsed && user && (
        <div className="p-4 border-t border-border">
          <div className="flex items-center gap-3">
            <div className="w-8 h-8 rounded-full bg-primary/20 flex items-center justify-center flex-shrink-0">
              <span className="text-primary text-xs font-medium">
                {user.firstName[0]}{user.lastName[0]}
              </span>
            </div>
            <div className="min-w-0">
              <p className="text-sm font-medium text-foreground truncate">
                {user.firstName} {user.lastName}
              </p>
              <p className="text-xs text-muted-foreground truncate">{user.email}</p>
            </div>
          </div>
        </div>
      )}
    </aside>
  );
}

interface SidebarNavItemProps {
  item: NavItem;
  isCollapsed: boolean;
  isActive: boolean;
  badge?: number;
  onClick?: () => void;
}

function SidebarNavItem({ item, isCollapsed, isActive, badge, onClick }: SidebarNavItemProps) {
  const Icon = item.icon;
  const showBadge = typeof badge === 'number' && badge > 0;

  return (
    <NavLink
      to={item.href}
      onClick={onClick}
      className={cn(
        'relative flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition-colors',
        'min-h-[44px]',
        isActive
          ? 'bg-primary text-primary-foreground'
          : 'text-muted-foreground hover:text-foreground hover:bg-accent',
        isCollapsed && 'justify-center px-2'
      )}
      title={isCollapsed ? item.label : undefined}
    >
      <div className="relative flex-shrink-0">
        <Icon className="h-5 w-5" />
        {showBadge && isCollapsed && (
          <span className="absolute -top-1.5 -right-1.5 h-2 w-2 rounded-full bg-red-500 ring-2 ring-card" />
        )}
      </div>
      {!isCollapsed && <span className="truncate">{item.label}</span>}
      {showBadge && !isCollapsed && (
        <span className="ml-auto inline-flex items-center justify-center rounded-full bg-red-500 px-1.5 min-w-[1.25rem] h-5 text-xs font-semibold text-white">
          {badge}
        </span>
      )}
    </NavLink>
  );
}
