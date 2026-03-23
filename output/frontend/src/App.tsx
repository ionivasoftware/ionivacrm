import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { useEffect } from 'react';
import { ProtectedRoute } from '@/components/auth/ProtectedRoute';
import { MainLayout } from '@/components/layout/MainLayout';
import { LoginPage } from '@/pages/LoginPage';
import { DashboardPage } from '@/pages/DashboardPage';
import { CustomersPage } from '@/pages/customers/CustomersPage';
import { CustomerDetailPage } from '@/pages/customers/CustomerDetailPage';
import { PlaceholderPage } from '@/pages/PlaceholderPage';
import { useAuthStore } from '@/stores/authStore';
import { useThemeStore } from '@/stores/themeStore';

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
      <AppInitializer>
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

            {/* Other feature routes */}
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

            {/* SuperAdmin routes */}
            <Route
              path="/admin/users"
              element={
                <ProtectedRoute superAdminOnly>
                  <PlaceholderPage
                    title="Kullanıcı Yönetimi"
                    description="Kullanıcı yönetimi modülü yakında aktif olacak."
                  />
                </ProtectedRoute>
              }
            />
            <Route
              path="/admin/projects"
              element={
                <ProtectedRoute superAdminOnly>
                  <PlaceholderPage
                    title="Proje Yönetimi"
                    description="Proje yönetimi modülü yakında aktif olacak."
                  />
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
      </AppInitializer>
    </BrowserRouter>
  );
}
