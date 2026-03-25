import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Plus, Pencil, FolderOpen, Loader2 } from 'lucide-react';
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
  useAdminProjects, useCreateProject, useUpdateProject,
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

// ── Page ──────────────────────────────────────────────────────────────────────

export function ProjectsAdminPage() {
  const { data: projects = [], isLoading } = useAdminProjects();
  const [showCreate, setShowCreate] = useState(false);
  const [editTarget, setEditTarget] = useState<AdminProject | null>(null);

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
                    <p className="text-xs text-muted-foreground mt-0.5">
                      ID: <span className="font-mono">{project.id}</span>
                    </p>
                  </div>
                  <Button
                    variant="outline"
                    size="sm"
                    className="flex-shrink-0 gap-1.5"
                    onClick={() => setEditTarget(project)}
                  >
                    <Pencil className="h-3.5 w-3.5" /> Düzenle
                  </Button>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      {showCreate && <ProjectDialog onClose={() => setShowCreate(false)} />}
      {editTarget && <ProjectDialog project={editTarget} onClose={() => setEditTarget(null)} />}
    </div>
  );
}
