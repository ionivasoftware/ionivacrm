import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useNavigate, useLocation, Navigate } from 'react-router-dom';
import { Eye, EyeOff, Loader2, AlertCircle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { useAuthStore } from '@/stores/authStore';
import { cn } from '@/lib/utils';

const loginSchema = z.object({
  email: z
    .string()
    .min(1, 'E-posta adresi gereklidir')
    .email('Geçerli bir e-posta adresi giriniz'),
  password: z
    .string()
    .min(1, 'Şifre gereklidir')
    .min(6, 'Şifre en az 6 karakter olmalıdır'),
  rememberMe: z.boolean(),
});

type LoginFormData = z.infer<typeof loginSchema>;

export function LoginPage() {
  const [showPassword, setShowPassword] = useState(false);
  const [loginError, setLoginError] = useState<string | null>(null);
  const { login, isAuthenticated, isLoading: authLoading } = useAuthStore();
  const navigate = useNavigate();
  const location = useLocation();

  const from = (location.state as { from?: { pathname: string } })?.from?.pathname ?? '/dashboard';

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginFormData>({
    resolver: zodResolver(loginSchema),
    defaultValues: {
      email: '',
      password: '',
      rememberMe: false,
    },
  });

  // Already authenticated — redirect
  if (!authLoading && isAuthenticated) {
    return <Navigate to={from} replace />;
  }

  async function onSubmit(data: LoginFormData) {
    setLoginError(null);
    try {
      await login(data);
      navigate(from, { replace: true });
    } catch (error: unknown) {
      if (
        error &&
        typeof error === 'object' &&
        'response' in error &&
        error.response &&
        typeof error.response === 'object' &&
        'data' in error.response
      ) {
        const responseData = error.response.data as { message?: string };
        setLoginError(responseData.message ?? 'Giriş başarısız. Lütfen tekrar deneyiniz.');
      } else {
        setLoginError('Sunucuya bağlanılamadı. Lütfen tekrar deneyiniz.');
      }
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-4">
      {/* Background decoration */}
      <div className="absolute inset-0 overflow-hidden pointer-events-none">
        <div className="absolute -top-40 -right-40 w-80 h-80 bg-primary/5 rounded-full blur-3xl" />
        <div className="absolute -bottom-40 -left-40 w-80 h-80 bg-primary/5 rounded-full blur-3xl" />
      </div>

      <div className="w-full max-w-md relative">
        {/* Logo */}
        <div className="flex justify-center mb-8">
          <div className="flex items-center gap-3">
            <div className="w-12 h-12 rounded-xl bg-primary flex items-center justify-center shadow-lg shadow-primary/25">
              <span className="text-white font-bold text-lg">ION</span>
            </div>
            <div>
              <h1 className="text-2xl font-bold text-foreground">ION CRM</h1>
              <p className="text-xs text-muted-foreground">Müşteri İlişkileri Yönetimi</p>
            </div>
          </div>
        </div>

        <Card className="border-border shadow-xl">
          <CardHeader className="space-y-1 pb-6">
            <CardTitle className="text-xl font-semibold text-center">
              Hesabınıza Giriş Yapın
            </CardTitle>
            <CardDescription className="text-center text-muted-foreground">
              Devam etmek için bilgilerinizi giriniz
            </CardDescription>
          </CardHeader>
          <CardContent>
            <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
              {/* Error alert */}
              {loginError && (
                <div className="flex items-center gap-3 p-3 rounded-lg bg-destructive/10 border border-destructive/20 text-destructive">
                  <AlertCircle className="h-4 w-4 flex-shrink-0" />
                  <p className="text-sm">{loginError}</p>
                </div>
              )}

              {/* Email field */}
              <div className="space-y-2">
                <Label htmlFor="email" className="text-sm font-medium">
                  E-posta Adresi
                </Label>
                <Input
                  id="email"
                  type="email"
                  placeholder="ornek@sirket.com"
                  autoComplete="email"
                  autoFocus
                  {...register('email')}
                  className={cn(
                    'h-11',
                    errors.email && 'border-destructive focus-visible:ring-destructive'
                  )}
                />
                {errors.email && (
                  <p className="text-xs text-destructive">{errors.email.message}</p>
                )}
              </div>

              {/* Password field */}
              <div className="space-y-2">
                <Label htmlFor="password" className="text-sm font-medium">
                  Şifre
                </Label>
                <div className="relative">
                  <Input
                    id="password"
                    type={showPassword ? 'text' : 'password'}
                    placeholder="••••••••"
                    autoComplete="current-password"
                    {...register('password')}
                    className={cn(
                      'h-11 pr-11',
                      errors.password && 'border-destructive focus-visible:ring-destructive'
                    )}
                  />
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    className="absolute right-0 top-0 h-11 w-11 text-muted-foreground hover:text-foreground"
                    onClick={() => setShowPassword((prev) => !prev)}
                    tabIndex={-1}
                    aria-label={showPassword ? 'Şifreyi gizle' : 'Şifreyi göster'}
                  >
                    {showPassword ? (
                      <EyeOff className="h-4 w-4" />
                    ) : (
                      <Eye className="h-4 w-4" />
                    )}
                  </Button>
                </div>
                {errors.password && (
                  <p className="text-xs text-destructive">{errors.password.message}</p>
                )}
              </div>

              {/* Remember me */}
              <div className="flex items-center gap-2">
                <input
                  id="rememberMe"
                  type="checkbox"
                  className="h-4 w-4 rounded border-border text-primary focus:ring-primary cursor-pointer"
                  {...register('rememberMe')}
                />
                <Label
                  htmlFor="rememberMe"
                  className="text-sm text-muted-foreground cursor-pointer select-none"
                >
                  Beni hatırla (7 gün)
                </Label>
              </div>

              {/* Submit button */}
              <Button
                type="submit"
                className="w-full h-11 text-sm font-medium"
                disabled={isSubmitting}
              >
                {isSubmitting ? (
                  <>
                    <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                    Giriş yapılıyor...
                  </>
                ) : (
                  'Giriş Yap'
                )}
              </Button>
            </form>

            {/* SECURITY: Demo credential hints removed — never expose credentials in UI */}
          </CardContent>
        </Card>

        <p className="text-center text-xs text-muted-foreground mt-6">
          © {new Date().getFullYear()} ION CRM. Tüm hakları saklıdır.
        </p>
      </div>
    </div>
  );
}
