import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { User, Lock, Loader2, CheckCircle } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { useAuthStore } from '@/stores/authStore';
import { apiClient } from '@/api/client';
import type { ApiResponse } from '@/types';
import { useToast } from '@/hooks/use-toast';

// ── Profile form ──────────────────────────────────────────────────────────────

const profileSchema = z.object({
  firstName: z.string().min(1, 'Ad gereklidir'),
  lastName: z.string().min(1, 'Soyad gereklidir'),
});
type ProfileForm = z.infer<typeof profileSchema>;

// ── Password form ─────────────────────────────────────────────────────────────

const passwordSchema = z.object({
  newPassword: z.string()
    .min(8, 'En az 8 karakter')
    .regex(/[A-Z]/, 'En az bir büyük harf')
    .regex(/[a-z]/, 'En az bir küçük harf')
    .regex(/[0-9]/, 'En az bir rakam'),
  confirmPassword: z.string(),
}).refine(data => data.newPassword === data.confirmPassword, {
  message: 'Şifreler eşleşmiyor',
  path: ['confirmPassword'],
});
type PasswordForm = z.infer<typeof passwordSchema>;

// ── Page ──────────────────────────────────────────────────────────────────────

export function SettingsPage() {
  const { user } = useAuthStore();
  const { toast } = useToast();
  const [profileSaved, setProfileSaved] = useState(false);
  const [passwordSaved, setPasswordSaved] = useState(false);

  const profileForm = useForm<ProfileForm>({
    resolver: zodResolver(profileSchema),
    defaultValues: {
      firstName: user?.firstName ?? '',
      lastName: user?.lastName ?? '',
    },
  });

  const passwordForm = useForm<PasswordForm>({
    resolver: zodResolver(passwordSchema),
    defaultValues: { newPassword: '', confirmPassword: '' },
  });

  const [profileLoading, setProfileLoading] = useState(false);
  const [passwordLoading, setPasswordLoading] = useState(false);

  async function handleProfileSubmit(data: ProfileForm) {
    setProfileLoading(true);
    setProfileSaved(false);
    try {
      await apiClient.put<ApiResponse<unknown>>('/auth/profile', {
        firstName: data.firstName,
        lastName: data.lastName,
      });
      // Update stored user info
      const savedUser = localStorage.getItem('user');
      if (savedUser) {
        const u = JSON.parse(savedUser);
        u.firstName = data.firstName;
        u.lastName = data.lastName;
        localStorage.setItem('user', JSON.stringify(u));
      }
      setProfileSaved(true);
      toast({ title: 'Profil güncellendi' });
    } catch {
      toast({ title: 'Hata', description: 'Profil güncellenemedi.', variant: 'destructive' });
    } finally {
      setProfileLoading(false);
    }
  }

  async function handlePasswordSubmit(data: PasswordForm) {
    setPasswordLoading(true);
    setPasswordSaved(false);
    try {
      await apiClient.put<ApiResponse<unknown>>('/auth/profile', {
        firstName: user?.firstName ?? '',
        lastName: user?.lastName ?? '',
        newPassword: data.newPassword,
      });
      setPasswordSaved(true);
      passwordForm.reset();
      toast({ title: 'Şifre güncellendi' });
    } catch {
      toast({ title: 'Hata', description: 'Şifre güncellenemedi.', variant: 'destructive' });
    } finally {
      setPasswordLoading(false);
    }
  }

  return (
    <div className="space-y-6 max-w-2xl">
      <div>
        <h1 className="text-2xl font-bold text-foreground">Ayarlar</h1>
        <p className="text-muted-foreground text-sm mt-1">Profil bilgilerinizi ve şifrenizi yönetin.</p>
      </div>

      {/* Profile section */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <User className="h-4 w-4" /> Profil Bilgileri
          </CardTitle>
          <CardDescription>Ad, soyad ve diğer hesap bilgileriniz.</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="flex items-center gap-4 mb-6 p-4 rounded-lg bg-muted/30 border border-border">
            <div className="w-14 h-14 rounded-full bg-primary/15 flex items-center justify-center flex-shrink-0">
              <span className="text-primary text-xl font-bold">
                {user?.firstName?.[0]}{user?.lastName?.[0]}
              </span>
            </div>
            <div>
              <p className="font-semibold text-foreground">{user?.firstName} {user?.lastName}</p>
              <p className="text-sm text-muted-foreground">{user?.email}</p>
              {user?.isSuperAdmin && (
                <Badge variant="secondary" className="mt-1 text-xs">SuperAdmin</Badge>
              )}
            </div>
          </div>

          <form onSubmit={profileForm.handleSubmit(handleProfileSubmit)} className="space-y-4">
            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label>Ad *</Label>
                <Input {...profileForm.register('firstName')} className="h-10" />
                {profileForm.formState.errors.firstName && (
                  <p className="text-xs text-destructive">{profileForm.formState.errors.firstName.message}</p>
                )}
              </div>
              <div className="space-y-1.5">
                <Label>Soyad *</Label>
                <Input {...profileForm.register('lastName')} className="h-10" />
                {profileForm.formState.errors.lastName && (
                  <p className="text-xs text-destructive">{profileForm.formState.errors.lastName.message}</p>
                )}
              </div>
            </div>
            <div className="space-y-1.5">
              <Label>E-posta</Label>
              <Input value={user?.email ?? ''} disabled className="h-10 opacity-60" />
              <p className="text-xs text-muted-foreground">E-posta adresi değiştirilemez.</p>
            </div>
            <div className="flex items-center gap-2">
              <Button type="submit" disabled={profileLoading} className="gap-2">
                {profileLoading && <Loader2 className="h-4 w-4 animate-spin" />}
                Kaydet
              </Button>
              {profileSaved && (
                <span className="flex items-center gap-1 text-sm text-emerald-500">
                  <CheckCircle className="h-4 w-4" /> Kaydedildi
                </span>
              )}
            </div>
          </form>
        </CardContent>
      </Card>

      {/* Password section */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Lock className="h-4 w-4" /> Şifre Değiştir
          </CardTitle>
          <CardDescription>Hesabınızın güvenliği için düzenli olarak şifrenizi değiştirin.</CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={passwordForm.handleSubmit(handlePasswordSubmit)} className="space-y-4">
            <div className="space-y-1.5">
              <Label>Yeni Şifre *</Label>
              <Input
                type="password"
                {...passwordForm.register('newPassword')}
                className="h-10"
                placeholder="En az 8 karakter"
              />
              {passwordForm.formState.errors.newPassword && (
                <p className="text-xs text-destructive">{passwordForm.formState.errors.newPassword.message}</p>
              )}
              <p className="text-xs text-muted-foreground">En az 8 karakter, büyük/küçük harf ve rakam içermelidir.</p>
            </div>
            <div className="space-y-1.5">
              <Label>Yeni Şifre (Tekrar) *</Label>
              <Input
                type="password"
                {...passwordForm.register('confirmPassword')}
                className="h-10"
                placeholder="Şifreyi tekrar girin"
              />
              {passwordForm.formState.errors.confirmPassword && (
                <p className="text-xs text-destructive">{passwordForm.formState.errors.confirmPassword.message}</p>
              )}
            </div>
            <div className="flex items-center gap-2">
              <Button type="submit" disabled={passwordLoading} variant="outline" className="gap-2">
                {passwordLoading && <Loader2 className="h-4 w-4 animate-spin" />}
                Şifreyi Güncelle
              </Button>
              {passwordSaved && (
                <span className="flex items-center gap-1 text-sm text-emerald-500">
                  <CheckCircle className="h-4 w-4" /> Güncellendi
                </span>
              )}
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
