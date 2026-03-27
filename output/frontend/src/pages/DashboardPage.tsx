import { useNavigate } from 'react-router-dom';
import { Users, TrendingUp, CheckSquare, AlertCircle, Clock, Phone } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { useDashboardStats } from '@/api/dashboard';
import {
  AreaChart,
  Area,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  PieChart,
  Pie,
  Cell,
} from 'recharts';

const STATUS_COLORS: Record<string, string> = {
  Lead: '#6366f1',
  Active: '#22c55e',
  Demo: '#8b5cf6',
  Churned: '#ef4444',
  Passive: '#64748b',
};

const STATUS_LABELS: Record<string, string> = {
  Lead: 'Lead',
  Active: 'Aktif',
  Demo: 'Demo',
  Churned: 'Kaybedildi',
  Passive: 'Pasif',
};

const STAGE_COLORS: Record<string, string> = {
  YeniArama: '#6366f1',
  Potansiyel: '#f59e0b',
  Demo: '#8b5cf6',
  Musteri: '#22c55e',
  Kayip: '#ef4444',
};

const STAGE_LABELS: Record<string, string> = {
  YeniArama: 'Yeni Arama',
  Potansiyel: 'Potansiyel',
  Demo: 'Demo',
  Musteri: 'Müşteri',
  Kayip: 'Kayıp',
};

const CONTACT_TYPE_LABELS: Record<string, string> = {
  Call: 'Arama',
  Email: 'E-posta',
  Meeting: 'Toplantı',
  Note: 'Not',
  WhatsApp: 'WhatsApp',
  Visit: 'Ziyaret',
};

function formatCurrency(value: number): string {
  return new Intl.NumberFormat('tr-TR', {
    style: 'currency',
    currency: 'TRY',
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
  }).format(value);
}

function formatDate(dateStr: string): string {
  return new Intl.DateTimeFormat('tr-TR', {
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(dateStr));
}

function formatExpirationDate(dateStr: string): string {
  return new Intl.DateTimeFormat('tr-TR', {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  }).format(new Date(dateStr));
}

interface StatCardProps {
  title: string;
  value: string | number;
  icon: React.ComponentType<{ className?: string }>;
  description?: string;
  isLoading?: boolean;
}

function StatCard({ title, value, icon: Icon, description, isLoading }: StatCardProps) {
  return (
    <Card>
      <CardContent className="p-6">
        {isLoading ? (
          <div className="space-y-3">
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-8 w-16" />
            <Skeleton className="h-3 w-32" />
          </div>
        ) : (
          <div className="flex items-start justify-between">
            <div className="space-y-1">
              <p className="text-sm text-muted-foreground">{title}</p>
              <p className="text-3xl font-bold text-foreground">{value}</p>
              {description && (
                <p className="text-xs text-muted-foreground">{description}</p>
              )}
            </div>
            <div className="p-3 rounded-xl bg-primary/10">
              <Icon className="h-6 w-6 text-primary" />
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

export function DashboardPage() {
  const { data: stats, isLoading, isError } = useDashboardStats();
  const navigate = useNavigate();

  if (isError) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[40vh] gap-3">
        <AlertCircle className="h-10 w-10 text-destructive/50" />
        <p className="text-muted-foreground text-sm">Dashboard verileri yüklenemedi.</p>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Page header */}
      <div>
        <h1 className="text-2xl font-bold text-foreground">Gösterge Paneli</h1>
        <p className="text-muted-foreground text-sm mt-1">
          Hoş geldiniz! İşte bugünkü özet bilgileriniz.
        </p>
      </div>

      {/* Stats grid */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <StatCard
          title="Toplam Müşteri"
          value={stats?.totalCustomers ?? 0}
          icon={Users}
          description={stats ? `${stats.activeCustomers} aktif müşteri` : undefined}
          isLoading={isLoading}
        />
        <StatCard
          title="Bu Ay Yeni Lead"
          value={stats?.newLeadsThisMonth ?? 0}
          icon={TrendingUp}
          isLoading={isLoading}
        />
        <StatCard
          title="Açık Görevler"
          value={stats?.openTasks ?? 0}
          icon={CheckSquare}
          isLoading={isLoading}
        />
      </div>

      {/* Charts row */}
      <div className="grid grid-cols-1 lg:grid-cols-4 gap-4">
        {/* Activity chart */}
        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle className="text-base font-semibold">Aylık Aktivite</CardTitle>
            <CardDescription>Son 6 ay iletişim geçmişi</CardDescription>
          </CardHeader>
          <CardContent>
            {isLoading ? (
              <Skeleton className="h-[220px] w-full" />
            ) : (
              <ResponsiveContainer width="100%" height={220}>
                <AreaChart data={stats?.monthlyActivity ?? []}>
                  <defs>
                    <linearGradient id="callsGrad" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="5%" stopColor="#6366f1" stopOpacity={0.3} />
                      <stop offset="95%" stopColor="#6366f1" stopOpacity={0} />
                    </linearGradient>
                    <linearGradient id="meetingsGrad" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="5%" stopColor="#22c55e" stopOpacity={0.3} />
                      <stop offset="95%" stopColor="#22c55e" stopOpacity={0} />
                    </linearGradient>
                  </defs>
                  <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" />
                  <XAxis
                    dataKey="month"
                    tick={{ fill: 'hsl(var(--muted-foreground))', fontSize: 12 }}
                    axisLine={false}
                    tickLine={false}
                  />
                  <YAxis
                    tick={{ fill: 'hsl(var(--muted-foreground))', fontSize: 12 }}
                    axisLine={false}
                    tickLine={false}
                  />
                  <Tooltip
                    contentStyle={{
                      backgroundColor: 'hsl(var(--card))',
                      border: '1px solid hsl(var(--border))',
                      borderRadius: '8px',
                    }}
                    labelStyle={{ color: 'hsl(var(--foreground))' }}
                  />
                  <Area
                    type="monotone"
                    dataKey="calls"
                    name="Arama"
                    stroke="#6366f1"
                    fill="url(#callsGrad)"
                    strokeWidth={2}
                  />
                  <Area
                    type="monotone"
                    dataKey="meetings"
                    name="Toplantı"
                    stroke="#22c55e"
                    fill="url(#meetingsGrad)"
                    strokeWidth={2}
                  />
                </AreaChart>
              </ResponsiveContainer>
            )}
          </CardContent>
        </Card>

        {/* Customer status pie chart */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base font-semibold">Müşteri Durumu</CardTitle>
            <CardDescription>Statüye göre dağılım</CardDescription>
          </CardHeader>
          <CardContent>
            {isLoading ? (
              <Skeleton className="h-[220px] w-full" />
            ) : !stats?.customersByStatus?.length ? (
              <div className="flex items-center justify-center h-[220px]">
                <p className="text-sm text-muted-foreground">Veri yok</p>
              </div>
            ) : (
              <div className="space-y-4">
                <ResponsiveContainer width="100%" height={140}>
                  <PieChart>
                    <Pie
                      data={stats.customersByStatus}
                      cx="50%"
                      cy="50%"
                      innerRadius={40}
                      outerRadius={65}
                      dataKey="count"
                      nameKey="status"
                    >
                      {stats.customersByStatus.map((entry) => (
                        <Cell
                          key={entry.status}
                          fill={STATUS_COLORS[entry.status] ?? '#64748b'}
                        />
                      ))}
                    </Pie>
                    <Tooltip
                      contentStyle={{
                        backgroundColor: 'hsl(var(--card))',
                        border: '1px solid hsl(var(--border))',
                        borderRadius: '8px',
                      }}
                      formatter={(value, name) => [value, STATUS_LABELS[name as string] ?? name]}
                    />
                  </PieChart>
                </ResponsiveContainer>
                <div className="space-y-2">
                  {stats.customersByStatus.map((item) => (
                    <div key={item.status} className="flex items-center justify-between text-sm">
                      <div className="flex items-center gap-2">
                        <div
                          className="w-3 h-3 rounded-full"
                          style={{ backgroundColor: STATUS_COLORS[item.status] }}
                        />
                        <span className="text-muted-foreground">
                          {STATUS_LABELS[item.status] ?? item.status}
                        </span>
                      </div>
                      <span className="font-medium text-foreground">{item.count}</span>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </CardContent>
        </Card>

        {/* Sales pipeline pie chart */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base font-semibold">Satış Pipeline</CardTitle>
            <CardDescription>Aşamaya göre dağılım</CardDescription>
          </CardHeader>
          <CardContent>
            {isLoading ? (
              <Skeleton className="h-[220px] w-full" />
            ) : !stats?.opportunitiesByStage?.length ? (
              <div className="flex items-center justify-center h-[220px]">
                <p className="text-sm text-muted-foreground">Veri yok</p>
              </div>
            ) : (
              <div className="space-y-4">
                <ResponsiveContainer width="100%" height={140}>
                  <PieChart>
                    <Pie
                      data={stats.opportunitiesByStage}
                      cx="50%"
                      cy="50%"
                      innerRadius={40}
                      outerRadius={65}
                      dataKey="count"
                      nameKey="stage"
                    >
                      {stats.opportunitiesByStage.map((entry) => (
                        <Cell
                          key={entry.stage}
                          fill={STAGE_COLORS[entry.stage] ?? '#64748b'}
                        />
                      ))}
                    </Pie>
                    <Tooltip
                      contentStyle={{
                        backgroundColor: 'hsl(var(--card))',
                        border: '1px solid hsl(var(--border))',
                        borderRadius: '8px',
                      }}
                      formatter={(value, name) => [value, STAGE_LABELS[name as string] ?? name]}
                    />
                  </PieChart>
                </ResponsiveContainer>
                <div className="space-y-2">
                  {stats.opportunitiesByStage.map((item) => (
                    <div key={item.stage} className="flex items-center justify-between text-sm">
                      <div className="flex items-center gap-2">
                        <div
                          className="w-3 h-3 rounded-full"
                          style={{ backgroundColor: STAGE_COLORS[item.stage] ?? '#64748b' }}
                        />
                        <span className="text-muted-foreground">
                          {STAGE_LABELS[item.stage] ?? item.stage}
                        </span>
                      </div>
                      <span className="font-medium text-foreground">{item.count}</span>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </CardContent>
        </Card>
      </div>

      {/* Expiring subscriptions + Recent activity */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        {/* Expiring subscriptions widget */}
        <Card>
          <CardHeader>
            <div className="flex items-center justify-between">
              <div>
                <CardTitle className="text-base font-semibold flex items-center gap-2">
                  <Clock className="h-4 w-4 text-amber-500" />
                  Süresi Yaklaşan Abonelikler
                </CardTitle>
                <CardDescription>Önümüzdeki 30 gün içinde</CardDescription>
              </div>
              {(stats?.expiringCustomers?.length ?? 0) > 0 && (
                <Badge variant="outline" className="text-amber-500 border-amber-500/40 bg-amber-500/10">
                  {stats!.expiringCustomers.length} firma
                </Badge>
              )}
            </div>
          </CardHeader>
          <CardContent>
            {isLoading ? (
              <div className="space-y-3">
                {Array.from({ length: 4 }).map((_, i) => (
                  <Skeleton key={i} className="h-14 w-full" />
                ))}
              </div>
            ) : !stats?.expiringCustomers?.length ? (
              <div className="flex flex-col items-center justify-center py-10 text-center">
                <div className="w-12 h-12 rounded-full bg-muted flex items-center justify-center mb-3">
                  <Clock className="h-6 w-6 text-muted-foreground/40" />
                </div>
                <p className="text-sm text-muted-foreground">Önümüzdeki 30 günde süresi dolacak abonelik yok.</p>
              </div>
            ) : (
              <div className="space-y-2">
                {stats.expiringCustomers.map((customer) => (
                  <div
                    key={customer.id}
                    className="flex items-center gap-3 p-3 rounded-lg bg-muted/30 hover:bg-muted/50 transition-colors cursor-pointer"
                    onClick={() => navigate(`/customers/${customer.id}`)}
                  >
                    <div className={`w-10 h-10 rounded-full flex items-center justify-center flex-shrink-0 text-xs font-bold ${
                      customer.daysLeft <= 7
                        ? 'bg-red-500/15 text-red-400'
                        : customer.daysLeft <= 14
                        ? 'bg-amber-500/15 text-amber-400'
                        : 'bg-orange-500/15 text-orange-400'
                    }`}>
                      {customer.daysLeft}g
                    </div>
                    <div className="min-w-0 flex-1">
                      <p className="text-sm font-medium text-foreground truncate">
                        {customer.companyName}
                      </p>
                      <div className="flex items-center gap-2 mt-0.5">
                        {customer.contactName && (
                          <p className="text-xs text-muted-foreground truncate">{customer.contactName}</p>
                        )}
                        {customer.phone && (
                          <div className="flex items-center gap-1 text-xs text-muted-foreground">
                            <Phone className="h-3 w-3" />
                            {customer.phone}
                          </div>
                        )}
                      </div>
                    </div>
                    <div className="text-right flex-shrink-0">
                      <p className="text-xs text-muted-foreground">{formatExpirationDate(customer.expirationDate)}</p>
                      <Badge
                        variant="outline"
                        className={`text-xs mt-1 ${
                          customer.daysLeft <= 7
                            ? 'text-red-400 border-red-500/40 bg-red-500/10'
                            : customer.daysLeft <= 14
                            ? 'text-amber-400 border-amber-500/40 bg-amber-500/10'
                            : 'text-orange-400 border-orange-500/40 bg-orange-500/10'
                        }`}
                      >
                        {customer.daysLeft === 0 ? 'Bugün' : `${customer.daysLeft} gün`}
                      </Badge>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </CardContent>
        </Card>

        {/* Recent activities */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base font-semibold">Son Aktiviteler</CardTitle>
            <CardDescription>Son iletişim kayıtları</CardDescription>
          </CardHeader>
          <CardContent>
            {isLoading ? (
              <div className="space-y-3">
                {Array.from({ length: 5 }).map((_, i) => (
                  <Skeleton key={i} className="h-12 w-full" />
                ))}
              </div>
            ) : !stats?.recentActivities?.length ? (
              <p className="text-sm text-muted-foreground text-center py-8">Henüz aktivite yok.</p>
            ) : (
              <div className="space-y-3">
                {stats.recentActivities.map((activity) => (
                  <div
                    key={activity.id}
                    className="flex items-start gap-3 p-3 rounded-lg bg-muted/30 hover:bg-muted/50 transition-colors"
                  >
                    <div className="w-8 h-8 rounded-full bg-primary/10 flex items-center justify-center flex-shrink-0 mt-0.5">
                      <span className="text-primary text-xs font-medium">
                        {CONTACT_TYPE_LABELS[activity.type]?.[0] ?? '?'}
                      </span>
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex items-start justify-between gap-2">
                        <p className="text-sm font-medium text-foreground truncate">
                          {activity.customerName}
                        </p>
                        <Badge variant="secondary" className="text-xs flex-shrink-0">
                          {CONTACT_TYPE_LABELS[activity.type]}
                        </Badge>
                      </div>
                      {activity.subject && (
                        <p className="text-xs text-muted-foreground truncate mt-0.5">
                          {activity.subject}
                        </p>
                      )}
                      <p className="text-xs text-muted-foreground mt-0.5">
                        {activity.createdByUserName} · {formatDate(activity.contactedAt)}
                      </p>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </CardContent>
        </Card>
      </div>

    </div>
  );
}
