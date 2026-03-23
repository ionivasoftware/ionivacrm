import { useEffect } from 'react';
import { useForm, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Loader2, Phone, Mail, Calendar, MessageCircle, StickyNote, MapPin, Users } from 'lucide-react';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Textarea } from '@/components/ui/textarea';
import { useCreateContactHistory } from '@/api/customers';
import { useToast } from '@/hooks/use-toast';
import { cn } from '@/lib/utils';
import type { ContactType } from '@/types';

// ── Helpers ──────────────────────────────────────────────────────────────────

function getLocalDateTimeString(): string {
  const now = new Date();
  const y = now.getFullYear();
  const m = String(now.getMonth() + 1).padStart(2, '0');
  const d = String(now.getDate()).padStart(2, '0');
  const h = String(now.getHours()).padStart(2, '0');
  const min = String(now.getMinutes()).padStart(2, '0');
  return `${y}-${m}-${d}T${h}:${min}`;
}

// ── Types / constants ─────────────────────────────────────────────────────────

export const CONTACT_TYPE_LABELS: Record<ContactType, string> = {
  Call: 'Telefon Araması',
  Email: 'E-posta',
  Meeting: 'Toplantı',
  Note: 'Not',
  WhatsApp: 'WhatsApp',
  Visit: 'Saha Ziyareti',
};

export const CONTACT_TYPE_ICONS: Record<ContactType, React.ComponentType<{ className?: string }>> = {
  Call: Phone,
  Email: Mail,
  Meeting: Users,
  Note: StickyNote,
  WhatsApp: MessageCircle,
  Visit: MapPin,
};

const CONTACT_TYPE_COLORS: Record<ContactType, string> = {
  Call: 'text-blue-400',
  Email: 'text-violet-400',
  Meeting: 'text-green-400',
  Note: 'text-amber-400',
  WhatsApp: 'text-emerald-400',
  Visit: 'text-rose-400',
};

// ── Schema ──────────────────────────────────────────────────────────────────

const schema = z.object({
  type: z.enum(['Call', 'Email', 'Meeting', 'Note', 'WhatsApp', 'Visit'] as const),
  subject: z.string(),
  content: z.string(),
  outcome: z.string(),
  contactedAt: z.string().min(1, 'Tarih ve saat gereklidir'),
});

type FormData = z.infer<typeof schema>;

// ── Props ────────────────────────────────────────────────────────────────────

interface AddContactHistoryDialogProps {
  isOpen: boolean;
  onClose: () => void;
  customerId: string;
  defaultType?: ContactType;
  onSuccess?: () => void;
}

// ── Component ────────────────────────────────────────────────────────────────

export function AddContactHistoryDialog({
  isOpen,
  onClose,
  customerId,
  defaultType = 'Note',
  onSuccess,
}: AddContactHistoryDialogProps) {
  const { toast } = useToast();
  const mutation = useCreateContactHistory();

  const {
    register,
    handleSubmit,
    control,
    watch,
    reset,
    formState: { errors },
  } = useForm<FormData>({
    resolver: zodResolver(schema),
    defaultValues: {
      type: defaultType,
      subject: '',
      content: '',
      outcome: '',
      contactedAt: getLocalDateTimeString(),
    },
  });

  const selectedType = watch('type');

  // Reset when dialog opens with new defaultType
  useEffect(() => {
    if (isOpen) {
      reset({
        type: defaultType,
        subject: '',
        content: '',
        outcome: '',
        contactedAt: getLocalDateTimeString(),
      });
    }
  }, [isOpen, defaultType, reset]);

  const handleClose = () => {
    reset();
    onClose();
  };

  const onSubmit = async (data: FormData) => {
    try {
      await mutation.mutateAsync({
        customerId,
        type: data.type,
        subject: data.subject || undefined,
        content: data.content || undefined,
        outcome: data.outcome || undefined,
        contactedAt: new Date(data.contactedAt).toISOString(),
      });
      toast({
        title: 'Aktivite eklendi',
        description: `${CONTACT_TYPE_LABELS[data.type]} başarıyla kaydedildi.`,
      });
      onSuccess?.();
      handleClose();
    } catch {
      toast({
        title: 'Hata oluştu',
        description: 'Aktivite eklenirken bir hata oluştu.',
        variant: 'destructive',
      });
    }
  };

  const TypeIcon = CONTACT_TYPE_ICONS[selectedType] ?? StickyNote;

  return (
    <Dialog open={isOpen} onOpenChange={(open) => !open && handleClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <div className="flex items-center gap-3">
            <div className="w-9 h-9 rounded-lg bg-primary/10 flex items-center justify-center flex-shrink-0">
              <TypeIcon className={cn('h-5 w-5', CONTACT_TYPE_COLORS[selectedType])} />
            </div>
            <DialogTitle className="text-lg">Aktivite Ekle</DialogTitle>
          </div>
        </DialogHeader>

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4 py-1">
          {/* Type selector as button group */}
          <div className="space-y-1.5">
            <Label>Aktivite Türü <span className="text-destructive">*</span></Label>
            <Controller
              control={control}
              name="type"
              render={({ field }) => (
                <div className="grid grid-cols-3 gap-2">
                  {(Object.keys(CONTACT_TYPE_LABELS) as ContactType[]).map((type) => {
                    const Icon = CONTACT_TYPE_ICONS[type];
                    return (
                      <button
                        key={type}
                        type="button"
                        onClick={() => field.onChange(type)}
                        className={cn(
                          'flex flex-col items-center gap-1.5 p-3 rounded-lg border text-xs font-medium transition-all',
                          'min-h-[60px] touch-manipulation',
                          field.value === type
                            ? 'border-primary bg-primary/10 text-primary'
                            : 'border-border text-muted-foreground hover:border-muted-foreground hover:text-foreground'
                        )}
                      >
                        <Icon className="h-4 w-4" />
                        {CONTACT_TYPE_LABELS[type]}
                      </button>
                    );
                  })}
                </div>
              )}
            />
          </div>

          {/* Date & Time */}
          <div className="space-y-1.5">
            <Label htmlFor="contactedAt">
              Tarih ve Saat <span className="text-destructive">*</span>
            </Label>
            <Input
              id="contactedAt"
              type="datetime-local"
              {...register('contactedAt')}
              className={cn('h-11', errors.contactedAt && 'border-destructive')}
            />
            {errors.contactedAt && (
              <p className="text-xs text-destructive">{errors.contactedAt.message}</p>
            )}
          </div>

          {/* Subject */}
          <div className="space-y-1.5">
            <Label htmlFor="subject">Konu</Label>
            <Input
              id="subject"
              placeholder={
                selectedType === 'Call'
                  ? 'Ör: Yıllık yenileme görüşmesi'
                  : selectedType === 'Email'
                  ? 'Ör: Teklif gönderildi'
                  : selectedType === 'Meeting'
                  ? 'Ör: Demo sunumu'
                  : 'Konu başlığı'
              }
              {...register('subject')}
              className="h-11"
            />
          </div>

          {/* Content / Notes */}
          <div className="space-y-1.5">
            <Label htmlFor="content">Açıklama / Notlar</Label>
            <Textarea
              id="content"
              placeholder="Görüşme detayları, önemli noktalar..."
              rows={3}
              {...register('content')}
            />
          </div>

          {/* Outcome */}
          <div className="space-y-1.5">
            <Label htmlFor="outcome">Sonuç / Aksiyon</Label>
            <Input
              id="outcome"
              placeholder="Ör: Teklif hazırlanacak, takip araması yapılacak"
              {...register('outcome')}
              className="h-11"
            />
          </div>

          <DialogFooter className="gap-2 pt-2">
            <Button
              type="button"
              variant="outline"
              onClick={handleClose}
              disabled={mutation.isPending}
              className="h-11"
            >
              İptal
            </Button>
            <Button
              type="submit"
              disabled={mutation.isPending}
              className="h-11 min-w-[100px]"
            >
              {mutation.isPending && <Loader2 className="h-4 w-4 animate-spin mr-2" />}
              Kaydet
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
