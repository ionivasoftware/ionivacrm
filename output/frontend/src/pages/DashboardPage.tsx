import { Users, TrendingUp, CheckSquare, Activity, AlertCircle } from 'lucide-react';
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
  Inactive: '#f59e0b',
  Churned: '#ef4444',
};

const STATUS_LABELS: Record<string, string> = {
  Lead: 'Lead',
  Active: 'Aktif',
  Inactive: 'Pasif',
  Churned: 'Kaybedildi',
};

const STAGE_LABELS: Record<string, string> = {
  Prospecting: 'Araştırma',
  Qualification: 'Nitelendirme',
  Proposal: 'Teklif',
  Negotiation: 'Müzakere',
  ClosedWon: 'Kazanıldı',
  ClosedLost: 'Kaybedildi',
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
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
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
        <StatCard
          title="Pipeline Değeri"
          value={stats ? formatCurrency(stats.pipelineValue) : '₺0'}
          icon={Activity}
          description={stats ? `${stats.openOpportunities} açık fırsat` : undefined}
          isLoading={isLoading}
        />
      </div>

      {/* Charts row */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
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
      </div>

      {/* Recent Activity + Pipeline */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
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

        {/* Pipeline by stage */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base font-semibold">Satış Pipeline</CardTitle>
            <CardDescription>Aşamaya göre fırsatlar</CardDescription>
          </CardHeader>
          <CardContent>
            {isLoading ? (
              <div className="space-y-3">
                {Array.from({ length: 5 }).map((_, i) => (
                  <Skeleton key={i} className="h-10 w-full" />
                ))}
              </div>
            ) : !stats?.opportunitiesByStage?.length ? (
              <p className="text-sm text-muted-foreground text-center py-8">Henüz fırsat yok.</p>
            ) : (
              <div className="space-y-3">
                {stats.opportunitiesByStage.map((item) => {
                  const maxValue = Math.max(...stats.opportunitiesByStage.map((s) => s.value));
                  const percentage = maxValue > 0 ? (item.value / maxValue) * 100 : 0;
                  return (
                    <div key={item.stage} className="space-y-1">
                      <div className="flex items-center justify-between text-sm">
                        <span className="text-muted-foreground">
                          {STAGE_LABELS[item.stage] ?? item.stage}
                        </span>
                        <div className="flex items-center gap-2">
                          <Badge variant="outline" className="text-xs">
                            {item.count} fırsat
                          </Badge>
                          <span className="font-medium text-foreground text-xs">
                            {formatCurrency(item.value)}
                          </span>
                        </div>
                      </div>
                      <div className="h-2 bg-muted rounded-full overflow-hidden">
                        <div
                          className="h-full bg-primary rounded-full transition-all duration-500"
                          style={{ width: `${percentage}%` }}
                        />
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
