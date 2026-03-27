import { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useForm, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import {
  ArrowLeft,
  Edit,
  Phone,
  Mail,
  MapPin,
  Hash,
  User,
  Building2,
  Plus,
  CheckCircle2,
  Circle,
  Loader2,
  AlertTriangle,
  DollarSign,
  Calendar,
  Target,
  MessageSquare,
  Users,
  StickyNote,
  MessageCircle,
  Trash2,
  CalendarClock,
} from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { Separator } from '@/components/ui/separator';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@/components/ui/dialog';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Textarea } from '@/components/ui/textarea';
import {
  useCustomer,
  useContactHistory,
  useCustomerTasks,
  useCustomerOpportunities,
  useUpdateTask,
  useCreateTask,
  useCreateOpportunity,
  useDeleteCustomer,
  useExtendEmsExpiration,
} from '@/api/customers';
import { CustomerStatusBadge, CustomerLabelBadge } from '@/components/customers/CustomerStatusBadge';
import { CustomerFormDialog } from '@/components/customers/CustomerForm';
import {
  AddContactHistoryDialog,
  CONTACT_TYPE_LABELS,
  CONTACT_TYPE_ICONS,
} from '@/components/customers/AddContactHistoryDialog';
import { useToast } from '@/hooks/use-toast';
import { cn } from '@/lib/utils';
import { useCanAccessFinance } from '@/lib/roles';
import { useAuthStore } from '@/stores/authStore';
import {
  useParasutStatus,
  useSyncContactToParasut,
  useCreateParasutInvoice,
  useLinkParasutContact,
  useParasutContacts,
  type InvoiceLine,
} from '@/api/parasut';
import type {
  ContactType,
  TaskPriority,
  TaskStatus,
  OpportunityStage,
  CustomerTask,
} from '@/types';

// ── Date helpers ──────────────────────────────────────────────────────────────

function formatDate(dateStr: string): string {
  return new Intl.DateTimeFormat('tr-TR', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(dateStr));
}

function formatDateShort(dateStr: string): string {
  return new Intl.DateTimeFormat('tr-TR', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  }).format(new Date(dateStr));
}

function formatRelative(dateStr: string): string {
  const date = new Date(dateStr);
  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const diffMin = Math.floor(diffMs / 60000);
  if (diffMin < 1) return 'Az önce';
  if (diffMin < 60) return `${diffMin} dk önce`;
  const diffH = Math.floor(diffMin / 60);
  if (diffH < 24) return `${diffH} sa önce`;
  const diffD = Math.floor(diffH / 24);
  if (diffD < 7) return `${diffD} gün önce`;
  if (diffD < 30) return `${Math.floor(diffD / 7)} hafta önce`;
  return formatDateShort(dateStr);
}

function formatCurrency(value: number): string {
  return new Intl.NumberFormat('tr-TR', {
    style: 'currency',
    currency: 'TRY',
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
  }).format(value);
}

// ── Label maps ────────────────────────────────────────────────────────────────

const PRIORITY_LABELS: Record<TaskPriority, string> = {
  Low: 'Düşük',
  Medium: 'Orta',
  High: 'Yüksek',
  Critical: 'Kritik',
};

const PRIORITY_CLASSES: Record<TaskPriority, string> = {
  Low: 'bg-slate-500/15 text-slate-400 border-slate-500/30',
  Medium: 'bg-blue-500/15 text-blue-400 border-blue-500/30',
  High: 'bg-amber-500/15 text-amber-400 border-amber-500/30',
  Critical: 'bg-red-500/15 text-red-400 border-red-500/30',
};

const TASK_STATUS_LABELS: Record<TaskStatus, string> = {
  Todo: 'Yapılacak',
  InProgress: 'Devam Ediyor',
  Done: 'Tamamlandı',
  Cancelled: 'İptal',
};

const STAGE_LABELS: Record<OpportunityStage, string> = {
  YeniArama:  'Yeni Arama',
  Potansiyel: 'Potansiyel',
  Demo:       'Demo',
  Musteri:    'Müşteri',
  Kayip:      'Kayıp',
};

const STAGE_CLASSES: Record<OpportunityStage, string> = {
  YeniArama:  'bg-slate-500/15 text-slate-400 border-slate-500/30',
  Potansiyel: 'bg-blue-500/15 text-blue-400 border-blue-500/30',
  Demo:       'bg-violet-500/15 text-violet-400 border-violet-500/30',
  Musteri:    'bg-green-500/15 text-green-400 border-green-500/30',
  Kayip:      'bg-red-500/15 text-red-400 border-red-500/30',
};

const CONTACT_TYPE_BG: Record<ContactType, string> = {
  Call: 'bg-blue-500/15',
  Email: 'bg-violet-500/15',
  Meeting: 'bg-green-500/15',
  Note: 'bg-amber-500/15',
  WhatsApp: 'bg-emerald-500/15',
  Visit: 'bg-rose-500/15',
};

const CONTACT_TYPE_TEXT: Record<ContactType, string> = {
  Call: 'text-blue-400',
  Email: 'text-violet-400',
  Meeting: 'text-green-400',
  Note: 'text-amber-400',
  WhatsApp: 'text-emerald-400',
  Visit: 'text-rose-400',
};

// ── Quick Invoice Form (inline in Cari tab) ───────────────────────────────────

interface QuickInvoiceFormProps {
  projectId: string;
  contactId: string;
  customerName: string;
  onSuccess: () => void;
  onError: () => void;
  createInvoice: ReturnType<typeof useCreateParasutInvoice>;
}

function QuickInvoiceForm({ projectId, contactId, customerName, onSuccess, onError, createInvoice }: QuickInvoiceFormProps) {
  const today = new Date().toISOString().split('T')[0];
  const in30 = new Date(Date.now() + 30 * 86400000).toISOString().split('T')[0];
  const [description, setDescription] = useState('');
  const [issueDate, setIssueDate] = useState(today);
  const [dueDate, setDueDate] = useState(in30);
  const [lines, setLines] = useState<InvoiceLine[]>([{ description: '', quantity: 1, unitPrice: 0, vatRate: 20, discountValue: 0, unit: 'Adet' }]);

  const addLine = () => setLines(l => [...l, { description: '', quantity: 1, unitPrice: 0, vatRate: 20, discountValue: 0, unit: 'Adet' }]);
  const removeLine = (i: number) => setLines(l => l.filter((_, idx) => idx !== i));
  const updateLine = (i: number, field: keyof InvoiceLine, value: string | number) =>
    setLines(l => l.map((ln, idx) => idx === i ? { ...ln, [field]: value } : ln));

  const total = lines.reduce((s, l) => {
    const base = (l.quantity || 0) * (l.unitPrice || 0) * (1 - (l.discountValue || 0) / 100);
    return s + base * (1 + (l.vatRate || 0) / 100);
  }, 0);

  async function submit() {
    try {
      await createInvoice.mutateAsync({
        projectId,
        parasutContactId: contactId || undefined,
        issueDate,
        dueDate,
        currency: 'TRL',
        description: description || `${customerName} faturası`,
        lines,
      });
      onSuccess();
    } catch {
      onError();
    }
  }

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-1">
          <Label className="text-xs">Açıklama</Label>
          <Input value={description} onChange={e => setDescription(e.target.value)} className="h-8 text-sm" placeholder={`${customerName} faturası`} />
        </div>
        <div className="space-y-1" />
        <div className="space-y-1">
          <Label className="text-xs">Fatura Tarihi</Label>
          <Input type="date" value={issueDate} onChange={e => setIssueDate(e.target.value)} className="h-8 text-sm" />
        </div>
        <div className="space-y-1">
          <Label className="text-xs">Vade Tarihi</Label>
          <Input type="date" value={dueDate} onChange={e => setDueDate(e.target.value)} className="h-8 text-sm" />
        </div>
      </div>

      <div className="space-y-1">
        <div className="flex items-center justify-between">
          <Label className="text-xs">Kalemler</Label>
          <Button type="button" variant="ghost" size="sm" className="h-6 text-xs gap-1" onClick={addLine}>
            <Plus className="h-3 w-3" /> Ekle
          </Button>
        </div>
        {lines.map((l, i) => (
          <div key={i} className="grid grid-cols-[2fr_1fr_1fr_1fr_auto] gap-2 items-center">
            <Input value={l.description ?? ''} onChange={e => updateLine(i, 'description', e.target.value)} className="h-8 text-xs" placeholder="Ürün/hizmet" />
            <Input type="number" value={l.quantity} onChange={e => updateLine(i, 'quantity', Number(e.target.value))} className="h-8 text-xs" placeholder="Miktar" />
            <Input type="number" value={l.unitPrice} onChange={e => updateLine(i, 'unitPrice', Number(e.target.value))} className="h-8 text-xs" placeholder="Fiyat" />
            <Input type="number" value={l.vatRate} onChange={e => updateLine(i, 'vatRate', Number(e.target.value))} className="h-8 text-xs" placeholder="KDV%" />
            <Button type="button" variant="ghost" size="icon" className="h-8 w-8 text-muted-foreground" onClick={() => removeLine(i)} disabled={lines.length === 1}>
              <Trash2 className="h-3 w-3" />
            </Button>
          </div>
        ))}
        <p className="text-xs text-right font-medium text-foreground pt-1">
          Toplam: {new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY', minimumFractionDigits: 2 }).format(total)}
        </p>
      </div>

      <Button size="sm" className="gap-2 w-full" onClick={submit} disabled={createInvoice.isPending}>
        {createInvoice.isPending && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
        Paraşüt'e Gönder
      </Button>
    </div>
  );
}

// ── Tab type ──────────────────────────────────────────────────────────────────

type ActiveTab = 'timeline' | 'tasks' | 'opportunities' | 'cari';

// ── Inline Schemas ────────────────────────────────────────────────────────────

const createTaskSchema = z.object({
  title: z.string().min(1, 'Başlık gereklidir'),
  description: z.string(),
  dueDate: z.string(),
  priority: z.enum(['Low', 'Medium', 'High', 'Critical'] as const),
});
type CreateTaskFormData = z.infer<typeof createTaskSchema>;

const createOpportunitySchema = z.object({
  title: z.string().min(1, 'Başlık gereklidir'),
  value: z.string(),
  stage: z.enum([
    'YeniArama',
    'Potansiyel',
    'Demo',
    'Musteri',
    'Kayip',
  ] as const),
  probability: z.string(),
  expectedCloseDate: z.string(),
});
type CreateOpportunityFormData = z.infer<typeof createOpportunitySchema>;

// ── Create Task Dialog ────────────────────────────────────────────────────────

interface CreateTaskDialogProps {
  isOpen: boolean;
  onClose: () => void;
  customerId: string;
}

function CreateTaskDialog({ isOpen, onClose, customerId }: CreateTaskDialogProps) {
  const { toast } = useToast();
  const mutation = useCreateTask();

  const {
    register,
    handleSubmit,
    control,
    reset,
    formState: { errors },
  } = useForm<CreateTaskFormData>({
    resolver: zodResolver(createTaskSchema),
    defaultValues: { title: '', description: '', dueDate: '', priority: 'Medium' },
  });

  const handleClose = () => { reset(); onClose(); };

  const onSubmit = async (data: CreateTaskFormData) => {
    try {
      await mutation.mutateAsync({
        customerId,
        title: data.title,
        description: data.description || undefined,
        dueDate: data.dueDate || undefined,
        priority: data.priority,
      });
      toast({ title: 'Görev oluşturuldu', description: `"${data.title}" görevi eklendi.` });
      handleClose();
    } catch {
      toast({ title: 'Hata', description: 'Görev eklenemedi.', variant: 'destructive' });
    }
  };

  return (
    <Dialog open={isOpen} onOpenChange={(open) => !open && handleClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Yeni Görev Ekle</DialogTitle>
        </DialogHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4 py-1">
          <div className="space-y-1.5">
            <Label htmlFor="task-title">Başlık <span className="text-destructive">*</span></Label>
            <Input
              id="task-title"
              placeholder="Görev başlığı"
              {...register('title')}
              className={cn('h-11', errors.title && 'border-destructive')}
            />
            {errors.title && <p className="text-xs text-destructive">{errors.title.message}</p>}
          </div>
          <div className="space-y-1.5">
            <Label>Öncelik</Label>
            <Controller
              control={control}
              name="priority"
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger className="h-11">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="Low">Düşük</SelectItem>
                    <SelectItem value="Medium">Orta</SelectItem>
                    <SelectItem value="High">Yüksek</SelectItem>
                    <SelectItem value="Critical">Kritik</SelectItem>
                  </SelectContent>
                </Select>
              )}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="task-dueDate">Bitiş Tarihi</Label>
            <Input id="task-dueDate" type="date" {...register('dueDate')} className="h-11" />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="task-desc">Açıklama</Label>
            <Textarea id="task-desc" placeholder="Görev detayları..." rows={2} {...register('description')} />
          </div>
          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={handleClose} disabled={mutation.isPending} className="h-11">İptal</Button>
            <Button type="submit" disabled={mutation.isPending} className="h-11 min-w-[100px]">
              {mutation.isPending && <Loader2 className="h-4 w-4 animate-spin mr-2" />}
              Oluştur
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ── Create Opportunity Dialog ─────────────────────────────────────────────────

interface CreateOpportunityDialogProps {
  isOpen: boolean;
  onClose: () => void;
  customerId: string;
}

function CreateOpportunityDialog({ isOpen, onClose, customerId }: CreateOpportunityDialogProps) {
  const { toast } = useToast();
  const mutation = useCreateOpportunity();

  const {
    register,
    handleSubmit,
    control,
    reset,
    formState: { errors },
  } = useForm<CreateOpportunityFormData>({
    resolver: zodResolver(createOpportunitySchema),
    defaultValues: {
      title: '',
      value: '',
      stage: 'YeniArama',
      probability: '',
      expectedCloseDate: '',
    },
  });

  const handleClose = () => { reset(); onClose(); };

  const onSubmit = async (data: CreateOpportunityFormData) => {
    try {
      await mutation.mutateAsync({
        customerId,
        title: data.title,
        value: data.value ? parseFloat(data.value) : undefined,
        stage: data.stage,
        probability: data.probability ? parseInt(data.probability) : undefined,
        expectedCloseDate: data.expectedCloseDate || undefined,
      });
      toast({ title: 'Fırsat oluşturuldu', description: `"${data.title}" fırsatı eklendi.` });
      handleClose();
    } catch {
      toast({ title: 'Hata', description: 'Fırsat eklenemedi.', variant: 'destructive' });
    }
  };

  return (
    <Dialog open={isOpen} onOpenChange={(open) => !open && handleClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Yeni Fırsat Ekle</DialogTitle>
        </DialogHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4 py-1">
          <div className="space-y-1.5">
            <Label htmlFor="opp-title">Başlık <span className="text-destructive">*</span></Label>
            <Input
              id="opp-title"
              placeholder="Fırsat başlığı"
              {...register('title')}
              className={cn('h-11', errors.title && 'border-destructive')}
            />
            {errors.title && <p className="text-xs text-destructive">{errors.title.message}</p>}
          </div>
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-1.5">
              <Label htmlFor="opp-value">Değer (₺)</Label>
              <Input id="opp-value" type="number" placeholder="0" {...register('value')} className="h-11" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="opp-prob">Olasılık (%)</Label>
              <Input id="opp-prob" type="number" min="0" max="100" placeholder="50" {...register('probability')} className="h-11" />
            </div>
          </div>
          <div className="space-y-1.5">
            <Label>Aşama</Label>
            <Controller
              control={control}
              name="stage"
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger className="h-11">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {(Object.keys(STAGE_LABELS) as OpportunityStage[]).map((s) => (
                      <SelectItem key={s} value={s}>{STAGE_LABELS[s]}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="opp-date">Beklenen Kapanış</Label>
            <Input id="opp-date" type="date" {...register('expectedCloseDate')} className="h-11" />
          </div>
          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={handleClose} disabled={mutation.isPending} className="h-11">İptal</Button>
            <Button type="submit" disabled={mutation.isPending} className="h-11 min-w-[100px]">
              {mutation.isPending && <Loader2 className="h-4 w-4 animate-spin mr-2" />}
              Oluştur
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ── Task Row ──────────────────────────────────────────────────────────────────

interface TaskRowProps {
  task: CustomerTask;
}

function TaskRow({ task }: TaskRowProps) {
  const { toast } = useToast();
  const updateTask = useUpdateTask();
  const isDone = task.status === 'Done';
  const isCancelled = task.status === 'Cancelled';

  const toggleDone = async () => {
    const newStatus: TaskStatus = isDone ? 'Todo' : 'Done';
    try {
      await updateTask.mutateAsync({
        id: task.id,
        customerId: task.customerId,
        title: task.title,
        description: task.description ?? undefined,
        dueDate: task.dueDate ?? undefined,
        priority: task.priority,
        assignedUserId: task.assignedUserId ?? undefined,
        status: newStatus,
      });
    } catch {
      toast({ title: 'Hata', description: 'Görev güncellenemedi.', variant: 'destructive' });
    }
  };

  return (
    <div
      className={cn(
        'flex items-start gap-3 p-4 rounded-lg border transition-colors',
        isDone || isCancelled
          ? 'border-border/50 bg-muted/20 opacity-70'
          : 'border-border hover:bg-muted/30'
      )}
    >
      <button
        onClick={toggleDone}
        disabled={updateTask.isPending || isCancelled}
        className="mt-0.5 flex-shrink-0 text-muted-foreground hover:text-primary transition-colors disabled:opacity-50"
        title={isDone ? 'Tamamlanmadı olarak işaretle' : 'Tamamlandı olarak işaretle'}
      >
        {updateTask.isPending ? (
          <Loader2 className="h-5 w-5 animate-spin" />
        ) : isDone ? (
          <CheckCircle2 className="h-5 w-5 text-green-500" />
        ) : (
          <Circle className="h-5 w-5" />
        )}
      </button>

      <div className="flex-1 min-w-0">
        <div className="flex items-start justify-between gap-2">
          <p className={cn('font-medium text-sm leading-tight', isDone && 'line-through text-muted-foreground')}>
            {task.title}
          </p>
          <div className="flex items-center gap-1 flex-shrink-0">
            <span
              className={cn(
                'inline-flex items-center rounded-full border px-2 py-0.5 text-xs font-semibold',
                PRIORITY_CLASSES[task.priority]
              )}
            >
              {PRIORITY_LABELS[task.priority]}
            </span>
          </div>
        </div>
        {task.description && (
          <p className="text-xs text-muted-foreground mt-1 line-clamp-2">{task.description}</p>
        )}
        <div className="flex items-center gap-3 mt-1.5 flex-wrap">
          {task.dueDate && (
            <div className="flex items-center gap-1 text-xs text-muted-foreground">
              <Calendar className="h-3 w-3" />
              {formatDateShort(task.dueDate)}
            </div>
          )}
          {task.assignedUserName && (
            <div className="flex items-center gap-1 text-xs text-muted-foreground">
              <User className="h-3 w-3" />
              {task.assignedUserName}
            </div>
          )}
          <span
            className={cn(
              'text-xs',
              task.status === 'Done'
                ? 'text-green-500'
                : task.status === 'InProgress'
                ? 'text-blue-400'
                : task.status === 'Cancelled'
                ? 'text-red-400'
                : 'text-muted-foreground'
            )}
          >
            {TASK_STATUS_LABELS[task.status]}
          </span>
        </div>
      </div>
    </div>
  );
}

// ── Extend EMS Expiration Dialog ──────────────────────────────────────────────

const DURATION_OPTIONS = [
  { label: '30 Gün',  durationType: 'Days',   amount: 30 },
  { label: '1 Ay',    durationType: 'Months',  amount: 1  },
  { label: '1 Yıl',   durationType: 'Years',   amount: 1  },
] as const;

interface ExtendExpirationDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  companyName: string;
  currentExpiry: string | null;
  extendExpiration: ReturnType<typeof useExtendEmsExpiration>;
}

function ExtendExpirationDialog({
  open,
  onOpenChange,
  companyName,
  currentExpiry,
  extendExpiration,
}: ExtendExpirationDialogProps) {
  const { toast } = useToast();
  const [selected, setSelected] = useState<(typeof DURATION_OPTIONS)[number] | null>(null);

  async function handleConfirm() {
    if (!selected) return;
    try {
      const result = await extendExpiration.mutateAsync({
        durationType: selected.durationType,
        amount: selected.amount,
      });
      const newDate = new Date(result.newExpirationDate).toLocaleDateString('tr-TR', {
        day: 'numeric', month: 'long', year: 'numeric',
      });
      const invoiceMsg = result.parasutInvoiceCreated
        ? " Paraşüt'te taslak fatura oluşturuldu."
        : '';
      toast({
        title: 'Süre uzatıldı',
        description: `Yeni bitiş tarihi: ${newDate}.${invoiceMsg}`,
      });
      onOpenChange(false);
      setSelected(null);
    } catch {
      toast({ title: 'Hata', description: 'Süre uzatılamadı.', variant: 'destructive' });
    }
  }

  return (
    <Dialog open={open} onOpenChange={v => { onOpenChange(v); if (!v) setSelected(null); }}>
      <DialogContent className="max-w-sm">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <CalendarClock className="h-5 w-5 text-amber-400" />
            EMS Süre Uzat
          </DialogTitle>
        </DialogHeader>
        <div className="space-y-4 py-2">
          <p className="text-sm text-muted-foreground">
            <span className="font-medium text-foreground">{companyName}</span> firmasının EMS
            abonelik süresini uzatın.
          </p>
          {currentExpiry && (
            <p className="text-xs text-muted-foreground">
              Mevcut bitiş:{' '}
              <span className="font-medium text-foreground">
                {new Date(currentExpiry).toLocaleDateString('tr-TR', {
                  day: 'numeric', month: 'long', year: 'numeric',
                })}
              </span>
            </p>
          )}
          <div className="grid grid-cols-3 gap-2">
            {DURATION_OPTIONS.map(opt => (
              <button
                key={opt.label}
                onClick={() => setSelected(opt)}
                className={cn(
                  'rounded-lg border px-3 py-4 text-sm font-medium transition-colors text-center',
                  selected?.label === opt.label
                    ? 'border-amber-500 bg-amber-500/15 text-amber-400'
                    : 'border-border text-muted-foreground hover:border-amber-500/50 hover:text-foreground'
                )}
              >
                {opt.label}
                {opt.durationType !== 'Days' && (
                  <span className="block text-[10px] mt-1 opacity-60">
                    Paraşüt fatura
                  </span>
                )}
              </button>
            ))}
          </div>
        </div>
        <DialogFooter>
          <Button variant="ghost" onClick={() => { onOpenChange(false); setSelected(null); }}>
            İptal
          </Button>
          <Button
            onClick={handleConfirm}
            disabled={!selected || extendExpiration.isPending}
            className="bg-amber-500 hover:bg-amber-600 text-white"
          >
            {extendExpiration.isPending ? (
              <><Loader2 className="h-4 w-4 mr-1.5 animate-spin" />Uzatılıyor...</>
            ) : (
              <><CalendarClock className="h-4 w-4 mr-1.5" />Uzat</>
            )}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Main Component ────────────────────────────────────────────────────────────

export function CustomerDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { toast } = useToast();
  const customerId = id ?? '';
  const { currentProjectId } = useAuthStore();
  const canAccessFinance = useCanAccessFinance();

  // UI state
  const [activeTab, setActiveTab] = useState<ActiveTab>('timeline');
  const [showEditDialog, setShowEditDialog] = useState(false);
  const [showAddContact, setShowAddContact] = useState(false);
  const [addContactType, setAddContactType] = useState<ContactType>('Note');
  const [showCreateTask, setShowCreateTask] = useState(false);
  const [showCreateOpportunity, setShowCreateOpportunity] = useState(false);

  // Cari (Paraşüt) state
  const [showCariInvoiceForm, setShowCariInvoiceForm] = useState(false);
  const [showLinkDialog, setShowLinkDialog] = useState(false);
  const [linkSearch, setLinkSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [linkPage, setLinkPage] = useState(1);
  const parasutStatus = useParasutStatus(currentProjectId);
  const syncContact = useSyncContactToParasut();
  const linkContact = useLinkParasutContact();
  const createInvoice = useCreateParasutInvoice();
  const parasutContactsQuery = useParasutContacts(currentProjectId, linkPage, showLinkDialog, debouncedSearch);

  // EMS extend expiration state
  const [showExtendDialog, setShowExtendDialog] = useState(false);
  const extendExpiration = useExtendEmsExpiration(id ?? '');

  useEffect(() => {
    const t = setTimeout(() => { setDebouncedSearch(linkSearch); setLinkPage(1); }, 400);
    return () => clearTimeout(t);
  }, [linkSearch]);

  // Queries
  const {
    data: customer,
    isLoading: customerLoading,
    isError: customerError,
  } = useCustomer(customerId);

  const { data: historyData, isLoading: historyLoading } = useContactHistory(customerId);
  const { data: tasksData, isLoading: tasksLoading } = useCustomerTasks(customerId);
  const { data: oppsData, isLoading: oppsLoading } = useCustomerOpportunities(customerId);
  const deleteMutation = useDeleteCustomer();

  function openAddContact(type: ContactType) {
    setAddContactType(type);
    setShowAddContact(true);
  }

  async function handleDelete() {
    if (!customer) return;
    if (!window.confirm(`"${customer.companyName}" müşterisini silmek istediğinize emin misiniz?`)) return;
    try {
      await deleteMutation.mutateAsync(customerId);
      toast({ title: 'Müşteri silindi', description: `${customer.companyName} kayıttan silindi.` });
      navigate('/customers');
    } catch {
      toast({ title: 'Hata', description: 'Müşteri silinemedi.', variant: 'destructive' });
    }
  }

  // ── Loading ──

  if (customerLoading) {
    return (
      <div className="space-y-6">
        <div className="flex items-center gap-4">
          <Skeleton className="h-10 w-32" />
        </div>
        <Card>
          <CardContent className="p-6 space-y-4">
            <div className="flex items-start justify-between">
              <div className="space-y-2">
                <Skeleton className="h-7 w-64" />
                <Skeleton className="h-5 w-32" />
              </div>
              <Skeleton className="h-10 w-24" />
            </div>
            <Separator />
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              {Array.from({ length: 4 }).map((_, i) => (
                <Skeleton key={i} className="h-12 w-full" />
              ))}
            </div>
          </CardContent>
        </Card>
      </div>
    );
  }

  // ── Error / Not Found ──

  if (customerError || !customer) {
    return (
      <div className="flex flex-col items-center justify-center py-24 text-center">
        <div className="w-20 h-20 rounded-full bg-destructive/10 flex items-center justify-center mb-6">
          <AlertTriangle className="h-10 w-10 text-destructive/60" />
        </div>
        <p className="text-xl font-semibold text-foreground mb-2">Müşteri bulunamadı</p>
        <p className="text-muted-foreground mb-6">
          Bu müşteri kaydı mevcut değil veya erişim yetkiniz bulunmuyor.
        </p>
        <Button variant="outline" onClick={() => navigate('/customers')} className="gap-2">
          <ArrowLeft className="h-4 w-4" />
          Müşterilere Dön
        </Button>
      </div>
    );
  }

  const tabItems: { id: ActiveTab; label: string; count?: number }[] = [
    {
      id: 'timeline',
      label: 'Aktivite Geçmişi',
      count: historyData?.totalCount,
    },
    {
      id: 'tasks',
      label: 'Görevler',
      count: tasksData?.items.filter((t) => t.status !== 'Done' && t.status !== 'Cancelled').length,
    },
    {
      id: 'opportunities',
      label: 'Fırsatlar',
      count: oppsData?.totalCount,
    },
    ...(canAccessFinance ? [{ id: 'cari' as ActiveTab, label: 'Cari / Fatura' }] : []),
  ];

  return (
    <div className="space-y-6">
      {/* ── Back nav ── */}
      <div className="flex items-center justify-between">
        <Button
          variant="ghost"
          className="gap-2 text-muted-foreground hover:text-foreground -ml-2"
          onClick={() => navigate('/customers')}
        >
          <ArrowLeft className="h-4 w-4" />
          Müşteriler
        </Button>
        <div className="flex items-center gap-2">
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="outline" className="gap-2 h-10">
                <Plus className="h-4 w-4" />
                Aktivite Ekle
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem onClick={() => openAddContact('Call')} className="gap-2">
                <Phone className="h-4 w-4 text-blue-400" /> Arama Kaydet
              </DropdownMenuItem>
              <DropdownMenuItem onClick={() => openAddContact('Email')} className="gap-2">
                <Mail className="h-4 w-4 text-violet-400" /> E-posta Kaydet
              </DropdownMenuItem>
              <DropdownMenuItem onClick={() => openAddContact('Meeting')} className="gap-2">
                <Users className="h-4 w-4 text-green-400" /> Toplantı Ekle
              </DropdownMenuItem>
              <DropdownMenuItem onClick={() => openAddContact('WhatsApp')} className="gap-2">
                <MessageCircle className="h-4 w-4 text-emerald-400" /> WhatsApp Kaydet
              </DropdownMenuItem>
              <DropdownMenuItem onClick={() => openAddContact('Visit')} className="gap-2">
                <MapPin className="h-4 w-4 text-rose-400" /> Ziyaret Ekle
              </DropdownMenuItem>
              <DropdownMenuItem onClick={() => openAddContact('Note')} className="gap-2">
                <StickyNote className="h-4 w-4 text-amber-400" /> Not Ekle
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
          {/* EMS-only: extend expiration */}
          {customer?.legacyId && !customer.legacyId.startsWith('PC-') && (
            <Button
              variant="outline"
              className="gap-2 h-10 border-amber-500/40 text-amber-400 hover:bg-amber-500/10"
              onClick={() => setShowExtendDialog(true)}
            >
              <CalendarClock className="h-4 w-4" />
              Süre Uzat
            </Button>
          )}
          <Button onClick={() => setShowEditDialog(true)} className="gap-2 h-10">
            <Edit className="h-4 w-4" />
            Düzenle
          </Button>
        </div>
      </div>

      {/* ── Customer Header Card ── */}
      <Card>
        <CardContent className="p-6">
          <div className="flex flex-col sm:flex-row sm:items-start gap-4">
            {/* Avatar + Name */}
            <div className="flex items-start gap-4 flex-1 min-w-0">
              <div className="w-16 h-16 rounded-2xl bg-primary/10 flex items-center justify-center flex-shrink-0">
                <span className="text-primary text-xl font-bold">
                  {customer.companyName.slice(0, 2).toUpperCase()}
                </span>
              </div>
              <div className="min-w-0 flex-1 pt-1">
                <div className="flex flex-wrap items-center gap-2 mb-1">
                  <h1 className="text-xl font-bold text-foreground leading-tight">
                    {customer.companyName}
                  </h1>
                  <CustomerStatusBadge status={customer.status} />
                  {customer.label && <CustomerLabelBadge label={customer.label} />}
                </div>
                <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
                  {customer.segment && (
                    <span className="text-sm text-muted-foreground">{customer.segment}</span>
                  )}
                  {customer.code && (
                    <span className="text-sm text-muted-foreground font-mono">#{customer.code}</span>
                  )}
                  {customer.assignedUserName && (
                    <div className="flex items-center gap-1 text-sm text-muted-foreground">
                      <User className="h-3.5 w-3.5" />
                      {customer.assignedUserName}
                    </div>
                  )}
                </div>
              </div>
            </div>

            {/* Delete button */}
            <Button
              variant="ghost"
              size="sm"
              className="h-9 w-9 p-0 text-muted-foreground hover:text-destructive hover:bg-destructive/10 self-start"
              onClick={handleDelete}
              disabled={deleteMutation.isPending}
              title="Müşteriyi sil"
            >
              {deleteMutation.isPending ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : (
                <Trash2 className="h-4 w-4" />
              )}
            </Button>
          </div>

          <Separator className="my-5" />

          {/* Contact info grid */}
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <InfoItem
              icon={Mail}
              label="E-posta"
              value={customer.email}
              href={customer.email ? `mailto:${customer.email}` : undefined}
            />
            <InfoItem
              icon={Phone}
              label="Telefon"
              value={customer.phone}
              href={customer.phone ? `tel:${customer.phone}` : undefined}
            />
            <InfoItem
              icon={User}
              label="İletişim Kişisi"
              value={customer.contactName}
            />
            <InfoItem
              icon={MapPin}
              label="Adres"
              value={customer.address}
            />
            {(customer.taxNumber || customer.taxUnit) && (
              <InfoItem
                icon={Hash}
                label="Vergi No / Dairesi"
                value={
                  [customer.taxNumber, customer.taxUnit].filter(Boolean).join(' · ') || null
                }
              />
            )}
            <InfoItem
              icon={Building2}
              label="Kayıt Tarihi"
              value={formatDateShort(customer.createdAt)}
            />
          </div>
        </CardContent>
      </Card>

      {/* ── Tabs ── */}
      <div>
        {/* Tab bar */}
        <div className="flex border-b border-border overflow-x-auto">
          {tabItems.map((tab) => (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              className={cn(
                'flex items-center gap-2 px-4 py-3 text-sm font-medium whitespace-nowrap transition-colors border-b-2 -mb-px',
                'min-h-[44px] touch-manipulation',
                activeTab === tab.id
                  ? 'border-primary text-primary'
                  : 'border-transparent text-muted-foreground hover:text-foreground hover:border-border'
              )}
            >
              {tab.label}
              {tab.count !== undefined && tab.count > 0 && (
                <span
                  className={cn(
                    'text-xs rounded-full px-1.5 py-0.5 font-semibold',
                    activeTab === tab.id
                      ? 'bg-primary/20 text-primary'
                      : 'bg-muted text-muted-foreground'
                  )}
                >
                  {tab.count}
                </span>
              )}
            </button>
          ))}
        </div>

        {/* Tab content */}
        <div className="mt-4">
          {/* ── Timeline Tab ── */}
          {activeTab === 'timeline' && (
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <p className="text-sm text-muted-foreground">
                  {historyData
                    ? `${historyData.totalCount} aktivite kaydı`
                    : 'Yükleniyor...'}
                </p>
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <Button variant="outline" size="sm" className="gap-2 h-9">
                      <Plus className="h-3.5 w-3.5" />
                      Ekle
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end">
                    <DropdownMenuItem onClick={() => openAddContact('Call')} className="gap-2">
                      <Phone className="h-4 w-4 text-blue-400" /> Arama
                    </DropdownMenuItem>
                    <DropdownMenuItem onClick={() => openAddContact('Email')} className="gap-2">
                      <Mail className="h-4 w-4 text-violet-400" /> E-posta
                    </DropdownMenuItem>
                    <DropdownMenuItem onClick={() => openAddContact('Meeting')} className="gap-2">
                      <Users className="h-4 w-4 text-green-400" /> Toplantı
                    </DropdownMenuItem>
                    <DropdownMenuItem onClick={() => openAddContact('Note')} className="gap-2">
                      <StickyNote className="h-4 w-4 text-amber-400" /> Not
                    </DropdownMenuItem>
                  </DropdownMenuContent>
                </DropdownMenu>
              </div>

              {historyLoading && (
                <div className="space-y-3">
                  {Array.from({ length: 5 }).map((_, i) => (
                    <Skeleton key={i} className="h-20 w-full rounded-lg" />
                  ))}
                </div>
              )}

              {!historyLoading && (!historyData?.items || historyData.items.length === 0) && (
                <div className="flex flex-col items-center justify-center py-16 text-center">
                  <div className="w-16 h-16 rounded-full bg-muted flex items-center justify-center mb-4">
                    <MessageSquare className="h-8 w-8 text-muted-foreground/40" />
                  </div>
                  <p className="font-medium text-foreground mb-1">Henüz aktivite yok</p>
                  <p className="text-sm text-muted-foreground mb-4">
                    Bu müşteriyle ilk iletişimi kaydedelim.
                  </p>
                  <Button
                    variant="outline"
                    className="gap-2 h-10"
                    onClick={() => openAddContact('Note')}
                  >
                    <Plus className="h-4 w-4" />
                    İlk Aktiviteyi Ekle
                  </Button>
                </div>
              )}

              {!historyLoading && historyData?.items && historyData.items.length > 0 && (
                <div className="relative">
                  {/* Timeline line */}
                  <div className="absolute left-6 top-6 bottom-2 w-px bg-border hidden sm:block" />
                  <div className="space-y-3">
                    {historyData.items.map((entry) => {
                      const Icon = CONTACT_TYPE_ICONS[entry.type] ?? StickyNote;
                      return (
                        <div key={entry.id} className="flex gap-4">
                          {/* Icon bubble */}
                          <div
                            className={cn(
                              'w-12 h-12 rounded-full flex items-center justify-center flex-shrink-0 z-10',
                              'hidden sm:flex',
                              CONTACT_TYPE_BG[entry.type]
                            )}
                          >
                            <Icon
                              className={cn('h-5 w-5', CONTACT_TYPE_TEXT[entry.type])}
                            />
                          </div>

                          {/* Entry card */}
                          <Card className="flex-1 border-border/60">
                            <CardContent className="p-4">
                              <div className="flex items-start justify-between gap-2 mb-2">
                                <div className="flex items-center gap-2 flex-wrap">
                                  {/* Mobile icon */}
                                  <div
                                    className={cn(
                                      'sm:hidden w-6 h-6 rounded-full flex items-center justify-center flex-shrink-0',
                                      CONTACT_TYPE_BG[entry.type]
                                    )}
                                  >
                                    <Icon
                                      className={cn('h-3.5 w-3.5', CONTACT_TYPE_TEXT[entry.type])}
                                    />
                                  </div>
                                  <span
                                    className={cn(
                                      'text-xs font-semibold uppercase tracking-wide',
                                      CONTACT_TYPE_TEXT[entry.type]
                                    )}
                                  >
                                    {CONTACT_TYPE_LABELS[entry.type]}
                                  </span>
                                  {entry.subject && (
                                    <p className="font-medium text-sm text-foreground">
                                      {entry.subject}
                                    </p>
                                  )}
                                </div>
                                <span className="text-xs text-muted-foreground whitespace-nowrap flex-shrink-0">
                                  {formatRelative(entry.contactedAt)}
                                </span>
                              </div>
                              {entry.content && (
                                <p className="text-sm text-muted-foreground mb-2 leading-relaxed">
                                  {entry.content}
                                </p>
                              )}
                              {entry.outcome && (
                                <div className="flex items-start gap-2 text-sm p-2 rounded-md bg-muted/40 border border-border/50">
                                  <Target className="h-4 w-4 text-primary flex-shrink-0 mt-0.5" />
                                  <span className="text-foreground">{entry.outcome}</span>
                                </div>
                              )}
                              <div className="flex items-center gap-3 mt-2 text-xs text-muted-foreground">
                                {entry.createdByUserName && (
                                  <span className="flex items-center gap-1">
                                    <User className="h-3 w-3" />
                                    {entry.createdByUserName}
                                  </span>
                                )}
                                <span>{formatDate(entry.contactedAt)}</span>
                              </div>
                            </CardContent>
                          </Card>
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}
            </div>
          )}

          {/* ── Tasks Tab ── */}
          {activeTab === 'tasks' && (
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <p className="text-sm text-muted-foreground">
                  {tasksData
                    ? `${tasksData.totalCount} görev`
                    : 'Yükleniyor...'}
                </p>
                <Button
                  variant="outline"
                  size="sm"
                  className="gap-2 h-9"
                  onClick={() => setShowCreateTask(true)}
                >
                  <Plus className="h-3.5 w-3.5" />
                  Yeni Görev
                </Button>
              </div>

              {tasksLoading && (
                <div className="space-y-3">
                  {Array.from({ length: 4 }).map((_, i) => (
                    <Skeleton key={i} className="h-20 w-full rounded-lg" />
                  ))}
                </div>
              )}

              {!tasksLoading && (!tasksData?.items || tasksData.items.length === 0) && (
                <div className="flex flex-col items-center justify-center py-16 text-center">
                  <div className="w-16 h-16 rounded-full bg-muted flex items-center justify-center mb-4">
                    <CheckCircle2 className="h-8 w-8 text-muted-foreground/40" />
                  </div>
                  <p className="font-medium text-foreground mb-1">Görev yok</p>
                  <p className="text-sm text-muted-foreground mb-4">
                    Bu müşteri için henüz görev oluşturulmadı.
                  </p>
                  <Button
                    variant="outline"
                    className="gap-2 h-10"
                    onClick={() => setShowCreateTask(true)}
                  >
                    <Plus className="h-4 w-4" />
                    Görev Oluştur
                  </Button>
                </div>
              )}

              {!tasksLoading && tasksData?.items && tasksData.items.length > 0 && (
                <div className="space-y-2">
                  {/* Active tasks first */}
                  {tasksData.items
                    .filter((t) => t.status !== 'Done' && t.status !== 'Cancelled')
                    .map((task) => (
                      <TaskRow key={task.id} task={task} />
                    ))}
                  {/* Done tasks */}
                  {tasksData.items.filter((t) => t.status === 'Done' || t.status === 'Cancelled')
                    .length > 0 && (
                    <>
                      <div className="flex items-center gap-2 py-2">
                        <div className="flex-1 h-px bg-border" />
                        <span className="text-xs text-muted-foreground">Tamamlananlar</span>
                        <div className="flex-1 h-px bg-border" />
                      </div>
                      {tasksData.items
                        .filter((t) => t.status === 'Done' || t.status === 'Cancelled')
                        .map((task) => (
                          <TaskRow key={task.id} task={task} />
                        ))}
                    </>
                  )}
                </div>
              )}
            </div>
          )}

          {/* ── Opportunities Tab ── */}
          {activeTab === 'opportunities' && (
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <p className="text-sm text-muted-foreground">
                  {oppsData
                    ? `${oppsData.totalCount} fırsat`
                    : 'Yükleniyor...'}
                </p>
                <Button
                  variant="outline"
                  size="sm"
                  className="gap-2 h-9"
                  onClick={() => setShowCreateOpportunity(true)}
                >
                  <Plus className="h-3.5 w-3.5" />
                  Yeni Fırsat
                </Button>
              </div>

              {oppsLoading && (
                <div className="space-y-3">
                  {Array.from({ length: 3 }).map((_, i) => (
                    <Skeleton key={i} className="h-24 w-full rounded-lg" />
                  ))}
                </div>
              )}

              {!oppsLoading && (!oppsData?.items || oppsData.items.length === 0) && (
                <div className="flex flex-col items-center justify-center py-16 text-center">
                  <div className="w-16 h-16 rounded-full bg-muted flex items-center justify-center mb-4">
                    <DollarSign className="h-8 w-8 text-muted-foreground/40" />
                  </div>
                  <p className="font-medium text-foreground mb-1">Fırsat yok</p>
                  <p className="text-sm text-muted-foreground mb-4">
                    Bu müşteri için henüz satış fırsatı oluşturulmadı.
                  </p>
                  <Button
                    variant="outline"
                    className="gap-2 h-10"
                    onClick={() => setShowCreateOpportunity(true)}
                  >
                    <Plus className="h-4 w-4" />
                    Fırsat Oluştur
                  </Button>
                </div>
              )}

              {!oppsLoading && oppsData?.items && oppsData.items.length > 0 && (
                <div className="space-y-3">
                  {oppsData.items.map((opp) => (
                    <Card key={opp.id} className="border-border/60 hover:bg-muted/20 transition-colors">
                      <CardContent className="p-4">
                        <div className="flex items-start justify-between gap-3">
                          <div className="flex-1 min-w-0">
                            <div className="flex items-center gap-2 flex-wrap mb-1">
                              <span
                                className={cn(
                                  'inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-semibold',
                                  STAGE_CLASSES[opp.stage]
                                )}
                              >
                                {STAGE_LABELS[opp.stage]}
                              </span>
                              {opp.probability !== null && opp.probability !== undefined && (
                                <span className="text-xs text-muted-foreground">
                                  %{opp.probability} olasılık
                                </span>
                              )}
                            </div>
                            <p className="font-medium text-foreground leading-tight">{opp.title}</p>
                            <div className="flex items-center gap-3 mt-2 flex-wrap">
                              {opp.expectedCloseDate && (
                                <div className="flex items-center gap-1 text-xs text-muted-foreground">
                                  <Calendar className="h-3 w-3" />
                                  {formatDateShort(opp.expectedCloseDate)}
                                </div>
                              )}
                              {opp.assignedUserName && (
                                <div className="flex items-center gap-1 text-xs text-muted-foreground">
                                  <User className="h-3 w-3" />
                                  {opp.assignedUserName}
                                </div>
                              )}
                            </div>
                          </div>
                          {opp.value !== null && opp.value !== undefined && (
                            <div className="flex-shrink-0 text-right">
                              <p className="text-lg font-bold text-foreground">
                                {formatCurrency(opp.value)}
                              </p>
                            </div>
                          )}
                        </div>
                      </CardContent>
                    </Card>
                  ))}
                </div>
              )}
            </div>
          )}
          {/* ── Cari Tab ── */}
          {activeTab === 'cari' && (
            <div className="space-y-4">
              {!parasutStatus.data?.isConnected && !parasutStatus.isLoading ? (
                <div className="flex flex-col items-center justify-center py-16 text-center">
                  <div className="w-16 h-16 rounded-full bg-muted flex items-center justify-center mb-4">
                    <DollarSign className="h-8 w-8 text-muted-foreground/40" />
                  </div>
                  <p className="font-medium text-foreground mb-1">Paraşüt Bağlı Değil</p>
                  <p className="text-sm text-muted-foreground mb-4">
                    Fatura oluşturmak için önce Ayarlar'dan Paraşüt'e bağlanın.
                  </p>
                  <Button variant="outline" className="gap-2" onClick={() => navigate('/settings')}>
                    Ayarlara Git
                  </Button>
                </div>
              ) : (
                <>
                  {/* Paraşüt sync + status */}
                  <div className="flex items-center justify-between p-4 rounded-lg border border-border bg-muted/20">
                    <div>
                      <p className="text-sm font-medium text-foreground">Paraşüt Cari Eşleşmesi</p>
                      {customer?.parasutContactId ? (
                        <p className="text-xs text-emerald-400 mt-0.5">
                          Cari ID: <span className="font-mono">{customer.parasutContactId}</span>
                        </p>
                      ) : (
                        <p className="text-xs text-muted-foreground mt-0.5">
                          Paraşüt'te zaten cari varsa eşleyin, yoksa yeni kayıt gönderin.
                        </p>
                      )}
                    </div>
                    <div className="flex gap-2">
                      <Button
                        variant="ghost"
                        size="sm"
                        className="gap-1.5 text-muted-foreground"
                        disabled={!currentProjectId}
                        onClick={() => setShowLinkDialog(true)}
                        title="Paraşüt'teki mevcut cariyle eşle"
                      >
                        Mevcut Cari Eşle
                      </Button>
                      <Button
                        variant="outline"
                        size="sm"
                        className="gap-1.5"
                        disabled={syncContact.isPending || !currentProjectId}
                        onClick={async () => {
                          if (!currentProjectId) return;
                          try {
                            const res = await syncContact.mutateAsync({ projectId: currentProjectId, customerId });
                            toast({ title: 'Paraşüt\'e gönderildi', description: `Cari ID: ${res.parasutContactId}` });
                          } catch {
                            toast({ title: 'Hata', description: 'Cari gönderilemedi.', variant: 'destructive' });
                          }
                        }}
                      >
                        {syncContact.isPending
                          ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
                          : <Users className="h-3.5 w-3.5" />}
                        {customer?.parasutContactId ? 'Güncelle' : 'Paraşüt\'e Gönder'}
                      </Button>
                    </div>
                  </div>

                  {/* Quick invoice form */}
                  {showCariInvoiceForm ? (
                    <Card className="border-border/60">
                      <CardContent className="p-4 space-y-4">
                        <div className="flex items-center justify-between">
                          <p className="text-sm font-medium">Hızlı Fatura Oluştur</p>
                          <Button variant="ghost" size="sm" onClick={() => setShowCariInvoiceForm(false)}>İptal</Button>
                        </div>
                        <QuickInvoiceForm
                          projectId={currentProjectId!}
                          contactId={customer?.parasutContactId ?? ''}
                          customerName={customer?.companyName ?? ''}
                          onSuccess={() => {
                            setShowCariInvoiceForm(false);
                            toast({ title: 'Fatura oluşturuldu', description: 'Paraşüt\'e gönderildi.' });
                          }}
                          onError={() => toast({ title: 'Hata', description: 'Fatura oluşturulamadı.', variant: 'destructive' })}
                          createInvoice={createInvoice}
                        />
                      </CardContent>
                    </Card>
                  ) : (
                    <div className="flex gap-2">
                      <Button
                        variant="outline"
                        size="sm"
                        className="gap-2"
                        onClick={() => setShowCariInvoiceForm(true)}
                        disabled={!parasutStatus.data?.isConnected}
                      >
                        <Plus className="h-3.5 w-3.5" />
                        Fatura Oluştur
                      </Button>
                      <Button
                        variant="ghost"
                        size="sm"
                        className="gap-2 text-muted-foreground"
                        onClick={() => navigate('/invoices')}
                      >
                        Tüm Faturalar →
                      </Button>
                    </div>
                  )}
                </>
              )}
            </div>
          )}
        </div>
      </div>

      {/* ── Dialogs ── */}

      {/* Extend EMS Expiration Dialog */}
      <ExtendExpirationDialog
        open={showExtendDialog}
        onOpenChange={setShowExtendDialog}
        companyName={customer?.companyName ?? ''}
        currentExpiry={customer?.expirationDate ?? null}
        extendExpiration={extendExpiration}
      />

      <CustomerFormDialog
        isOpen={showEditDialog}
        onClose={() => setShowEditDialog(false)}
        customer={customer}
      />

      <AddContactHistoryDialog
        isOpen={showAddContact}
        onClose={() => setShowAddContact(false)}
        customerId={customerId}
        defaultType={addContactType}
      />

      <CreateTaskDialog
        isOpen={showCreateTask}
        onClose={() => setShowCreateTask(false)}
        customerId={customerId}
      />

      <CreateOpportunityDialog
        isOpen={showCreateOpportunity}
        onClose={() => setShowCreateOpportunity(false)}
        customerId={customerId}
      />

      {/* Link Paraşüt Contact Dialog */}
      <Dialog open={showLinkDialog} onOpenChange={setShowLinkDialog}>
        <DialogContent className="max-w-lg">
          <DialogHeader>
            <DialogTitle>Mevcut Paraşüt Carisiyle Eşle</DialogTitle>
          </DialogHeader>
          <div className="space-y-3">
            <Input
              placeholder="İsim veya VKN ile ara..."
              value={linkSearch}
              onChange={(e) => setLinkSearch(e.target.value)}
              autoFocus
            />
            {parasutContactsQuery.data && (
              <p className="text-xs text-muted-foreground">
                Toplam {parasutContactsQuery.data.totalCount} cari
                {parasutContactsQuery.data.totalPages > 1 && ` · Sayfa ${linkPage} / ${parasutContactsQuery.data.totalPages}`}
              </p>
            )}
            {parasutContactsQuery.isLoading ? (
              <div className="flex items-center justify-center py-8">
                <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
              </div>
            ) : (
              <div className="max-h-56 overflow-y-auto space-y-1">
                {(parasutContactsQuery.data?.items ?? []).map(contact => (
                  <button
                    key={contact.id}
                    className="w-full text-left px-3 py-2.5 rounded-md hover:bg-accent transition-colors flex items-center justify-between gap-2"
                    onClick={async () => {
                      if (!currentProjectId) return;
                      try {
                        await linkContact.mutateAsync({
                          projectId: currentProjectId,
                          customerId,
                          parasutContactId: contact.id,
                          parasutContactName: contact.name,
                        });
                        toast({
                          title: 'Cari eşlendi',
                          description: `${contact.name} (ID: ${contact.id}) ile bağlantı kuruldu.`,
                        });
                        setShowLinkDialog(false);
                        setLinkSearch('');
                        setLinkPage(1);
                      } catch {
                        toast({ title: 'Hata', description: 'Eşleştirme başarısız.', variant: 'destructive' });
                      }
                    }}
                    disabled={linkContact.isPending}
                  >
                    <div>
                      <p className="text-sm font-medium text-foreground">{contact.name}</p>
                      <p className="text-xs text-muted-foreground">
                        ID: {contact.id}
                        {contact.taxNumber ? ` · VKN: ${contact.taxNumber}` : ''}
                      </p>
                    </div>
                    {linkContact.isPending && <Loader2 className="h-3.5 w-3.5 animate-spin flex-shrink-0" />}
                  </button>
                ))}
                {!parasutContactsQuery.isLoading && (parasutContactsQuery.data?.items ?? []).length === 0 && (
                  <p className="text-sm text-muted-foreground text-center py-4">
                    Paraşüt'te cari bulunamadı.
                  </p>
                )}
              </div>
            )}
            {/* Pagination */}
            {(parasutContactsQuery.data?.totalPages ?? 0) > 1 && (
              <div className="flex items-center justify-between pt-1">
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setLinkPage(p => Math.max(1, p - 1))}
                  disabled={linkPage <= 1 || parasutContactsQuery.isFetching}
                >
                  ‹ Önceki
                </Button>
                <span className="text-xs text-muted-foreground">
                  {linkPage} / {parasutContactsQuery.data?.totalPages}
                </span>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setLinkPage(p => p + 1)}
                  disabled={linkPage >= (parasutContactsQuery.data?.totalPages ?? 1) || parasutContactsQuery.isFetching}
                >
                  Sonraki ›
                </Button>
              </div>
            )}
          </div>
          <DialogFooter>
            <Button variant="ghost" onClick={() => { setShowLinkDialog(false); setLinkSearch(''); setLinkPage(1); }}>
              Kapat
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

// ── InfoItem ──────────────────────────────────────────────────────────────────

interface InfoItemProps {
  icon: React.ComponentType<{ className?: string }>;
  label: string;
  value: string | null | undefined;
  href?: string;
}

function InfoItem({ icon: Icon, label, value, href }: InfoItemProps) {
  if (!value) return null;
  return (
    <div className="flex items-start gap-3 min-w-0">
      <div className="w-8 h-8 rounded-lg bg-muted flex items-center justify-center flex-shrink-0 mt-0.5">
        <Icon className="h-4 w-4 text-muted-foreground" />
      </div>
      <div className="min-w-0">
        <p className="text-xs text-muted-foreground mb-0.5">{label}</p>
        {href ? (
          <a
            href={href}
            onClick={(e) => e.stopPropagation()}
            className="text-sm font-medium text-primary hover:underline truncate block"
          >
            {value}
          </a>
        ) : (
          <p className="text-sm font-medium text-foreground truncate">{value}</p>
        )}
      </div>
    </div>
  );
}
