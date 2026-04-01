import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  BarChart2, Users, TrendingUp, CheckSquare, MessageSquare,
  Phone, Mail, Calendar, AlertCircle,
} from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import { Badge } from '@/components/ui/badge';
import {
  BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip,
  ResponsiveContainer, PieChart, Pie, Cell,
} from 'recharts';
import { apiClient } from '@/api/client';
import { useAuthStore } from '@/stores/authStore';
import type { ApiResponse } from '@/types';

// ── Types ──────────────────────────────────────────────────────────────────────

interface ReportsData {
  totalCustomers: number;
  newCustomers: number;
  newLeads: number;
  closedWon: number;
  closedLost: number;
  totalContacts: number;
  totalTasks: number;
  completedTasks: number;
  dailyActivity: { date: string; contacts: number; tasks: number }[];
  contactTypeBreakdown: { type: string; count: number }[];
}

const TYPE_LABELS: Record<string, string> = {
  Call: 'Arama',
  Email: 'E-posta',
  Meeting: 'Toplantı',
  Note: 'Not',
  WhatsApp: 'WhatsApp',
  Visit: 'Ziyaret',
};

const TYPE_COLORS: Record<string, string> = {
  Call: '#6366f1',
  Email: '#22c55e',
  Meeting: '#f59e0b',
  Note: '#64748b',
  WhatsApp: '#10b981',
  Visit: '#ef4444',
};

// ── Stat Card ─────────────────────────────────────────────────────────────────

function StatCard({
  title,
  value,
  sub,
  icon: Icon,
  color = 'text-primary',
  isLoading,
}: {
  title: string;
  value: string | number;
  sub?: string;
  icon: React.ComponentType<{ className?: string }>;
  color?: string;
  isLoading?: boolean;
}) {
  return (
    <Card>
      <CardContent className="p-5">
        {isLoading ? (
          <div className="space-y-2">
            <Skeleton className="h-4 w-20" />
            <Skeleton className="h-8 w-14" />
          </div>
        ) : (
          <div className="flex items-start justify-between">
            <div>
              <p className="text-sm text-muted-foreground">{title}</p>
              <p className="text-3xl font-bold text-foreground mt-0.5">{value}</p>
              {sub && <p className="text-xs text-muted-foreground mt-1">{sub}</p>}
            </div>
            <div className="p-2.5 rounded-xl bg-primary/10">
              <Icon className={`h-5 w-5 ${color}`} />
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

function formatDateInput(d: Date): string {
  return d.toISOString().split('T')[0];
}

export function ReportsPage() {
  const { currentProjectId } = useAuthStore();

  const today = new Date();
  const firstOfMonth = new Date(today.getFullYear(), today.getMonth(), 1);

  const [startDate, setStartDate] = useState(formatDateInput(firstOfMonth));
  const [endDate, setEndDate] = useState(formatDateInput(today));
  const [queryDates, setQueryDates] = useState({ start: startDate, end: endDate });

  const { data, isLoading, isError } = useQuery({
    queryKey: ['reports', currentProjectId, queryDates.start, queryDates.end],
    queryFn: async () => {
      const res = await apiClient.get<ApiResponse<ReportsData>>('/reports', {
        params: {
          projectId: currentProjectId,
          startDate: queryDates.start,
          endDate: queryDates.end,
        },
      });
      return res.data.data;
    },
    enabled: !!currentProjectId,
  });

  function handleApply() {
    setQueryDates({ start: startDate, end: endDate });
  }

  // Filter daily activity to last 30 days max for readability
  const dailyData = data?.dailyActivity?.slice(-30) ?? [];

  return (
    <div className="space-y-6">
      {/* Header */}
      <div>
        <h1 className="text-2xl font-bold text-foreground">Raporlar</h1>
        <p className="text-muted-foreground text-sm mt-1">
          Seçili tarih aralığında proje istatistiklerini görüntüleyin.
        </p>
      </div>

      {/* Date filter */}
      <Card>
        <CardContent className="p-4">
          <div className="flex flex-wrap items-end gap-4">
            <div className="space-y-1.5">
              <Label>Başlangıç Tarihi</Label>
              <Input
                type="date"
                value={startDate}
                onChange={(e) => setStartDate(e.target.value)}
                className="h-10 w-44"
              />
            </div>
            <div className="space-y-1.5">
              <Label>Bitiş Tarihi</Label>
              <Input
                type="date"
                value={endDate}
                onChange={(e) => setEndDate(e.target.value)}
                className="h-10 w-44"
              />
            </div>
            <Button onClick={handleApply} className="gap-2 h-10">
              <BarChart2 className="h-4 w-4" />
              Raporu Getir
            </Button>
            <div className="flex gap-2 flex-wrap">
              {[
                { label: 'Bu Ay', start: formatDateInput(firstOfMonth), end: formatDateInput(today) },
                { label: 'Son 30 Gün', start: formatDateInput(new Date(Date.now() - 30 * 86400000)), end: formatDateInput(today) },
                { label: 'Son 90 Gün', start: formatDateInput(new Date(Date.now() - 90 * 86400000)), end: formatDateInput(today) },
              ].map(({ label, start, end }) => (
                <Button
                  key={label}
                  variant="outline"
                  size="sm"
                  className="h-10 text-xs"
                  onClick={() => { setStartDate(start); setEndDate(end); setQueryDates({ start, end }); }}
                >
                  {label}
                </Button>
              ))}
            </div>
          </div>
        </CardContent>
      </Card>

      {isError && (
        <div className="flex items-center gap-3 p-4 rounded-lg border border-destructive/20 bg-destructive/5">
          <AlertCircle className="h-5 w-5 text-destructive" />
          <p className="text-sm text-destructive">Raporlar yüklenemedi.</p>
        </div>
      )}

      {/* KPI cards */}
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-4">
        <StatCard
          title="Toplam Müşteri"
          value={data?.totalCustomers ?? 0}
          sub={`+${data?.newCustomers ?? 0} yeni`}
          icon={Users}
          isLoading={isLoading}
        />
        <StatCard
          title="Yeni Lead"
          value={data?.newLeads ?? 0}
          icon={TrendingUp}
          isLoading={isLoading}
        />
        <StatCard
          title="Kazanılan Pipeline"
          value={data?.closedWon ?? 0}
          sub={`${data?.closedLost ?? 0} kaybedildi`}
          icon={CheckSquare}
          color="text-emerald-500"
          isLoading={isLoading}
        />
        <StatCard
          title="Toplam Görüşme"
          value={data?.totalContacts ?? 0}
          icon={MessageSquare}
          isLoading={isLoading}
        />
        <StatCard
          title="Tamamlanan Görev"
          value={data?.completedTasks ?? 0}
          sub={`${data?.totalTasks ?? 0} toplam`}
          icon={CheckSquare}
          isLoading={isLoading}
        />
      </div>

      {/* Charts row */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        {/* Daily activity bar chart */}
        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle className="text-base font-semibold">Günlük Görüşme Aktivitesi</CardTitle>
            <CardDescription>Seçili dönemdeki günlük iletişim sayısı</CardDescription>
          </CardHeader>
          <CardContent>
            {isLoading ? (
              <Skeleton className="h-[200px] w-full" />
            ) : dailyData.length === 0 ? (
              <div className="flex items-center justify-center h-[200px]">
                <p className="text-sm text-muted-foreground">Bu dönemde veri yok.</p>
              </div>
            ) : (
              <ResponsiveContainer width="100%" height={200}>
                <BarChart data={dailyData} barSize={6}>
                  <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" />
                  <XAxis
                    dataKey="date"
                    tick={{ fill: 'hsl(var(--muted-foreground))', fontSize: 10 }}
                    axisLine={false}
                    tickLine={false}
                    interval={Math.floor(dailyData.length / 7)}
                  />
                  <YAxis
                    tick={{ fill: 'hsl(var(--muted-foreground))', fontSize: 11 }}
                    axisLine={false}
                    tickLine={false}
                  />
                  <Tooltip
                    contentStyle={{
                      backgroundColor: 'hsl(var(--card))',
                      border: '1px solid hsl(var(--border))',
                      borderRadius: '8px',
                    }}
                  />
                  <Bar dataKey="contacts" name="Görüşme" fill="#6366f1" radius={[2, 2, 0, 0]} />
                </BarChart>
              </ResponsiveContainer>
            )}
          </CardContent>
        </Card>

        {/* Contact type pie chart */}
        <Card>
          <CardHeader>
            <CardTitle className="text-base font-semibold">Görüşme Türleri</CardTitle>
            <CardDescription>İletişim kanalı dağılımı</CardDescription>
          </CardHeader>
          <CardContent>
            {isLoading ? (
              <Skeleton className="h-[200px] w-full" />
            ) : !data?.contactTypeBreakdown?.length ? (
              <div className="flex items-center justify-center h-[200px]">
                <p className="text-sm text-muted-foreground">Veri yok</p>
              </div>
            ) : (
              <div className="space-y-3">
                <ResponsiveContainer width="100%" height={130}>
                  <PieChart>
                    <Pie
                      data={data.contactTypeBreakdown}
                      cx="50%"
                      cy="50%"
                      innerRadius={35}
                      outerRadius={60}
                      dataKey="count"
                      nameKey="type"
                    >
                      {data.contactTypeBreakdown.map((entry) => (
                        <Cell key={entry.type} fill={TYPE_COLORS[entry.type] ?? '#64748b'} />
                      ))}
                    </Pie>
                    <Tooltip
                      contentStyle={{
                        backgroundColor: 'hsl(var(--card))',
                        border: '1px solid hsl(var(--border))',
                        borderRadius: '8px',
                      }}
                      formatter={(value, name) => [value, TYPE_LABELS[name as string] ?? name]}
                    />
                  </PieChart>
                </ResponsiveContainer>
                <div className="space-y-1.5">
                  {data.contactTypeBreakdown.map((item) => (
                    <div key={item.type} className="flex items-center justify-between text-sm">
                      <div className="flex items-center gap-2">
                        <div
                          className="w-2.5 h-2.5 rounded-full"
                          style={{ backgroundColor: TYPE_COLORS[item.type] ?? '#64748b' }}
                        />
                        <span className="text-muted-foreground text-xs">
                          {TYPE_LABELS[item.type] ?? item.type}
                        </span>
                      </div>
                      <Badge variant="secondary" className="text-xs">{item.count}</Badge>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
