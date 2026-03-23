import { Users, TrendingUp, CheckSquare, Activity, ArrowUpRight, ArrowDownRight } from 'lucide-react';
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
  trend?: number;
  description?: string;
  isLoading?: boolean;
}

function StatCard({ title, value, icon: Icon, trend, description, isLoading }: StatCardProps) {
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
              {trend !== undefined && (
                <div className={`flex items-center gap-1 text-xs ${trend >= 0 ? 'text-green-500' : 'text-red-500'}`}>
                  {trend >= 0 ? (
                    <ArrowUpRight className="h-3 w-3" />
                  ) : (
                    <ArrowDownRight className="h-3 w-3" />
                  )}
                  <span>{Math.abs(trend)}% geçen aya göre</span>
                </div>
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
  const { data: stats, isLoading } = useDashboardStats();

  // Mock data for when API is not available
  const mockStats = {
    totalCustomers: 248,
    activeCustomers: 189,
    newLeadsThisMonth: 23,
    openTasks: 47,
    openOpportunities: 31,
    pipelineValue: 2450000,
    monthlyActivity: [
      { month: 'Eki', calls: 45, meetings: 12, emails: 38 },
      { month: 'Kas', calls: 52, meetings: 18, emails: 42 },
      { month: 'Ara', calls: 38, meetings: 8, emails: 29 },
      { month: 'Oca', calls: 61, meetings: 22, emails: 55 },
      { month: 'Şub', calls: 48, meetings: 15, emails: 41 },
      { month: 'Mar', calls: 67, meetings: 28, emails: 62 },
    ],
    customersByStatus: [
      { status: 'Active' as const, count: 189 },
      { status: 'Lead' as const, count: 32 },
      { status: 'Inactive' as const, count: 18 },
      { status: 'Churned' as const, count: 9 },
    ],
    opportunitiesByStage: [
      { stage: 'Prospecting' as const, count: 12, value: 480000 },
      { stage: 'Qualification' as const, count: 8, value: 620000 },
      { stage: 'Proposal' as const, count: 5, value: 750000 },
      { stage: 'Negotiation' as const, count: 4, value: 380000 },
      { stage: 'ClosedWon' as const, count: 2, value: 220000 },
    ],
    recentActivities: [
      { id: '1', type: 'Call' as const, customerName: 'ABC Teknoloji A.Ş.', subject: 'Yıllık abonelik görüşmesi', createdByUserName: 'Ahmet Yılmaz', contactedAt: new Date(Date.now() - 30 * 60 * 1000).toISOString() },
      { id: '2', type: 'Meeting' as const, customerName: 'XYZ Yazılım Ltd.', subject: 'Demo sunumu', createdByUserName: 'Fatma Kaya', contactedAt: new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString() },
      { id: '3', type: 'Email' as const, customerName: 'Delta Holding', subject: 'Teklif gönderildi', createdByUserName: 'Mehmet Demir', contactedAt: new Date(Date.now() - 4 * 60 * 60 * 1000).toISOString() },
      { id: '4', type: 'WhatsApp' as const, customerName: 'Beta Lojistik', subject: 'Randevu onayı', createdByUserName: 'Ayşe Çelik', contactedAt: new Date(Date.now() - 6 * 60 * 60 * 1000).toISOString() },
      { id: '5', type: 'Visit' as const, customerName: 'Omega Mühendislik', subject: 'Yerinde inceleme', createdByUserName: 'Ali Şahin', contactedAt: new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString() },
    ],
  };

  const displayStats = stats ?? mockStats;

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
          value={displayStats.totalCustomers}
          icon={Users}
          trend={8}
          description={`${displayStats.activeCustomers} aktif müşteri`}
          isLoading={isLoading}
        />
        <StatCard
          title="Bu Ay Yeni Lead"
          value={displayStats.newLeadsThisMonth}
          icon={TrendingUp}
          trend={12}
          isLoading={isLoading}
        />
        <StatCard
          title="Açık Görevler"
          value={displayStats.openTasks}
          icon={CheckSquare}
          trend={-5}
          isLoading={isLoading}
        />
        <StatCard
          title="Pipeline Değeri"
          value={formatCurrency(displayStats.pipelineValue)}
          icon={Activity}
          trend={23}
          description={`${displayStats.openOpportunities} açık fırsat`}
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
                <AreaChart data={displayStats.monthlyActivity}>
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
            ) : (
              <div className="space-y-4">
                <ResponsiveContainer width="100%" height={140}>
                  <PieChart>
                    <Pie
                      data={displayStats.customersByStatus}
                      cx="50%"
                      cy="50%"
                      innerRadius={40}
                      outerRadius={65}
                      dataKey="count"
                      nameKey="status"
                    >
                      {displayStats.customersByStatus.map((entry) => (
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
                    />
                  </PieChart>
                </ResponsiveContainer>
                <div className="space-y-2">
                  {displayStats.customersByStatus.map((item) => (
                    <div key={item.status} className="flex items-center justify-between text-sm">
                      <div className="flex items-center gap-2">
                        <div
                          className="w-3 h-3 rounded-full"
                          style={{ backgroundColor: STATUS_COLORS[item.status] }}
                        />
                        <span className="text-muted-foreground">{item.status}</span>
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
            ) : (
              <div className="space-y-3">
                {displayStats.recentActivities.map((activity) => (
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
            ) : (
              <div className="space-y-3">
                {displayStats.opportunitiesByStage.map((item) => {
                  const maxValue = Math.max(...displayStats.opportunitiesByStage.map((s) => s.value));
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
