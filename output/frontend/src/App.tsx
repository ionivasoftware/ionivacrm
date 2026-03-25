import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { lazy, Suspense, useEffect } from 'react';
import { ProtectedRoute } from '@/components/auth/ProtectedRoute';
import { MainLayout } from '@/components/layout/MainLayout';
import { Toaster } from '@/components/ui/toaster';
import { useAuthStore } from '@/stores/authStore';
import { useThemeStore } from '@/stores/themeStore';

// ── Lazy-loaded pages (code splitting) ──────────────────────────────────────
const LoginPage         = lazy(() => import('@/pages/LoginPage').then(m => ({ default: m.LoginPage })));
const DashboardPage     = lazy(() => import('@/pages/DashboardPage').then(m => ({ default: m.DashboardPage })));
const CustomersPage     = lazy(() => import('@/pages/customers/CustomersPage').then(m => ({ default: m.CustomersPage })));
const CustomerDetailPage = lazy(() => import('@/pages/customers/CustomerDetailPage').then(m => ({ default: m.CustomerDetailPage })));
const PlaceholderPage   = lazy(() => import('@/pages/PlaceholderPage').then(m => ({ default: m.PlaceholderPage })));
const ContactHistoriesPage = lazy(() => import('@/pages/ContactHistoriesPage').then(m => ({ default: m.ContactHistoriesPage })));
const UsersAdminPage    = lazy(() => import('@/pages/admin/UsersAdminPage').then(m => ({ default: m.UsersAdminPage })));
const ProjectsAdminPage = lazy(() => import('@/pages/admin/ProjectsAdminPage').then(m => ({ default: m.ProjectsAdminPage })));

// ── Page-level loading fallback ───────────────────────────────────────────────
function PageLoader() {
  return (
    <div className="flex items-center justify-center min-h-[60vh]">
      <div className="flex flex-col items-center gap-4">
        <div className="w-8 h-8 border-4 border-primary border-t-transparent rounded-full animate-spin" />
        <p className="text-muted-foreground text-sm">Yükleniyor...</p>
      </div>
    </div>
  );
}

function AppInitializer({ children }: { children: React.ReactNode }) {
  const initializeAuth = useAuthStore((s) => s.initializeAuth);
  const { theme } = useThemeStore();

  useEffect(() => {
    // Apply theme on mount
    const root = document.documentElement;
    if (theme === 'light') {
      root.classList.remove('dark');
      root.classList.add('light');
    } else {
      root.classList.remove('light');
      root.classList.add('dark');
    }

    // Initialize auth (try refresh token)
    initializeAuth();
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  return <>{children}</>;
}

export default function App() {
  return (
    <BrowserRouter>
      {/* Global toast provider — must be outside Router so toasts appear everywhere */}
      <Toaster />
      <AppInitializer>
        <Suspense fallback={<PageLoader />}>
          <Routes>
            {/* Public routes */}
            <Route path="/login" element={<LoginPage />} />
            <Route path="/" element={<Navigate to="/dashboard" replace />} />

            {/* Protected routes */}
            <Route
              element={
                <ProtectedRoute>
                  <MainLayout />
                </ProtectedRoute>
              }
            >
              <Route path="/dashboard" element={<DashboardPage />} />

              {/* Customer routes */}
              <Route path="/customers" element={<CustomersPage />} />
              <Route
                path="/customers/new"
                element={<Navigate to="/customers" replace />}
              />
              <Route path="/customers/:id" element={<CustomerDetailPage />} />
              <Route path="/contact-histories" element={<ContactHistoriesPage />} />

              {/* Feature routes — placeholder until Sprint 2+ */}
              <Route
                path="/pipeline"
                element={
                  <PlaceholderPage
                    title="Pipeline"
                    description="Kanban tabanlı satış pipeline'ı yakında aktif olacak."
                  />
                }
              />
              <Route
                path="/tasks"
                element={
                  <PlaceholderPage
                    title="Görevler"
                    description="Görev yönetimi sayfası yakında aktif olacak."
                  />
                }
              />
              <Route
                path="/reports"
                element={
                  <PlaceholderPage
                    title="Raporlar"
                    description="Raporlama modülü yakında aktif olacak."
                  />
                }
              />
              <Route
                path="/settings"
                element={
                  <PlaceholderPage
                    title="Ayarlar"
                    description="Ayarlar sayfası yakında aktif olacak."
                  />
                }
              />

              {/* SuperAdmin-only routes */}
              <Route
                path="/admin/users"
                element={
                  <ProtectedRoute superAdminOnly>
                    <UsersAdminPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="/admin/projects"
                element={
                  <ProtectedRoute superAdminOnly>
                    <ProjectsAdminPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="/sync/logs"
                element={
                  <ProtectedRoute superAdminOnly>
                    <PlaceholderPage
                      title="Senkronizasyon Logları"
                      description="Sync log modülü yakında aktif olacak."
                    />
                  </ProtectedRoute>
                }
              />
            </Route>

            {/* 404 */}
            <Route
              path="*"
              element={
                <div className="flex flex-col items-center justify-center min-h-screen bg-background text-center p-4">
                  <h1 className="text-6xl font-bold text-muted-foreground/30 mb-4">404</h1>
                  <p className="text-xl font-semibold text-foreground mb-2">Sayfa Bulunamadı</p>
                  <p className="text-muted-foreground mb-6">
                    Aradığınız sayfa mevcut değil veya taşınmış olabilir.
                  </p>
                  <a href="/dashboard" className="text-primary hover:underline">
                    Gösterge Paneline Dön
                  </a>
                </div>
              }
            />
          </Routes>
        </Suspense>
      </AppInitializer>
    </BrowserRouter>
  );
}
