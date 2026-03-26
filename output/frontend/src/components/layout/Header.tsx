import { Menu, Bell, Sun, Moon, ChevronDown, LogOut, Settings, User } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Avatar, AvatarFallback } from '@/components/ui/avatar';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { useAuthStore } from '@/stores/authStore';
import { useThemeStore } from '@/stores/themeStore';
import { useNavigate } from 'react-router-dom';
import { Badge } from '@/components/ui/badge';
import { useNotifications } from '@/api/dashboard';

const CONTACT_TYPE_LABELS: Record<string, string> = {
  Call: 'Arama',
  Email: 'E-posta',
  Meeting: 'Toplantı',
  Note: 'Not',
  WhatsApp: 'WhatsApp',
  Visit: 'Ziyaret',
};

function formatNotifDate(dateStr: string): string {
  return new Intl.DateTimeFormat('tr-TR', {
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(dateStr));
}

interface HeaderProps {
  onMobileMenuOpen: () => void;
}

export function Header({ onMobileMenuOpen }: HeaderProps) {
  const { user, logout, currentProjectId, setCurrentProject, projectNames } = useAuthStore();
  const { theme, toggleTheme } = useThemeStore();
  const navigate = useNavigate();
  const { data: notifications = [] } = useNotifications();

  // SuperAdmin can switch between ALL projects; regular users only their own
  const projectIds = user?.isSuperAdmin
    ? Object.keys(projectNames)
    : (user ? Object.keys(user.projectRoles) : []);
  const currentRole = currentProjectId ? user?.projectRoles[currentProjectId] : null;

  async function handleLogout() {
    await logout();
    navigate('/login');
  }

  return (
    <header className="h-16 bg-card border-b border-border flex items-center justify-between px-4 flex-shrink-0">
      {/* Left: Mobile menu + breadcrumb */}
      <div className="flex items-center gap-3">
        <Button
          variant="ghost"
          size="icon"
          onClick={onMobileMenuOpen}
          className="md:hidden h-11 w-11 text-muted-foreground"
          aria-label="Menüyü aç"
        >
          <Menu className="h-5 w-5" />
        </Button>

        {/* Project switcher (SuperAdmin or multi-project users) */}
        {(user?.isSuperAdmin || projectIds.length > 1) && currentProjectId && (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="outline" className="h-9 gap-2 text-sm border-border">
                <span className="max-w-[150px] truncate">
                  {(currentProjectId && projectNames[currentProjectId]) || currentProjectId}
                </span>
                {currentRole && (
                  <Badge variant="secondary" className="text-xs">
                    {currentRole}
                  </Badge>
                )}
                <ChevronDown className="h-3 w-3 opacity-50" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start" className="w-56">
              <DropdownMenuLabel>Proje Seç</DropdownMenuLabel>
              <DropdownMenuSeparator />
              {projectIds.map((pid) => (
                <DropdownMenuItem
                  key={pid}
                  onClick={() => setCurrentProject(pid)}
                  className="flex items-center justify-between"
                >
                  <span className="truncate">{projectNames[pid] || pid}</span>
                  {pid === currentProjectId && (
                    <span className="text-primary text-xs">✓</span>
                  )}
                </DropdownMenuItem>
              ))}
            </DropdownMenuContent>
          </DropdownMenu>
        )}
      </div>

      {/* Right: actions */}
      <div className="flex items-center gap-2">
        {/* Theme toggle */}
        <Button
          variant="ghost"
          size="icon"
          onClick={toggleTheme}
          className="h-11 w-11 text-muted-foreground hover:text-foreground"
          aria-label={theme === 'dark' ? 'Açık tema' : 'Koyu tema'}
        >
          {theme === 'dark' ? <Sun className="h-5 w-5" /> : <Moon className="h-5 w-5" />}
        </Button>

        {/* Notifications dropdown */}
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button
              variant="ghost"
              size="icon"
              className="h-11 w-11 text-muted-foreground hover:text-foreground relative"
              aria-label="Bildirimler"
            >
              <Bell className="h-5 w-5" />
              {notifications.length > 0 && (
                <span className="absolute top-2 right-2 w-2 h-2 bg-primary rounded-full" />
              )}
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="w-80">
            <DropdownMenuLabel className="flex items-center justify-between">
              <span>Son Aktiviteler</span>
              <span className="text-xs font-normal text-muted-foreground">{notifications.length} kayıt</span>
            </DropdownMenuLabel>
            <DropdownMenuSeparator />
            {notifications.length === 0 ? (
              <div className="py-6 text-center">
                <p className="text-sm text-muted-foreground">Henüz aktivite yok.</p>
              </div>
            ) : (
              <div className="max-h-[360px] overflow-y-auto">
                {notifications.map((notif) => (
                  <div
                    key={notif.id}
                    className="flex items-start gap-3 px-3 py-2.5 hover:bg-muted/50 transition-colors"
                  >
                    <div className="w-7 h-7 rounded-full bg-primary/10 flex items-center justify-center flex-shrink-0 mt-0.5">
                      <span className="text-primary text-[10px] font-semibold">
                        {CONTACT_TYPE_LABELS[notif.type]?.[0] ?? '?'}
                      </span>
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center justify-between gap-1">
                        <p className="text-xs font-medium text-foreground truncate">
                          {notif.customerName}
                        </p>
                        <Badge variant="secondary" className="text-[10px] px-1.5 py-0 flex-shrink-0">
                          {CONTACT_TYPE_LABELS[notif.type]}
                        </Badge>
                      </div>
                      {notif.subject && (
                        <p className="text-xs text-muted-foreground truncate mt-0.5">{notif.subject}</p>
                      )}
                      <p className="text-[10px] text-muted-foreground mt-0.5">
                        {notif.createdByUserName && `${notif.createdByUserName} · `}
                        {formatNotifDate(notif.contactedAt)}
                      </p>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </DropdownMenuContent>
        </DropdownMenu>

        {/* User menu */}
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button
              variant="ghost"
              className="h-11 gap-2 pl-2 pr-3 text-muted-foreground hover:text-foreground"
            >
              <Avatar className="h-8 w-8">
                <AvatarFallback className="bg-primary/20 text-primary text-xs font-medium">
                  {user?.firstName?.[0]}{user?.lastName?.[0]}
                </AvatarFallback>
              </Avatar>
              <span className="hidden sm:block text-sm font-medium text-foreground">
                {user?.firstName} {user?.lastName}
              </span>
              <ChevronDown className="h-3 w-3 opacity-50" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="w-56">
            <DropdownMenuLabel>
              <div>
                <p className="font-medium">{user?.firstName} {user?.lastName}</p>
                <p className="text-xs text-muted-foreground font-normal">{user?.email}</p>
                {user?.isSuperAdmin && (
                  <Badge className="mt-1 text-xs" variant="secondary">SuperAdmin</Badge>
                )}
              </div>
            </DropdownMenuLabel>
            <DropdownMenuSeparator />
            <DropdownMenuItem onClick={() => navigate('/settings')}>
              <User className="mr-2 h-4 w-4" />
              Profil
            </DropdownMenuItem>
            <DropdownMenuItem onClick={() => navigate('/settings')}>
              <Settings className="mr-2 h-4 w-4" />
              Ayarlar
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem
              onClick={handleLogout}
              className="text-destructive focus:text-destructive"
            >
              <LogOut className="mr-2 h-4 w-4" />
              Çıkış Yap
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    </header>
  );
}
