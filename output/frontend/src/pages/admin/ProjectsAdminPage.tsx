import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Plus, Pencil, FolderOpen, Loader2, Key, Eye, EyeOff, Copy, Check, MessageSquare } from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter,
} from '@/components/ui/dialog';
import {
  useAdminProjects, useCreateProject, useUpdateProject, useSetProjectApiKeys, useAddProjectSms,
  type AdminProject,
} from '@/api/admin';
import { useToast } from '@/hooks/use-toast';

// ── Schema ────────────────────────────────────────────────────────────────────

const schema = z.object({
  name: z.string().min(1, 'Proje adı gereklidir'),
  description: z.string(),
  isActive: z.boolean(),
});
type FormData = z.infer<typeof schema>;

// ── Project Dialog ────────────────────────────────────────────────────────────

function ProjectDialog({
  project,
  onClose,
}: {
  project?: AdminProject;
  onClose: () => void;
}) {
  const { toast } = useToast();
  const createMutation = useCreateProject();
  const updateMutation = useUpdateProject();
  const isEdit = !!project;

  const { register, handleSubmit, watch, setValue, formState: { errors } } = useForm<FormData>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: project?.name ?? '',
      description: project?.description ?? '',
      isActive: project?.isActive ?? true,
    },
  });

  const onSubmit = async (data: FormData) => {
    try {
      if (isEdit && project) {
        await updateMutation.mutateAsync({ id: project.id, ...data });
        toast({ title: 'Proje güncellendi' });
      } else {
        await createMutation.mutateAsync({ name: data.name, description: data.description || undefined });
        toast({ title: 'Proje oluşturuldu', description: `${data.name} başarıyla eklendi.` });
      }
      onClose();
    } catch {
      toast({ title: 'Hata', description: 'İşlem başarısız oldu.', variant: 'destructive' });
    }
  };

  const isPending = createMutation.isPending || updateMutation.isPending;

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{isEdit ? 'Projeyi Düzenle' : 'Yeni Proje'}</DialogTitle>
        </DialogHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4 py-2">
          <div className="space-y-1.5">
            <Label>Proje Adı *</Label>
            <Input {...register('name')} placeholder="Ör: Ioniva Satış" className="h-10" />
            {errors.name && <p className="text-xs text-destructive">{errors.name.message}</p>}
          </div>
          <div className="space-y-1.5">
            <Label>Açıklama</Label>
            <Input {...register('description')} placeholder="Kısa açıklama" className="h-10" />
          </div>
          {isEdit && (
            <div className="flex items-center gap-3 p-3 rounded-lg border">
              <input
                type="checkbox"
                id="isActive"
                checked={watch('isActive')}
                onChange={(e) => setValue('isActive', e.target.checked)}
                className="w-4 h-4"
              />
              <label htmlFor="isActive" className="text-sm font-medium cursor-pointer">Aktif</label>
            </div>
          )}
          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={onClose}>İptal</Button>
            <Button type="submit" disabled={isPending}>
              {isPending && <Loader2 className="h-4 w-4 animate-spin mr-2" />}
              {isEdit ? 'Güncelle' : 'Oluştur'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ── API Key Field ─────────────────────────────────────────────────────────────

function ApiKeyField({
  label,
  value,
  onChange,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
}) {
  const [show, setShow] = useState(false);
  const [copied, setCopied] = useState(false);
  const { toast } = useToast();

  const handleCopy = async () => {
    if (!value) return;
    await navigator.clipboard.writeText(value);
    setCopied(true);
    toast({ title: 'Kopyalandı' });
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="space-y-1.5">
      <Label>{label}</Label>
      <div className="flex gap-2">
        <Input
          type={show ? 'text' : 'password'}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder="API anahtarı girin"
          className="h-10 font-mono text-xs"
        />
        <Button type="button" variant="ghost" size="icon" onClick={() => setShow((s) => !s)} className="flex-shrink-0">
          {show ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
        </Button>
        <Button type="button" variant="ghost" size="icon" onClick={handleCopy} disabled={!value} className="flex-shrink-0">
          {copied ? <Check className="h-4 w-4 text-green-500" /> : <Copy className="h-4 w-4" />}
        </Button>
      </div>
    </div>
  );
}

// ── API Keys Dialog ───────────────────────────────────────────────────────────

function ApiKeysDialog({ project, onClose }: { project: AdminProject; onClose: () => void }) {
  const { toast } = useToast();
  const mutation = useSetProjectApiKeys();
  const [emsApiKey, setEmsApiKey] = useState(project.emsApiKey ?? '');
  const [rezervAlApiKey, setRezervAlApiKey] = useState(project.rezervAlApiKey ?? '');

  const handleSave = async () => {
    try {
      await mutation.mutateAsync({
        id: project.id,
        emsApiKey: emsApiKey.trim() || null,
        rezervAlApiKey: rezervAlApiKey.trim() || null,
      });
      toast({ title: 'API anahtarları kaydedildi' });
      onClose();
    } catch {
      toast({ title: 'Hata', description: 'Kayıt başarısız oldu.', variant: 'destructive' });
    }
  };

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Key className="h-4 w-4" />
            API Anahtarları — {project.name}
          </DialogTitle>
        </DialogHeader>
        <div className="space-y-4 py-2">
          <div className="p-3 rounded-lg bg-muted/40 text-xs text-muted-foreground space-y-1">
            <p><span className="font-semibold text-foreground">EMS</span> → SaaS A — Bearer token ile kimlik doğrulama</p>
            <p><span className="font-semibold text-foreground">Rezerval</span> → SaaS B — X-Api-Key header ile kimlik doğrulama</p>
          </div>
          <ApiKeyField label="EMS API Key (SaaS A)" value={emsApiKey} onChange={setEmsApiKey} />
          <ApiKeyField label="Rezerval API Key (SaaS B)" value={rezervAlApiKey} onChange={setRezervAlApiKey} />
        </div>
        <DialogFooter className="gap-2">
          <Button type="button" variant="outline" onClick={onClose}>İptal</Button>
          <Button onClick={handleSave} disabled={mutation.isPending}>
            {mutation.isPending && <Loader2 className="h-4 w-4 animate-spin mr-2" />}
            Kaydet
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Add SMS Credits Dialog ─────────────────────────────────────────────────────

function AddSmsDialog({ project, onClose }: { project: AdminProject; onClose: () => void }) {
  const { toast } = useToast();
  const mutation = useAddProjectSms();
  const [count, setCount] = useState('');

  const countNum = parseInt(count, 10);
  const isValid = !isNaN(countNum) && countNum > 0;

  async function handleConfirm() {
    if (!isValid) return;
    try {
      const result = await mutation.mutateAsync({ id: project.id, count: countNum });
      toast({
        title: 'SMS kredisi yüklendi',
        description: `${result.added} SMS eklendi. Güncel bakiye: ${result.smsCount} SMS`,
      });
      onClose();
    } catch {
      toast({ title: 'Hata', description: 'SMS yüklenemedi.', variant: 'destructive' });
    }
  }

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="sm:max-w-sm">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <MessageSquare className="h-5 w-5 text-blue-400" />
            SMS Yükle — {project.name}
          </DialogTitle>
        </DialogHeader>
        <div className="space-y-4 py-2">
          <div className="flex items-center justify-between p-3 rounded-lg bg-muted/40">
            <span className="text-sm text-muted-foreground">Mevcut bakiye</span>
            <span className="text-sm font-semibold tabular-nums">
              {project.smsCount.toLocaleString('tr-TR')} SMS
            </span>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="sms-count">Yüklenecek SMS adedi</Label>
            <Input
              id="sms-count"
              type="number"
              min={1}
              value={count}
              onChange={(e) => setCount(e.target.value)}
              placeholder="Örn: 500"
              className="h-10"
            />
            {count && !isValid && (
              <p className="text-xs text-destructive">Geçerli bir sayı girin (min 1)</p>
            )}
          </div>
          {isValid && (
            <div className="flex items-center justify-between p-3 rounded-lg border border-blue-500/30 bg-blue-500/5">
              <span className="text-sm text-muted-foreground">Yükleme sonrası bakiye</span>
              <span className="text-sm font-semibold text-blue-400 tabular-nums">
                {(project.smsCount + countNum).toLocaleString('tr-TR')} SMS
              </span>
            </div>
          )}
        </div>
        <DialogFooter className="gap-2">
          <Button type="button" variant="outline" onClick={onClose}>İptal</Button>
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

// ── Page ──────────────────────────────────────────────────────────────────────

export function ProjectsAdminPage() {
  const { data: projects = [], isLoading } = useAdminProjects();
  const [showCreate, setShowCreate] = useState(false);
  const [editTarget, setEditTarget] = useState<AdminProject | null>(null);
  const [apiKeysTarget, setApiKeysTarget] = useState<AdminProject | null>(null);
  const [smsTarget, setSmsTarget] = useState<AdminProject | null>(null);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-foreground">Proje Yönetimi</h1>
          <p className="text-muted-foreground text-sm mt-1">
            {isLoading ? 'Yükleniyor...' : `${projects.length} proje (tenant)`}
          </p>
        </div>
        <Button className="gap-2" onClick={() => setShowCreate(true)}>
          <Plus className="h-4 w-4" /> Yeni Proje
        </Button>
      </div>

      <Card>
        <CardContent className="p-0">
          {isLoading ? (
            <div className="divide-y divide-border">
              {Array.from({ length: 3 }).map((_, i) => (
                <div key={i} className="flex items-center gap-4 p-4">
                  <Skeleton className="h-10 w-10 rounded-lg" />
                  <div className="flex-1 space-y-2">
                    <Skeleton className="h-4 w-40" />
                    <Skeleton className="h-3 w-56" />
                  </div>
                </div>
              ))}
            </div>
          ) : projects.length === 0 ? (
            <div className="flex flex-col items-center py-16 text-center">
              <FolderOpen className="h-10 w-10 text-muted-foreground/30 mb-3" />
              <p className="text-muted-foreground text-sm">Henüz proje oluşturulmamış.</p>
            </div>
          ) : (
            <div className="divide-y divide-border">
              {projects.map((project) => (
                <div key={project.id} className="flex items-center gap-4 p-4 hover:bg-muted/30 transition-colors">
                  <div className="w-10 h-10 rounded-lg bg-primary/10 flex items-center justify-center flex-shrink-0">
                    <FolderOpen className="h-5 w-5 text-primary" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2">
                      <p className="text-sm font-semibold text-foreground">{project.name}</p>
                      <Badge variant={project.isActive ? 'default' : 'secondary'} className="text-xs">
                        {project.isActive ? 'Aktif' : 'Pasif'}
                      </Badge>
                    </div>
                    {project.description && (
                      <p className="text-xs text-muted-foreground mt-0.5">{project.description}</p>
                    )}
                    <div className="flex items-center gap-3 mt-0.5">
                      <p className="text-xs text-muted-foreground">
                        ID: <span className="font-mono">{project.id}</span>
                      </p>
                      <span className={`text-xs flex items-center gap-1 ${project.emsApiKey ? 'text-green-500' : 'text-muted-foreground/50'}`}>
                        <Key className="h-3 w-3" /> EMS
                      </span>
                      <span className={`text-xs flex items-center gap-1 ${project.rezervAlApiKey ? 'text-green-500' : 'text-muted-foreground/50'}`}>
                        <Key className="h-3 w-3" /> Rezerval
                      </span>
                      <span className={`text-xs flex items-center gap-1 ${project.smsCount > 0 ? 'text-blue-400' : 'text-muted-foreground/50'}`}>
                        <MessageSquare className="h-3 w-3" />
                        {project.smsCount.toLocaleString('tr-TR')} SMS
                      </span>
                    </div>
                  </div>
                  <div className="flex items-center gap-2 flex-shrink-0">
                    <Button
                      variant="outline"
                      size="sm"
                      className="gap-1.5 border-blue-500/40 text-blue-400 hover:bg-blue-500/10"
                      onClick={() => setSmsTarget(project)}
                    >
                      <MessageSquare className="h-3.5 w-3.5" /> SMS Yükle
                    </Button>
                    <Button
                      variant="outline"
                      size="sm"
                      className="gap-1.5"
                      onClick={() => setApiKeysTarget(project)}
                    >
                      <Key className="h-3.5 w-3.5" /> API Keys
                    </Button>
                    <Button
                      variant="outline"
                      size="sm"
                      className="gap-1.5"
                      onClick={() => setEditTarget(project)}
                    >
                      <Pencil className="h-3.5 w-3.5" /> Düzenle
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      {showCreate && <ProjectDialog onClose={() => setShowCreate(false)} />}
      {editTarget && <ProjectDialog project={editTarget} onClose={() => setEditTarget(null)} />}
      {apiKeysTarget && <ApiKeysDialog project={apiKeysTarget} onClose={() => setApiKeysTarget(null)} />}
      {smsTarget && <AddSmsDialog project={smsTarget} onClose={() => setSmsTarget(null)} />}
    </div>
  );
}
