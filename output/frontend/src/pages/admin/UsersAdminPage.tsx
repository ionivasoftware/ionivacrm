import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { Plus, UserCheck, Shield, Mail, Loader2, Users, Pencil, Trash2 } from 'lucide-react';
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
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from '@/components/ui/select';
import {
  useAdminUsers, useCreateUser, useUpdateUser, useDeleteUser, useAssignRole,
  type AdminUser,
} from '@/api/admin';
import { useAdminProjects } from '@/api/admin';
import { useToast } from '@/hooks/use-toast';

// ── Create User Dialog ─────────────────────────────────────────────────────────

const createSchema = z.object({
  firstName: z.string().min(1, 'Ad gereklidir'),
  lastName: z.string().min(1, 'Soyad gereklidir'),
  email: z.string().email('Geçerli e-posta giriniz'),
  password: z.string()
    .min(8, 'En az 8 karakter')
    .regex(/[A-Z]/, 'En az bir büyük harf içermelidir')
    .regex(/[a-z]/, 'En az bir küçük harf içermelidir')
    .regex(/[0-9]/, 'En az bir rakam içermelidir'),
  isSuperAdmin: z.boolean(),
});
type CreateForm = z.infer<typeof createSchema>;

function CreateUserDialog({ onClose }: { onClose: () => void }) {
  const { toast } = useToast();
  const mutation = useCreateUser();
  const { register, handleSubmit, setValue, watch, formState: { errors } } = useForm<CreateForm>({
    resolver: zodResolver(createSchema),
    defaultValues: { isSuperAdmin: false },
  });

  const onSubmit = async (data: CreateForm) => {
    try {
      await mutation.mutateAsync(data);
      toast({ title: 'Kullanıcı oluşturuldu', description: `${data.email} başarıyla eklendi.` });
      onClose();
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { errors?: string[] } } })?.response?.data?.errors?.[0]
        ?? 'Kullanıcı oluşturulamadı.';
      toast({ title: 'Hata', description: msg, variant: 'destructive' });
    }
  };

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader><DialogTitle>Yeni Kullanıcı</DialogTitle></DialogHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4 py-2">
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label>Ad *</Label>
              <Input {...register('firstName')} className="h-10" />
              {errors.firstName && <p className="text-xs text-destructive">{errors.firstName.message}</p>}
            </div>
            <div className="space-y-1.5">
              <Label>Soyad *</Label>
              <Input {...register('lastName')} className="h-10" />
              {errors.lastName && <p className="text-xs text-destructive">{errors.lastName.message}</p>}
            </div>
          </div>
          <div className="space-y-1.5">
            <Label>E-posta *</Label>
            <Input type="email" {...register('email')} className="h-10" />
            {errors.email && <p className="text-xs text-destructive">{errors.email.message}</p>}
          </div>
          <div className="space-y-1.5">
            <Label>Şifre *</Label>
            <Input type="password" {...register('password')} className="h-10" />
            {errors.password && <p className="text-xs text-destructive">{errors.password.message}</p>}
            <p className="text-xs text-muted-foreground">En az 8 karakter, büyük/küçük harf ve rakam içermelidir.</p>
          </div>
          <div className="flex items-center gap-3 p-3 rounded-lg border">
            <input
              type="checkbox"
              id="isSuperAdmin"
              checked={watch('isSuperAdmin')}
              onChange={(e) => setValue('isSuperAdmin', e.target.checked)}
              className="w-4 h-4"
            />
            <div>
              <label htmlFor="isSuperAdmin" className="text-sm font-medium cursor-pointer">SuperAdmin</label>
              <p className="text-xs text-muted-foreground">Tüm projelere tam erişim</p>
            </div>
          </div>
          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={onClose}>İptal</Button>
            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending && <Loader2 className="h-4 w-4 animate-spin mr-2" />}
              Oluştur
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ── Edit User Dialog ───────────────────────────────────────────────────────────

const editSchema = z.object({
  firstName: z.string().min(1, 'Ad gereklidir'),
  lastName: z.string().min(1, 'Soyad gereklidir'),
  isActive: z.boolean(),
  isSuperAdmin: z.boolean(),
});
type EditForm = z.infer<typeof editSchema>;

function EditUserDialog({ user, onClose }: { user: AdminUser; onClose: () => void }) {
  const { toast } = useToast();
  const mutation = useUpdateUser();
  const { register, handleSubmit, setValue, watch, formState: { errors } } = useForm<EditForm>({
    resolver: zodResolver(editSchema),
    defaultValues: {
      firstName: user.firstName,
      lastName: user.lastName,
      isActive: user.isActive,
      isSuperAdmin: user.isSuperAdmin,
    },
  });

  const onSubmit = async (data: EditForm) => {
    try {
      await mutation.mutateAsync({ id: user.id, ...data });
      toast({ title: 'Kullanıcı güncellendi' });
      onClose();
    } catch {
      toast({ title: 'Hata', description: 'Güncelleme başarısız.', variant: 'destructive' });
    }
  };

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader><DialogTitle>Kullanıcıyı Düzenle</DialogTitle></DialogHeader>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4 py-2">
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label>Ad *</Label>
              <Input {...register('firstName')} className="h-10" />
              {errors.firstName && <p className="text-xs text-destructive">{errors.firstName.message}</p>}
            </div>
            <div className="space-y-1.5">
              <Label>Soyad *</Label>
              <Input {...register('lastName')} className="h-10" />
              {errors.lastName && <p className="text-xs text-destructive">{errors.lastName.message}</p>}
            </div>
          </div>
          <div className="space-y-2">
            <div className="flex items-center gap-3 p-3 rounded-lg border">
              <input
                type="checkbox"
                id="editIsActive"
                checked={watch('isActive')}
                onChange={(e) => setValue('isActive', e.target.checked)}
                className="w-4 h-4"
              />
              <label htmlFor="editIsActive" className="text-sm font-medium cursor-pointer">Aktif</label>
            </div>
            <div className="flex items-center gap-3 p-3 rounded-lg border">
              <input
                type="checkbox"
                id="editIsSuperAdmin"
                checked={watch('isSuperAdmin')}
                onChange={(e) => setValue('isSuperAdmin', e.target.checked)}
                className="w-4 h-4"
              />
              <div>
                <label htmlFor="editIsSuperAdmin" className="text-sm font-medium cursor-pointer">SuperAdmin</label>
                <p className="text-xs text-muted-foreground">Tüm projelere tam erişim</p>
              </div>
            </div>
          </div>
          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={onClose}>İptal</Button>
            <Button type="submit" disabled={mutation.isPending}>
              {mutation.isPending && <Loader2 className="h-4 w-4 animate-spin mr-2" />}
              Güncelle
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ── Delete Confirm Dialog ──────────────────────────────────────────────────────

function DeleteUserDialog({ user, onClose }: { user: AdminUser; onClose: () => void }) {
  const { toast } = useToast();
  const mutation = useDeleteUser();

  const handleDelete = async () => {
    try {
      await mutation.mutateAsync(user.id);
      toast({ title: 'Kullanıcı silindi' });
      onClose();
    } catch {
      toast({ title: 'Hata', description: 'Kullanıcı silinemedi.', variant: 'destructive' });
    }
  };

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="sm:max-w-sm">
        <DialogHeader>
          <DialogTitle>Kullanıcıyı Sil</DialogTitle>
        </DialogHeader>
        <p className="text-sm text-muted-foreground py-2">
          <span className="font-semibold text-foreground">{user.fullName}</span> kullanıcısını silmek istediğinizden emin misiniz? Bu işlem geri alınamaz.
        </p>
        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose}>İptal</Button>
          <Button variant="destructive" disabled={mutation.isPending} onClick={handleDelete}>
            {mutation.isPending && <Loader2 className="h-4 w-4 animate-spin mr-2" />}
            Sil
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Assign Role Dialog ────────────────────────────────────────────────────────

function AssignRoleDialog({ user, onClose }: { user: AdminUser; onClose: () => void }) {
  const { toast } = useToast();
  const { data: projects = [] } = useAdminProjects();
  const mutation = useAssignRole();
  const [projectId, setProjectId] = useState('');
  const [role, setRole] = useState('');

  const handleSubmit = async () => {
    if (!projectId || !role) return;
    try {
      await mutation.mutateAsync({ userId: user.id, projectId, role });
      toast({ title: 'Rol atandı', description: `${user.fullName} kullanıcısına rol atandı.` });
      onClose();
    } catch {
      toast({ title: 'Hata', description: 'Rol atanamadı.', variant: 'destructive' });
    }
  };

  return (
    <Dialog open onOpenChange={(o) => !o && onClose()}>
      <DialogContent className="sm:max-w-sm">
        <DialogHeader>
          <DialogTitle>Rol Ata — {user.fullName}</DialogTitle>
        </DialogHeader>
        <div className="space-y-4 py-2">
          <div className="space-y-1.5">
            <Label>Proje</Label>
            <Select value={projectId} onValueChange={setProjectId}>
              <SelectTrigger className="h-10"><SelectValue placeholder="Proje seçin" /></SelectTrigger>
              <SelectContent>
                {projects.map((p) => (
                  <SelectItem key={p.id} value={p.id}>{p.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1.5">
            <Label>Rol</Label>
            <Select value={role} onValueChange={setRole}>
              <SelectTrigger className="h-10"><SelectValue placeholder="Rol seçin" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="ProjectAdmin">Proje Yöneticisi</SelectItem>
                <SelectItem value="SalesManager">Satış Müdürü</SelectItem>
                <SelectItem value="SalesRep">Satış Temsilcisi</SelectItem>
                <SelectItem value="Accounting">Muhasebe</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>
        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose}>İptal</Button>
          <Button disabled={!projectId || !role || mutation.isPending} onClick={handleSubmit}>
            {mutation.isPending && <Loader2 className="h-4 w-4 animate-spin mr-2" />}
            Ata
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Role label ────────────────────────────────────────────────────────────────

const ROLE_LABELS: Record<string, string> = {
  ProjectAdmin: 'Proje Yöneticisi',
  SalesManager: 'Satış Müdürü',
  SalesRep: 'Satış Temsilcisi',
  Accounting: 'Muhasebe',
};

// ── Page ──────────────────────────────────────────────────────────────────────

export function UsersAdminPage() {
  const { data: users = [], isLoading } = useAdminUsers();
  const [showCreate, setShowCreate] = useState(false);
  const [editTarget, setEditTarget] = useState<AdminUser | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<AdminUser | null>(null);
  const [assignTarget, setAssignTarget] = useState<AdminUser | null>(null);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-foreground">Kullanıcı Yönetimi</h1>
          <p className="text-muted-foreground text-sm mt-1">
            {isLoading ? 'Yükleniyor...' : `${users.length} kullanıcı`}
          </p>
        </div>
        <Button className="gap-2" onClick={() => setShowCreate(true)}>
          <Plus className="h-4 w-4" /> Yeni Kullanıcı
        </Button>
      </div>

      <Card>
        <CardContent className="p-0">
          {isLoading ? (
            <div className="divide-y divide-border">
              {Array.from({ length: 5 }).map((_, i) => (
                <div key={i} className="flex items-center gap-4 p-4">
                  <Skeleton className="h-10 w-10 rounded-full" />
                  <div className="flex-1 space-y-2">
                    <Skeleton className="h-4 w-40" />
                    <Skeleton className="h-3 w-56" />
                  </div>
                </div>
              ))}
            </div>
          ) : users.length === 0 ? (
            <div className="flex flex-col items-center py-16 text-center">
              <Users className="h-10 w-10 text-muted-foreground/30 mb-3" />
              <p className="text-muted-foreground text-sm">Kullanıcı bulunamadı.</p>
            </div>
          ) : (
            <div className="divide-y divide-border">
              {users.map((user) => (
                <div key={user.id} className="flex items-start gap-4 p-4 hover:bg-muted/30 transition-colors">
                  <div className="w-10 h-10 rounded-full bg-primary/10 flex items-center justify-center flex-shrink-0 text-primary font-semibold text-sm">
                    {user.firstName[0]}{user.lastName[0]}
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 flex-wrap">
                      <p className="text-sm font-semibold text-foreground">{user.fullName}</p>
                      {user.isSuperAdmin && (
                        <Badge variant="destructive" className="text-xs gap-1">
                          <Shield className="h-3 w-3" /> SuperAdmin
                        </Badge>
                      )}
                      {!user.isActive && (
                        <Badge variant="secondary" className="text-xs">Pasif</Badge>
                      )}
                    </div>
                    <p className="text-xs text-muted-foreground flex items-center gap-1 mt-0.5">
                      <Mail className="h-3 w-3" /> {user.email}
                    </p>
                    {user.projectRoles.length > 0 && (
                      <div className="flex flex-wrap gap-1.5 mt-2">
                        {user.projectRoles.map((r) => (
                          <Badge key={r.projectId} variant="outline" className="text-xs">
                            {r.projectName} · {ROLE_LABELS[r.role] ?? r.role}
                          </Badge>
                        ))}
                      </div>
                    )}
                  </div>
                  <div className="flex gap-1.5 flex-shrink-0">
                    <Button
                      variant="outline"
                      size="sm"
                      className="gap-1.5"
                      onClick={() => setAssignTarget(user)}
                    >
                      <UserCheck className="h-3.5 w-3.5" /> Rol Ata
                    </Button>
                    <Button
                      variant="outline"
                      size="sm"
                      className="gap-1.5"
                      onClick={() => setEditTarget(user)}
                    >
                      <Pencil className="h-3.5 w-3.5" />
                    </Button>
                    <Button
                      variant="outline"
                      size="sm"
                      className="gap-1.5 text-destructive hover:text-destructive"
                      onClick={() => setDeleteTarget(user)}
                    >
                      <Trash2 className="h-3.5 w-3.5" />
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      {showCreate && <CreateUserDialog onClose={() => setShowCreate(false)} />}
      {editTarget && <EditUserDialog user={editTarget} onClose={() => setEditTarget(null)} />}
      {deleteTarget && <DeleteUserDialog user={deleteTarget} onClose={() => setDeleteTarget(null)} />}
      {assignTarget && <AssignRoleDialog user={assignTarget} onClose={() => setAssignTarget(null)} />}
    </div>
  );
}
