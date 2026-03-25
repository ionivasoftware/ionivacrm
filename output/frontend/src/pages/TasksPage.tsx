import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { CheckCircle2, Circle, Clock, XCircle, User, Calendar, Loader2, ClipboardList } from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from '@/components/ui/select';
import { useAllTasks, useUpdateTaskStatus } from '@/api/customers';
import { useToast } from '@/hooks/use-toast';
import type { CustomerTask, TaskStatus, TaskPriority } from '@/types';

// ── Config ────────────────────────────────────────────────────────────────────

const STATUS_TABS: { value: TaskStatus | 'All'; label: string }[] = [
  { value: 'All',        label: 'Tümü' },
  { value: 'Todo',       label: 'Yapılacak' },
  { value: 'InProgress', label: 'Devam Ediyor' },
  { value: 'Done',       label: 'Tamamlandı' },
  { value: 'Cancelled',  label: 'İptal' },
];

const PRIORITY_LABELS: Record<TaskPriority, string> = {
  Low:      'Düşük',
  Medium:   'Orta',
  High:     'Yüksek',
  Critical: 'Kritik',
};

const PRIORITY_VARIANTS: Record<TaskPriority, 'outline' | 'secondary' | 'default' | 'destructive'> = {
  Low:      'outline',
  Medium:   'secondary',
  High:     'default',
  Critical: 'destructive',
};

const STATUS_ICON: Record<TaskStatus, React.ReactNode> = {
  Todo:       <Circle className="h-4 w-4 text-muted-foreground" />,
  InProgress: <Clock className="h-4 w-4 text-blue-500" />,
  Done:       <CheckCircle2 className="h-4 w-4 text-emerald-500" />,
  Cancelled:  <XCircle className="h-4 w-4 text-rose-400" />,
};

const STATUS_NEXT: Record<TaskStatus, TaskStatus> = {
  Todo:       'InProgress',
  InProgress: 'Done',
  Done:       'Todo',
  Cancelled:  'Todo',
};

function formatDate(dateStr: string | null) {
  if (!dateStr) return null;
  const d = new Date(dateStr);
  const now = new Date();
  const isOverdue = d < now;
  const formatted = d.toLocaleDateString('tr-TR', { day: '2-digit', month: 'short', year: 'numeric' });
  return { formatted, isOverdue };
}

// ── Task Row ──────────────────────────────────────────────────────────────────

function TaskRow({ task, onToggle, isToggling }: {
  task: CustomerTask;
  onToggle: (task: CustomerTask) => void;
  isToggling: boolean;
}) {
  const navigate = useNavigate();
  const dueDateInfo = formatDate(task.dueDate);

  return (
    <div className="flex items-start gap-3 p-4 hover:bg-muted/30 transition-colors border-b border-border last:border-0">
      {/* Status toggle */}
      <button
        className="mt-0.5 flex-shrink-0 disabled:opacity-50"
        onClick={() => onToggle(task)}
        disabled={isToggling || task.status === 'Cancelled'}
        title={`Durumu ${STATUS_NEXT[task.status]} yap`}
      >
        {isToggling ? <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" /> : STATUS_ICON[task.status]}
      </button>

      {/* Content */}
      <div className="flex-1 min-w-0">
        <div className="flex items-start justify-between gap-2">
          <p
            className={`text-sm font-medium leading-snug ${task.status === 'Done' ? 'line-through text-muted-foreground' : 'text-foreground'}`}
          >
            {task.title}
          </p>
          <Badge variant={PRIORITY_VARIANTS[task.priority]} className="text-xs flex-shrink-0">
            {PRIORITY_LABELS[task.priority]}
          </Badge>
        </div>

        <div className="flex flex-wrap items-center gap-3 mt-1.5">
          {/* Customer */}
          <button
            className="text-xs text-primary hover:underline truncate max-w-[160px]"
            onClick={() => navigate(`/customers/${task.customerId}`)}
          >
            {task.customerName || 'Müşteri'}
          </button>

          {/* Due date */}
          {dueDateInfo && (
            <span className={`text-xs flex items-center gap-1 ${dueDateInfo.isOverdue && task.status !== 'Done' ? 'text-destructive' : 'text-muted-foreground'}`}>
              <Calendar className="h-3 w-3" />
              {dueDateInfo.formatted}
              {dueDateInfo.isOverdue && task.status !== 'Done' && <span className="font-medium"> · Gecikti</span>}
            </span>
          )}

          {/* Assignee */}
          {task.assignedUserName && (
            <span className="text-xs text-muted-foreground flex items-center gap-1">
              <User className="h-3 w-3" /> {task.assignedUserName}
            </span>
          )}
        </div>

        {task.description && (
          <p className="text-xs text-muted-foreground mt-1 line-clamp-1">{task.description}</p>
        )}
      </div>
    </div>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function TasksPage() {
  const [activeStatus, setActiveStatus] = useState<TaskStatus | 'All'>('All');
  const [activePriority, setActivePriority] = useState<TaskPriority | 'All'>('All');
  const { toast } = useToast();

  const { data, isLoading } = useAllTasks(
    activeStatus !== 'All' ? { status: activeStatus } : {}
  );
  const toggleMutation = useUpdateTaskStatus();

  const tasks = data?.items ?? [];

  const filtered = activePriority === 'All'
    ? tasks
    : tasks.filter(t => t.priority === activePriority);

  const handleToggle = async (task: CustomerTask) => {
    try {
      await toggleMutation.mutateAsync({
        customerId: task.customerId,
        taskId: task.id,
        status: STATUS_NEXT[task.status],
      });
    } catch {
      toast({ title: 'Hata', description: 'Durum güncellenemedi.', variant: 'destructive' });
    }
  };

  // Count per status tab
  const counts = STATUS_TABS.reduce<Record<string, number>>((acc, tab) => {
    acc[tab.value] = tab.value === 'All' ? tasks.length : tasks.filter(t => t.status === tab.value).length;
    return acc;
  }, {});

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-foreground">Görevler</h1>
          <p className="text-muted-foreground text-sm mt-1">
            {isLoading ? 'Yükleniyor...' : `${filtered.length} görev`}
          </p>
        </div>
        <ClipboardList className="h-6 w-6 text-muted-foreground" />
      </div>

      {/* Filters */}
      <div className="flex flex-wrap items-center gap-3">
        {/* Status tabs */}
        <div className="flex bg-muted rounded-lg p-1 gap-0.5">
          {STATUS_TABS.map((tab) => (
            <button
              key={tab.value}
              onClick={() => setActiveStatus(tab.value)}
              className={`px-3 py-1.5 rounded-md text-sm font-medium transition-colors ${
                activeStatus === tab.value
                  ? 'bg-background text-foreground shadow-sm'
                  : 'text-muted-foreground hover:text-foreground'
              }`}
            >
              {tab.label}
              {!isLoading && (
                <span className="ml-1.5 text-xs text-muted-foreground">
                  {counts[tab.value]}
                </span>
              )}
            </button>
          ))}
        </div>

        {/* Priority filter */}
        <Select value={activePriority} onValueChange={(v) => setActivePriority(v as TaskPriority | 'All')}>
          <SelectTrigger className="h-9 w-36">
            <SelectValue placeholder="Öncelik" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="All">Tüm Öncelikler</SelectItem>
            <SelectItem value="Critical">Kritik</SelectItem>
            <SelectItem value="High">Yüksek</SelectItem>
            <SelectItem value="Medium">Orta</SelectItem>
            <SelectItem value="Low">Düşük</SelectItem>
          </SelectContent>
        </Select>
      </div>

      {/* Task list */}
      <Card>
        <CardContent className="p-0">
          {isLoading ? (
            <div>
              {Array.from({ length: 6 }).map((_, i) => (
                <div key={i} className="flex items-center gap-3 p-4 border-b border-border last:border-0">
                  <Skeleton className="h-4 w-4 rounded-full flex-shrink-0" />
                  <div className="flex-1 space-y-2">
                    <Skeleton className="h-4 w-64" />
                    <Skeleton className="h-3 w-40" />
                  </div>
                  <Skeleton className="h-5 w-14 rounded-full" />
                </div>
              ))}
            </div>
          ) : filtered.length === 0 ? (
            <div className="flex flex-col items-center py-16 text-center">
              <ClipboardList className="h-10 w-10 text-muted-foreground/30 mb-3" />
              <p className="text-muted-foreground text-sm">Bu filtreye uygun görev bulunamadı.</p>
            </div>
          ) : (
            filtered.map((task) => (
              <TaskRow
                key={task.id}
                task={task}
                onToggle={handleToggle}
                isToggling={
                  toggleMutation.isPending &&
                  toggleMutation.variables?.taskId === task.id
                }
              />
            ))
          )}
        </CardContent>
      </Card>
    </div>
  );
}
