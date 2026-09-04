import { useNavigate } from 'react-router-dom';
import { ShieldCheck, ShieldAlert, ExternalLink, Loader2 } from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import {
  useBackupStatus,
  formatBytes,
  formatHoursAgo,
} from '@/api/backups';

/**
 * "Liftdesk Yedekleme" pano kartı (docs/crm-backup-api.md §7.1).
 *
 * Operatörün bakacağı tek alan isHealthy. Kart yalnız SuperAdmin'e gösterilir çünkü uç
 * SuperAdmin korumalı ve veri tüm kurulumu kapsıyor (firma bazlı yedek YOKTUR).
 *
 * SESSİZLİK BAŞARI DEĞİLDİR: veri gelmemesi "gösterme" demek değil — kaynak kayıt yoksa
 * isHealthy=false döner ve asıl yakalanması gereken hâl budur. Bu yüzden hata/boş durumda
 * kart gizlenmez, uyarı olarak gösterilir.
 */
export function BackupStatusCard({ enabled }: { enabled: boolean }) {
  const navigate = useNavigate();
  const { data, isLoading, isError, error } = useBackupStatus(enabled);

  if (!enabled) return null;

  if (isLoading) {
    return (
      <Card>
        <CardContent className="p-6 flex items-center gap-3 text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" />
          <span className="text-sm">Yedek durumu yükleniyor…</span>
        </CardContent>
      </Card>
    );
  }

  // Uç erişilemediğinde de sessiz kalınmaz — yedeklerin durumunu BİLMİYOR olmak da bir uyarıdır.
  if (isError || !data) {
    const msg =
      (error as { response?: { data?: { errors?: string[]; message?: string } } })?.response?.data
        ?.errors?.[0] ??
      (error as { response?: { data?: { message?: string } } })?.response?.data?.message ??
      'Yedek durumu alınamadı.';
    return (
      <Card className="border-amber-500/40">
        <CardContent className="p-6 flex items-start gap-3">
          <ShieldAlert className="h-5 w-5 text-amber-500 shrink-0 mt-0.5" />
          <div className="min-w-0">
            <p className="font-semibold text-foreground">Liftdesk Yedekleme</p>
            <p className="text-sm text-amber-600 dark:text-amber-500 mt-0.5">{msg}</p>
          </div>
        </CardContent>
      </Card>
    );
  }

  const healthy = data.isHealthy;
  const verified = data.lastSuccessfulVerify != null;
  const runUrl = data.lastBackup?.runUrl ?? null;

  // Yeşil özet satırı: "6 saat önce · 1.24 GB · doğrulandı"
  const summary = [
    formatHoursAgo(data.hoursSinceLastSuccessfulBackup),
    formatBytes(data.latestBackupSizeBytes),
    verified ? 'doğrulandı' : 'doğrulanmadı',
  ].join(' · ');

  return (
    <Card className={healthy ? 'border-emerald-500/40' : 'border-red-500/50'}>
      <CardContent className="p-6">
        <div className="flex items-start gap-3">
          {healthy ? (
            <ShieldCheck className="h-5 w-5 text-emerald-500 shrink-0 mt-0.5" />
          ) : (
            <ShieldAlert className="h-5 w-5 text-red-500 shrink-0 mt-0.5" />
          )}

          <div className="min-w-0 flex-1">
            <div className="flex items-center justify-between gap-3 flex-wrap">
              <p className="font-semibold text-foreground">Liftdesk Yedekleme</p>
              <span
                className={`text-xs font-semibold rounded-full border px-2 py-0.5 ${
                  healthy
                    ? 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border-emerald-500/30'
                    : 'bg-red-500/10 text-red-600 dark:text-red-400 border-red-500/30'
                }`}
              >
                {healthy ? 'Sağlıklı' : 'Sorunlu'}
              </span>
            </div>

            {healthy ? (
              <p className="text-sm text-muted-foreground mt-1">Son yedek: {summary}</p>
            ) : (
              <ul className="mt-1.5 space-y-1">
                {(data.problems ?? ['Yedekleme sağlıksız (ayrıntı bildirilmedi).']).map((p, i) => (
                  <li key={i} className="text-sm text-red-600 dark:text-red-400 flex gap-1.5">
                    <span aria-hidden>•</span>
                    <span className="min-w-0">{p}</span>
                  </li>
                ))}
              </ul>
            )}

            <div className="flex items-center gap-4 mt-3 text-xs">
              <button
                onClick={() => navigate('/admin/backups')}
                className="text-primary hover:underline font-medium"
              >
                Koşu geçmişi
              </button>
              {runUrl && (
                <a
                  href={runUrl}
                  target="_blank"
                  rel="noreferrer"
                  className="text-muted-foreground hover:text-foreground inline-flex items-center gap-1"
                >
                  Logu aç <ExternalLink className="h-3 w-3" />
                </a>
              )}
              {data.failuresLast7Days > 0 && (
                <span className="text-muted-foreground">
                  Son 7 günde {data.failuresLast7Days} hata
                </span>
              )}
            </div>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
