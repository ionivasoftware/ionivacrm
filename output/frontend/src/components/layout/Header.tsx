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

interface HeaderProps {
  onMobileMenuOpen: () => void;
}

export function Header({ onMobileMenuOpen }: HeaderProps) {
  const { user, logout, currentProjectId, setCurrentProject } = useAuthStore();
  const { theme, toggleTheme } = useThemeStore();
  const navigate = useNavigate();

  const projectIds = user ? Object.keys(user.projectRoles) : [];
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
                  {currentProjectId}
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
                  <span className="truncate">{pid}</span>
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

        {/* Notifications */}
        <Button
          variant="ghost"
          size="icon"
          className="h-11 w-11 text-muted-foreground hover:text-foreground relative"
          aria-label="Bildirimler"
        >
          <Bell className="h-5 w-5" />
          <span className="absolute top-2 right-2 w-2 h-2 bg-primary rounded-full" />
        </Button>

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
