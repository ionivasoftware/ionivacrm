import { useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import {
  Plus,
  Search,
  Filter,
  Upload,
  Download,
  ChevronLeft,
  ChevronRight,
  Users,
  X,
  MessageSquare,
} from 'lucide-react';
import { Card, CardContent, CardHeader } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCustomers } from '@/api/customers';
import { useAdminProjects } from '@/api/admin';
import { CustomerCard } from '@/components/customers/CustomerCard';
import { CustomerFormDialog } from '@/components/customers/CustomerForm';
import { RezervalCustomerFormDialog } from '@/components/customers/RezervalCustomerFormDialog';
import { AddContactHistoryDialog } from '@/components/customers/AddContactHistoryDialog';
import { useToast } from '@/hooks/use-toast';
import { useAuthStore } from '@/stores/authStore';
import { getSegmentsForProject } from '@/config/projectSegments';
import type { CustomerStatus, CustomerLabel, ContactType } from '@/types';

// ── Types ─────────────────────────────────────────────────────────────────────

interface QuickActionState {
  customerId: string;
  type: ContactType;
}

// ── Component ─────────────────────────────────────────────────────────────────

export function CustomersPage() {
  const { toast } = useToast();
  const navigate = useNavigate();

  // ── Filter / pagination state lives in the URL ──────────────────────────
  // Persists across navigation (customer detail back button), refresh, browser
  // history navigation, and is bookmark/shareable. Replaces useState so the
  // detail-page round trip no longer resets the user's filters.
  const [searchParams, setSearchParams] = useSearchParams();

  const search        = searchParams.get('q')       ?? '';
  const statusFilter  = (searchParams.get('status')  ?? 'all') as CustomerStatus | 'all';
  const segmentFilter = searchParams.get('segment') ?? 'all';
  const labelFilter   = (searchParams.get('label')   ?? 'all') as CustomerLabel | 'all';
  const sortBy        = searchParams.get('sort')     ?? 'activity_desc';
  const page          = Math.max(1, Number(searchParams.get('page') ?? '1') || 1);

  /**
   * Patches the URL search params. Empty string / 'all' / undefined values are
   * removed so the URL stays clean. By default also resets `page` to 1 since
   * any filter change should restart pagination — pass `resetPage: false` for
   * pure paging clicks.
   */
  function patchParams(updates: Record<string, string | undefined>, resetPage = true) {
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        for (const [key, value] of Object.entries(updates)) {
          if (value === undefined || value === '' || value === 'all') {
            next.delete(key);
          } else {
            next.set(key, value);
          }
        }
        if (resetPage) next.delete('page');
        return next;
      },
      { replace: true },
    );
  }

  const { currentProjectId, projectNames, projects: storeProjects } = useAuthStore();
  const projectName = currentProjectId ? projectNames[currentProjectId] : undefined;
  const segments = getSegmentsForProject(projectName);

  // Detect if the currently selected project is a RezervAl project.
  // SuperAdmin has full project objects in storeProjects; other users need a separate fetch.
  const { data: adminProjects } = useAdminProjects();
  const allProjects = storeProjects.length > 0 ? storeProjects : adminProjects ?? [];
  const currentProject = allProjects.find((p) => p.id === currentProjectId);
  const isRezervalProject = !!(
    (currentProject as { rezervAlApiKey?: string | null } | undefined)?.rezervAlApiKey
  );

  // Filter bar visibility — start expanded if any filter is already active so the
  // user immediately sees what's applied after returning from a customer detail.
  const initialFiltersOpen =
    statusFilter !== 'all' || segmentFilter !== 'all' || labelFilter !== 'all';
  const [showFilters, setShowFilters] = useState(initialFiltersOpen);

  const PAGE_SIZE = 20;

  // Dialog state
  const [showCreateDialog, setShowCreateDialog] = useState(false);
  const [quickAction, setQuickAction] = useState<QuickActionState | null>(null);

  const { data, isLoading, isError } = useCustomers({
    search: search.trim() || undefined,
    status: statusFilter !== 'all' ? statusFilter : undefined,
    segment: segmentFilter !== 'all' ? segmentFilter : undefined,
    label: labelFilter !== 'all' ? labelFilter : undefined,
    sortBy,
    page,
    pageSize: PAGE_SIZE,
  });

  const hasActiveFilters = statusFilter !== 'all' || segmentFilter !== 'all' || labelFilter !== 'all';
  const activeFilterCount = [
    statusFilter !== 'all',
    segmentFilter !== 'all',
    labelFilter !== 'all',
  ].filter(Boolean).length;

  function handleSearch(value: string) {
    patchParams({ q: value });
  }

  function handleStatusChange(value: string) {
    patchParams({ status: value });
  }

  function handleSegmentChange(value: string) {
    patchParams({ segment: value });
  }

  function handleLabelChange(value: string) {
    patchParams({ label: value });
  }

  function clearFilters() {
    patchParams({ status: undefined, segment: undefined, label: undefined });
  }

  function goToPage(nextPage: number) {
    patchParams({ page: String(nextPage) }, /* resetPage */ false);
  }

  function handleQuickAction(customerId: string, type: ContactType) {
    setQuickAction({ customerId, type });
  }

  function handleImport() {
    toast({ title: 'İçe aktarma', description: 'Bu özellik yakında kullanıma açılacak.' });
  }

  function handleExport() {
    toast({ title: 'Dışa aktarma', description: 'Bu özellik yakında kullanıma açılacak.' });
  }

  const rangeStart = data ? (page - 1) * PAGE_SIZE + 1 : 0;
  const rangeEnd = data ? Math.min(page * PAGE_SIZE, data.totalCount) : 0;

  return (
    <div className="space-y-6">
      {/* ── Page Header ── */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold text-foreground">Müşteriler</h1>
          <p className="text-muted-foreground text-sm mt-1">
            {isLoading
              ? 'Yükleniyor...'
              : data
              ? `${data.totalCount.toLocaleString('tr-TR')} müşteri ve lead kaydı`
              : 'Müşteri ve lead kayıtları'}
          </p>
        </div>
        <div className="flex items-center gap-2 flex-wrap">
          <Button
            variant="outline"
            size="sm"
            className="gap-2 h-10"
            onClick={() => navigate('/contact-histories')}
          >
            <MessageSquare className="h-4 w-4" />
            <span className="hidden sm:inline">Tüm Görüşmeler</span>
          </Button>
          <Button
            variant="outline"
            size="sm"
            className="gap-2 h-10"
            onClick={handleImport}
          >
            <Upload className="h-4 w-4" />
            <span className="hidden sm:inline">İçe Aktar</span>
          </Button>
          <Button
            variant="outline"
            size="sm"
            className="gap-2 h-10"
            onClick={handleExport}
          >
            <Download className="h-4 w-4" />
            <span className="hidden sm:inline">Dışa Aktar</span>
          </Button>
          <Button
            onClick={() => setShowCreateDialog(true)}
            className="gap-2 h-10"
          >
            <Plus className="h-4 w-4" />
            Yeni Müşteri
          </Button>
        </div>
      </div>

      {/* ── Search + Filter Card ── */}
      <Card>
        <CardHeader className="pb-4">
          {/* Search row */}
          <div className="flex flex-col sm:flex-row gap-3">
            <div className="relative flex-1">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
              <Input
                placeholder="Şirket adı, kişi veya e-posta ara..."
                value={search}
                onChange={(e) => handleSearch(e.target.value)}
                className="pl-9 h-11"
              />
              {search && (
                <button
                  onClick={() => handleSearch('')}
                  className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground transition-colors"
                >
                  <X className="h-4 w-4" />
                </button>
              )}
            </div>
            <Select value={sortBy} onValueChange={(v) => patchParams({ sort: v })}>
              <SelectTrigger className="h-11 w-[180px]">
                <SelectValue placeholder="Sırala" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="activity_desc">Son Aktivite (Yeni)</SelectItem>
                <SelectItem value="activity">Son Aktivite (Eski)</SelectItem>
                <SelectItem value="name">İsim (A-Z)</SelectItem>
                <SelectItem value="name_desc">İsim (Z-A)</SelectItem>
                <SelectItem value="created_desc">Kayıt Tarihi (Yeni)</SelectItem>
                <SelectItem value="created">Kayıt Tarihi (Eski)</SelectItem>
              </SelectContent>
            </Select>
            <Button
              variant={showFilters ? 'secondary' : 'outline'}
              className="gap-2 h-11 relative"
              onClick={() => setShowFilters((v) => !v)}
            >
              <Filter className="h-4 w-4" />
              Filtrele
              {activeFilterCount > 0 && (
                <span className="absolute -top-1.5 -right-1.5 bg-primary text-primary-foreground rounded-full w-5 h-5 text-[10px] font-bold flex items-center justify-center">
                  {activeFilterCount}
                </span>
              )}
            </Button>
          </div>

          {/* Filter dropdowns (collapsible) */}
          {showFilters && (
            <div className="flex flex-col sm:flex-row gap-3 mt-4 pt-4 border-t border-border flex-wrap">
              <div className="flex-1 min-w-[160px]">
                <Select value={statusFilter} onValueChange={handleStatusChange}>
                  <SelectTrigger className="h-11">
                    <SelectValue placeholder="Tüm Durumlar" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">Tüm Durumlar</SelectItem>
                    <SelectItem value="Lead">🔵 Lead</SelectItem>
                    <SelectItem value="Active">🟢 Aktif</SelectItem>
                    <SelectItem value="Demo">🟣 Demo</SelectItem>
                    <SelectItem value="Churned">🔴 Kayıp</SelectItem>
                    <SelectItem value="Passive">⚫ Pasif</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="flex-1 min-w-[160px]">
                <Select value={segmentFilter} onValueChange={handleSegmentChange}>
                  <SelectTrigger className="h-11">
                    <SelectValue placeholder="Tüm Segmentler" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">Tüm Segmentler</SelectItem>
                    {segments.map((seg) => (
                      <SelectItem key={seg} value={seg}>{seg}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="flex-1 min-w-[160px]">
                <Select value={labelFilter} onValueChange={handleLabelChange}>
                  <SelectTrigger className="h-11">
                    <SelectValue placeholder="Tüm Labellar" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">Tüm Labellar</SelectItem>
                    <SelectItem value="YuksekPotansiyel">⭐ Yüksek Potansiyel</SelectItem>
                    <SelectItem value="Potansiyel">🔵 Potansiyel</SelectItem>
                    <SelectItem value="Notr">⚪ Nötr</SelectItem>
                    <SelectItem value="Vasat">🟡 Vasat</SelectItem>
                    <SelectItem value="Kotu">🔴 Kötü</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              {hasActiveFilters && (
                <Button
                  variant="ghost"
                  className="h-11 gap-2 text-muted-foreground hover:text-foreground"
                  onClick={clearFilters}
                >
                  <X className="h-4 w-4" />
                  Temizle
                </Button>
              )}
            </div>
          )}
        </CardHeader>

        <CardContent>
          {/* ── Loading ── */}
          {isLoading && (
            <div className="space-y-3">
              {Array.from({ length: 8 }).map((_, i) => (
                <Skeleton key={i} className="h-[72px] w-full rounded-lg" />
              ))}
            </div>
          )}

          {/* ── Error ── */}
          {isError && !isLoading && (
            <div className="flex flex-col items-center justify-center py-16 text-center">
              <div className="w-16 h-16 rounded-full bg-destructive/10 flex items-center justify-center mb-4">
                <X className="h-8 w-8 text-destructive/60" />
              </div>
              <p className="font-medium text-foreground">Veriler yüklenemedi</p>
              <p className="text-sm text-muted-foreground mt-1">
                Bağlantınızı kontrol edip sayfayı yenileyin
              </p>
            </div>
          )}

          {/* ── Empty state ── */}
          {!isLoading && !isError && (!data?.items || data.items.length === 0) && (
            <div className="flex flex-col items-center justify-center py-16 text-center">
              <div className="w-20 h-20 rounded-full bg-muted flex items-center justify-center mb-6">
                <Users className="h-10 w-10 text-muted-foreground/40" />
              </div>
              <p className="text-lg font-semibold text-foreground mb-2">
                {search || hasActiveFilters
                  ? 'Sonuç bulunamadı'
                  : 'Henüz müşteri kaydı yok'}
              </p>
              <p className="text-sm text-muted-foreground max-w-sm">
                {search
                  ? `"${search}" için sonuç bulunamadı. Farklı bir arama terimi deneyin.`
                  : hasActiveFilters
                  ? 'Seçili filtrelere uygun müşteri bulunamadı.'
                  : 'İlk müşterinizi ekleyerek CRM yolculuğunuza başlayın.'}
              </p>
              <div className="flex gap-2 mt-6">
                {hasActiveFilters && (
                  <Button variant="outline" onClick={clearFilters} className="gap-2 h-10">
                    <X className="h-4 w-4" />
                    Filtreleri Temizle
                  </Button>
                )}
                {!search && !hasActiveFilters && (
                  <Button onClick={() => setShowCreateDialog(true)} className="gap-2 h-10">
                    <Plus className="h-4 w-4" />
                    İlk Müşteriyi Ekle
                  </Button>
                )}
              </div>
            </div>
          )}

          {/* ── Customer list ── */}
          {!isLoading && !isError && data?.items && data.items.length > 0 && (
            <>
              <div className="space-y-2">
                {data.items.map((customer) => (
                  <CustomerCard
                    key={customer.id}
                    customer={customer}
                    onQuickAction={handleQuickAction}
                  />
                ))}
              </div>

              {/* ── Pagination ── */}
              {data.totalPages > 1 && (
                <div className="flex flex-col sm:flex-row items-center justify-between gap-3 pt-4 mt-4 border-t border-border">
                  <p className="text-sm text-muted-foreground order-2 sm:order-1">
                    <span className="font-medium text-foreground">{rangeStart}–{rangeEnd}</span>
                    {' '}/ {data.totalCount.toLocaleString('tr-TR')} kayıt
                  </p>
                  <div className="flex items-center gap-1 order-1 sm:order-2">
                    <Button
                      variant="outline"
                      size="sm"
                      className="h-9 w-9 p-0"
                      onClick={() => goToPage(Math.max(1, page - 1))}
                      disabled={page <= 1}
                    >
                      <ChevronLeft className="h-4 w-4" />
                      <span className="sr-only">Önceki sayfa</span>
                    </Button>
                    <div className="flex items-center gap-1 px-2">
                      {Array.from({ length: Math.min(5, data.totalPages) }, (_, i) => {
                        let pageNum: number;
                        if (data.totalPages <= 5) {
                          pageNum = i + 1;
                        } else if (page <= 3) {
                          pageNum = i + 1;
                        } else if (page >= data.totalPages - 2) {
                          pageNum = data.totalPages - 4 + i;
                        } else {
                          pageNum = page - 2 + i;
                        }
                        return (
                          <button
                            key={pageNum}
                            onClick={() => goToPage(pageNum)}
                            className={`w-8 h-8 rounded text-sm font-medium transition-colors ${
                              pageNum === page
                                ? 'bg-primary text-primary-foreground'
                                : 'text-muted-foreground hover:text-foreground hover:bg-muted'
                            }`}
                          >
                            {pageNum}
                          </button>
                        );
                      })}
                    </div>
                    <Button
                      variant="outline"
                      size="sm"
                      className="h-9 w-9 p-0"
                      onClick={() => goToPage(Math.min(data.totalPages, page + 1))}
                      disabled={page >= data.totalPages}
                    >
                      <ChevronRight className="h-4 w-4" />
                      <span className="sr-only">Sonraki sayfa</span>
                    </Button>
                  </div>
                </div>
              )}
            </>
          )}
        </CardContent>
      </Card>

      {/* ── Dialogs ── */}
      {isRezervalProject ? (
        <RezervalCustomerFormDialog
          isOpen={showCreateDialog}
          onClose={() => setShowCreateDialog(false)}
        />
      ) : (
        <CustomerFormDialog
          isOpen={showCreateDialog}
          onClose={() => setShowCreateDialog(false)}
        />
      )}

      {quickAction && (
        <AddContactHistoryDialog
          isOpen={true}
          onClose={() => setQuickAction(null)}
          customerId={quickAction.customerId}
          defaultType={quickAction.type}
        />
      )}
    </div>
  );
}
