import { Users, Plus, Search, Filter } from 'lucide-react';
import { Card, CardContent, CardHeader } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { useCustomers } from '@/api/customers';
import { useNavigate } from 'react-router-dom';
import { useState } from 'react';
import type { CustomerStatus } from '@/types';

const STATUS_BADGE_VARIANT: Record<CustomerStatus, 'default' | 'secondary' | 'destructive' | 'outline'> = {
  Active: 'default',
  Lead: 'secondary',
  Inactive: 'outline',
  Churned: 'destructive',
};

const STATUS_LABELS: Record<CustomerStatus, string> = {
  Active: 'Aktif',
  Lead: 'Lead',
  Inactive: 'Pasif',
  Churned: 'Kayıp',
};

export function CustomersPage() {
  const navigate = useNavigate();
  const [search, setSearch] = useState('');
  const { data, isLoading } = useCustomers({ search, pageSize: 20 });

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-foreground">Müşteriler</h1>
          <p className="text-muted-foreground text-sm mt-1">
            Tüm müşteri ve lead kayıtları
          </p>
        </div>
        <Button onClick={() => navigate('/customers/new')} className="gap-2 h-11">
          <Plus className="h-4 w-4" />
          Yeni Müşteri
        </Button>
      </div>

      <Card>
        <CardHeader className="pb-4">
          <div className="flex flex-col sm:flex-row gap-3">
            <div className="relative flex-1">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <Input
                placeholder="Müşteri ara..."
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                className="pl-9 h-11"
              />
            </div>
            <Button variant="outline" className="gap-2 h-11">
              <Filter className="h-4 w-4" />
              Filtrele
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          {isLoading ? (
            <div className="space-y-3">
              {Array.from({ length: 8 }).map((_, i) => (
                <Skeleton key={i} className="h-14 w-full" />
              ))}
            </div>
          ) : !data?.items.length ? (
            <div className="flex flex-col items-center justify-center py-16 text-center">
              <Users className="h-12 w-12 text-muted-foreground/30 mb-4" />
              <p className="text-muted-foreground">
                {search ? 'Arama sonucu bulunamadı' : 'Henüz müşteri kaydı bulunmuyor'}
              </p>
              {!search && (
                <Button
                  variant="outline"
                  className="mt-4 gap-2"
                  onClick={() => navigate('/customers/new')}
                >
                  <Plus className="h-4 w-4" />
                  İlk müşteriyi ekle
                </Button>
              )}
            </div>
          ) : (
            <div className="space-y-2">
              {data.items.map((customer) => (
                <div
                  key={customer.id}
                  onClick={() => navigate(`/customers/${customer.id}`)}
                  className="flex items-center justify-between p-4 rounded-lg border border-border hover:bg-muted/50 cursor-pointer transition-colors"
                >
                  <div className="flex items-center gap-3 min-w-0">
                    <div className="w-10 h-10 rounded-full bg-primary/10 flex items-center justify-center flex-shrink-0">
                      <span className="text-primary text-sm font-medium">
                        {customer.companyName[0]}
                      </span>
                    </div>
                    <div className="min-w-0">
                      <p className="font-medium text-foreground truncate">
                        {customer.companyName}
                      </p>
                      <p className="text-sm text-muted-foreground truncate">
                        {customer.contactName ?? customer.email ?? '—'}
                      </p>
                    </div>
                  </div>
                  <div className="flex items-center gap-3 flex-shrink-0 ml-4">
                    <Badge variant={STATUS_BADGE_VARIANT[customer.status]}>
                      {STATUS_LABELS[customer.status]}
                    </Badge>
                  </div>
                </div>
              ))}
            </div>
          )}

          {data && data.totalPages > 1 && (
            <div className="flex items-center justify-center gap-2 pt-4 mt-4 border-t border-border">
              <p className="text-sm text-muted-foreground">
                Toplam {data.totalCount} kayıt · Sayfa {data.page} / {data.totalPages}
              </p>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
