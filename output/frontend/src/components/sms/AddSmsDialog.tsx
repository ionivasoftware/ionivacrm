import { useState } from 'react';
import { Loader2, MessageSquare } from 'lucide-react';
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
import { useToast } from '@/hooks/use-toast';
import { useAddProjectSms } from '@/api/admin';

interface AddSmsDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  projectId: string;
  projectName: string;
  currentSmsCount: number;
}

export function AddSmsDialog({
  open,
  onOpenChange,
  projectId,
  projectName,
  currentSmsCount,
}: AddSmsDialogProps) {
  const { toast } = useToast();
  const mutation = useAddProjectSms();
  const [count, setCount] = useState('');

  const countNum = parseInt(count, 10);
  const isValid = !isNaN(countNum) && countNum > 0;

  function handleClose() {
    onOpenChange(false);
    setCount('');
  }

  async function handleConfirm() {
    if (!isValid) return;
    try {
      const result = await mutation.mutateAsync({ id: projectId, count: countNum });
      toast({
        title: 'SMS kredisi yüklendi',
        description: `${result.added} SMS eklendi. Güncel bakiye: ${result.smsCount} SMS`,
      });
      handleClose();
    } catch {
      toast({ title: 'Hata', description: 'SMS yüklenemedi.', variant: 'destructive' });
    }
  }

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v) handleClose(); else onOpenChange(true); }}>
      <DialogContent className="sm:max-w-sm">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <MessageSquare className="h-5 w-5 text-blue-400" />
            SMS Yükle
          </DialogTitle>
        </DialogHeader>
        <div className="space-y-4 py-2">
          <p className="text-sm text-muted-foreground">
            <span className="font-medium text-foreground">{projectName}</span> projesine SMS kredisi yükleyin.
          </p>
          <div className="flex items-center justify-between p-3 rounded-lg bg-muted/40">
            <span className="text-sm text-muted-foreground">Mevcut bakiye</span>
            <span className="text-sm font-semibold tabular-nums">
              {currentSmsCount.toLocaleString('tr-TR')} SMS
            </span>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="sms-count-input">Yüklenecek SMS adedi</Label>
            <Input
              id="sms-count-input"
              type="number"
              min={1}
              value={count}
              onChange={(e) => setCount(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter' && isValid) handleConfirm(); }}
              placeholder="Örn: 500"
              className="h-10"
              autoFocus
            />
            {count && !isValid && (
              <p className="text-xs text-destructive">Geçerli bir sayı girin (min 1)</p>
            )}
          </div>
          {isValid && (
            <div className="flex items-center justify-between p-3 rounded-lg border border-blue-500/30 bg-blue-500/5">
              <span className="text-sm text-muted-foreground">Yükleme sonrası bakiye</span>
              <span className="text-sm font-semibold text-blue-400 tabular-nums">
                {(currentSmsCount + countNum).toLocaleString('tr-TR')} SMS
              </span>
            </div>
          )}
        </div>
        <DialogFooter className="gap-2">
          <Button type="button" variant="outline" onClick={handleClose}>
            İptal
          </Button>
          <Button
            onClick={handleConfirm}
            disabled={!isValid || mutation.isPending}
            className="bg-blue-600 hover:bg-blue-700 text-white"
          >
            {mutation.isPending ? (
              <><Loader2 className="h-4 w-4 mr-1.5 animate-spin" />Yükleniyor...</>
            ) : (
              <><MessageSquare className="h-4 w-4 mr-1.5" />Yükle</>
            )}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
