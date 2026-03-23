import { useState } from 'react';
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
import { CustomerCard } from '@/components/customers/CustomerCard';
import { CustomerFormDialog } from '@/components/customers/CustomerForm';
import { AddContactHistoryDialog } from '@/components/customers/AddContactHistoryDialog';
import { useToast } from '@/hooks/use-toast';
import type { CustomerStatus, CustomerSegment, ContactType } from '@/types';

// ── Types ─────────────────────────────────────────────────────────────────────

interface QuickActionState {
  customerId: string;
  type: ContactType;
}

// ── Component ─────────────────────────────────────────────────────────────────

export function CustomersPage() {
  const { toast } = useToast();

  // Search & filter state
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<CustomerStatus | 'all'>('all');
  const [segmentFilter, setSegmentFilter] = useState<CustomerSegment | 'all'>('all');
  const [showFilters, setShowFilters] = useState(false);

  // Pagination
  const [page, setPage] = useState(1);
  const PAGE_SIZE = 20;

  // Dialog state
  const [showCreateDialog, setShowCreateDialog] = useState(false);
  const [quickAction, setQuickAction] = useState<QuickActionState | null>(null);

  const { data, isLoading, isError } = useCustomers({
    search: search.trim() || undefined,
    status: statusFilter !== 'all' ? statusFilter : undefined,
    segment: segmentFilter !== 'all' ? segmentFilter : undefined,
    pageNumber: page,
    pageSize: PAGE_SIZE,
  });

  const hasActiveFilters = statusFilter !== 'all' || segmentFilter !== 'all';
  const activeFilterCount = [statusFilter !== 'all', segmentFilter !== 'all'].filter(Boolean).length;

  function handleSearch(value: string) {
    setSearch(value);
    setPage(1);
  }

  function handleStatusChange(value: string) {
    setStatusFilter(value as CustomerStatus | 'all');
    setPage(1);
  }

  function handleSegmentChange(value: string) {
    setSegmentFilter(value as CustomerSegment | 'all');
    setPage(1);
  }

  function clearFilters() {
    setStatusFilter('all');
    setSegmentFilter('all');
    setPage(1);
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
            <div className="flex flex-col sm:flex-row gap-3 mt-4 pt-4 border-t border-border">
              <div className="flex-1">
                <Select value={statusFilter} onValueChange={handleStatusChange}>
                  <SelectTrigger className="h-11">
                    <SelectValue placeholder="Tüm Durumlar" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">Tüm Durumlar</SelectItem>
                    <SelectItem value="Lead">🔵 Lead</SelectItem>
                    <SelectItem value="Active">🟢 Aktif</SelectItem>
                    <SelectItem value="Inactive">🟡 Pasif</SelectItem>
                    <SelectItem value="Churned">🔴 Kayıp</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="flex-1">
                <Select value={segmentFilter} onValueChange={handleSegmentChange}>
                  <SelectTrigger className="h-11">
                    <SelectValue placeholder="Tüm Segmentler" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">Tüm Segmentler</SelectItem>
                    <SelectItem value="SME">KOBİ</SelectItem>
                    <SelectItem value="Enterprise">Kurumsal</SelectItem>
                    <SelectItem value="Startup">Startup</SelectItem>
                    <SelectItem value="Government">Kamu</SelectItem>
                    <SelectItem value="Individual">Bireysel</SelectItem>
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
                      onClick={() => setPage((p) => Math.max(1, p - 1))}
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
                            onClick={() => setPage(pageNum)}
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
                      onClick={() => setPage((p) => Math.min(data.totalPages, p + 1))}
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
      <CustomerFormDialog
        isOpen={showCreateDialog}
        onClose={() => setShowCreateDialog(false)}
      />

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
