import { useState } from 'react';
import { ExternalLink, AlertTriangle, Loader2, ShieldCheck, ShieldAlert } from 'lucide-react';
import { Card, CardContent } from '@/components/ui/card';
import {
  useBackupRuns,
  useBackupStatus,
  useBackupHealthEvents,
  useInfraUsage,
  formatBytes,
  formatUtc,
  formatDuration,
  formatUsd,
  type BackupRun,
} from '@/api/backups';

const KINDS = [
  { value: '', label: 'Tümü' },
  { value: 'Backup', label: 'Yedekleme' },
  { value: 'Verify', label: 'Doğrulama' },
  { value: 'Mirror', label: 'Ayna' },
] as const;

const KIND_TR: Record<string, string> = {
  Backup: 'Yedekleme',
  Verify: 'Doğrulama',
  Mirror: 'Ayna',
};

const STATUS_TR: Record<string, string> = {
  Running: 'Çalışıyor',
  Succeeded: 'Başarılı',
  Failed: 'Başarısız',
};

function StatusBadge({ status }: { status: string }) {
  const cls =
    status === 'Succeeded'
      ? 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border-emerald-500/30'
      : status === 'Failed'
      ? 'bg-red-500/10 text-red-600 dark:text-red-400 border-red-500/30'
      : 'bg-blue-500/10 text-blue-500 border-blue-500/30';
  return (
    <span className={`inline-flex rounded-full border px-2 py-0.5 text-xs font-semibold ${cls}`}>
      {STATUS_TR[status] ?? status}
    </span>
  );
}

/**
 * Doğrulama gücü rozeti — bu ekranın asıl bilgisi.
 *
 * full + countsMatched=true → yedeğin GERÇEKTEN geri yüklenebildiği kanıtlanmış.
 * schema → yalnız şema doğrulanmış; veri geri geliyor mu bilinmiyor (zayıf hâl).
 * countsMatched=false → geri yükleme oldu ama VERİ EKSİK; en kötü hâl.
 */
function VerifyBadge({ run }: { run: BackupRun }) {
  if (run.kind !== 'Verify') return <span className="text-muted-foreground">—</span>;
  if (run.verifyMode == null && run.countsMatched == null)
    return <span className="text-muted-foreground">—</span>;

  const full = run.verifyMode === 'full';
  const matched = run.countsMatched === true;
  const strong = full && matched;
  const dataMissing = run.countsMatched === false;

  const cls = strong
    ? 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border-emerald-500/30'
    : dataMissing
    ? 'bg-red-500/10 text-red-600 dark:text-red-400 border-red-500/30'
    : 'bg-amber-500/10 text-amber-600 dark:text-amber-500 border-amber-500/30';

  const label = dataMissing
    ? 'veri eksik'
    : strong
    ? 'tam · sayımlar tuttu'
    : full
    ? 'tam · sayım doğrulanmadı'
    : 'yalnız şema';

  const title = dataMissing
    ? 'Geri yükleme yapıldı ama satır sayımları künyeyle tutmadı — veri eksik.'
    : strong
    ? 'Yedek gerçekten geri yüklenebiliyor: veri dahil geri yüklendi ve sayımlar tuttu.'
    : full
    ? 'Veri dahil geri yüklendi ancak sayım eşleşmesi bildirilmedi.'
    : 'Yalnız şema doğrulandı — verinin geri gelip gelmediği kanıtlanmadı (zayıf hâl).';

  return (
    <span className={`inline-flex rounded-full border px-2 py-0.5 text-xs font-medium ${cls}`} title={title}>
      {label}
    </span>
  );
}

/**
 * Altyapı maliyeti (sözleşme §7.4). Yedek ekranının ALT bölümünde durur.
 *
 * İki kural sözleşmeden geliyor ve bilerek uygulanmıştır:
 *  - Tutarlar TAHMİNDİR (Railway'in yayınlanmış oranlarıyla hesaplanır) → başlıkta açıkça yazar.
 *  - configured=false HATA DEĞİLDİR (token yok / API'ye ulaşılamadı) → kırmızı alarm değil,
 *    nötr bir bilgi satırı gösterilir.
 */
function InfraUsageSection() {
  const [days, setDays] = useState<number | null>(null);
  const { data, isLoading, isError } = useInfraUsage(days, true);

  const RANGES = [
    { value: null, label: 'Ay başından' },
    { value: 7, label: 'Son 7 gün' },
    { value: 30, label: 'Son 30 gün' },
  ] as const;

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <div>
          <h2 className="text-sm font-semibold text-foreground">Altyapı maliyeti</h2>
          <p className="text-xs text-muted-foreground">
            Tutarlar <span className="font-medium">tahminidir</span> — Railway'in yayınlanmış
            oranlarıyla hesaplanır, kesin fatura değildir.
          </p>
        </div>
        <div className="flex gap-1">
          {RANGES.map(r => (
            <button
              key={r.label}
              onClick={() => setDays(r.value)}
              className={`rounded-md px-2.5 py-1 text-xs transition-colors ${
                days === r.value
                  ? 'bg-primary text-primary-foreground'
                  : 'text-muted-foreground hover:text-foreground hover:bg-accent'
              }`}
            >
              {r.label}
            </button>
          ))}
        </div>
      </div>

      {isLoading && (
        <div className="flex items-center gap-2 text-muted-foreground text-sm">
          <Loader2 className="h-4 w-4 animate-spin" /> Yükleniyor…
        </div>
      )}

      {/* Erişilemedi ya da yapılandırılmadı: ikisi de nötr — yedek sağlığıyla karıştırılmamalı. */}
      {!isLoading && (isError || !data?.configured) && (
        <div className="rounded-lg border border-border bg-muted/30 px-4 py-3 text-sm text-muted-foreground">
          {data?.message ?? 'Altyapı maliyeti yapılandırılmadı.'}
        </div>
      )}

      {!isLoading && data?.configured && (
        <>
          {/* Ortam toplamları üstte — sözleşme §7.4 */}
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm">
            {(data.environmentTotals ?? []).map(t => (
              <span key={t.environment}>
                <span className="text-muted-foreground">{t.environment}</span>{' '}
                <span className="font-semibold text-foreground tabular-nums">
                  {formatUsd(t.estimatedCostUsd)}
                </span>
              </span>
            ))}
            {data.totalEstimatedCostUsd != null && (
              <span className="ml-auto">
                <span className="text-muted-foreground">toplam</span>{' '}
                <span className="font-semibold text-foreground tabular-nums">
                  {formatUsd(data.totalEstimatedCostUsd)}
                </span>
                {data.totalEstimatedMonthlyUsd != null && (
                  <span className="text-xs text-muted-foreground">
                    {' '}· aylık projeksiyon {formatUsd(data.totalEstimatedMonthlyUsd)}
                  </span>
                )}
              </span>
            )}
          </div>

          {data.periodStartUtc && data.periodEndUtc && (
            <p className="text-xs text-muted-foreground">
              Dönem: {formatUtc(data.periodStartUtc)} — {formatUtc(data.periodEndUtc)}
              {data.periodDays != null && <> ({data.periodDays.toFixed(1)} gün)</>}
            </p>
          )}

          {(data.rows?.length ?? 0) > 0 && (
            <div className="rounded-lg border border-border overflow-x-auto">
              <table className="w-full text-sm min-w-[760px]">
                <thead>
                  <tr className="bg-muted/40 border-b border-border">
                    <th className="text-left px-4 py-3 font-medium text-muted-foreground">Ortam</th>
                    <th className="text-left px-4 py-3 font-medium text-muted-foreground">Servis</th>
                    <th className="text-right px-4 py-3 font-medium text-muted-foreground" title="Pencere boyunca ortalama">vCPU⌀</th>
                    <th className="text-right px-4 py-3 font-medium text-muted-foreground" title="Pencere boyunca ortalama">RAM⌀</th>
                    <th className="text-right px-4 py-3 font-medium text-muted-foreground" title="Pencere boyunca ortalama">Disk⌀</th>
                    <th className="text-right px-4 py-3 font-medium text-muted-foreground" title="Pencere boyunca TOPLAM giden trafik">Egress</th>
                    <th className="text-right px-4 py-3 font-medium text-muted-foreground">Tahmini $</th>
                  </tr>
                </thead>
                <tbody>
                  {/* rows kaynakta zaten sıralı — yeniden sıralanmıyor. */}
                  {(data.rows ?? []).map((r, idx) => (
                    <tr
                      key={`${r.environment}-${r.service}-${idx}`}
                      className={`border-b border-border/50 hover:bg-muted/20 transition-colors ${
                        idx === (data.rows?.length ?? 0) - 1 ? 'border-b-0' : ''
                      }`}
                    >
                      <td className="px-4 py-3 text-muted-foreground">{r.environment}</td>
                      <td className="px-4 py-3 font-medium text-foreground">{r.service}</td>
                      <td className="px-4 py-3 text-right tabular-nums text-muted-foreground">{r.avgVCpu.toFixed(3)}</td>
                      <td className="px-4 py-3 text-right tabular-nums text-muted-foreground">{r.avgRamGb.toFixed(2)} GB</td>
                      <td className="px-4 py-3 text-right tabular-nums text-muted-foreground">{r.avgDiskGb.toFixed(2)} GB</td>
                      <td className="px-4 py-3 text-right tabular-nums text-muted-foreground">{r.egressGb.toFixed(2)} GB</td>
                      <td className="px-4 py-3 text-right tabular-nums font-medium text-foreground">
                        {formatUsd(r.estimatedCostUsd)}
                        <span className="block text-[10px] text-muted-foreground font-normal">
                          ay ~{formatUsd(r.estimatedMonthlyUsd)}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </div>
  );
}

export function BackupsPage() {
  const [kind, setKind] = useState<string>('');
  const { data: status } = useBackupStatus(true);
  const { data: events = [] } = useBackupHealthEvents(true, 10);
  const { data: runs = [], isLoading, isError, error } = useBackupRuns(kind || null, 50, true);

  const errMsg =
    (error as { response?: { data?: { errors?: string[]; message?: string } } })?.response?.data
      ?.errors?.[0] ??
    (error as { response?: { data?: { message?: string } } })?.response?.data?.message ??
    'Yedek geçmişi alınamadı.';

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-foreground">Liftdesk Yedekleme</h1>
        <p className="text-muted-foreground text-sm mt-1">
          Tüm Liftdesk kurulumunu kapsayan tek altyapı yedeği — firma bazlı yedek yoktur.
          Tarihler yerel saate çevrilmiştir.
        </p>
      </div>

      {status && (
        <Card className={status.isHealthy ? 'border-emerald-500/40' : 'border-red-500/50'}>
          <CardContent className="p-4 flex items-start gap-3">
            {status.isHealthy ? (
              <ShieldCheck className="h-5 w-5 text-emerald-500 shrink-0 mt-0.5" />
            ) : (
              <ShieldAlert className="h-5 w-5 text-red-500 shrink-0 mt-0.5" />
            )}
            <div className="min-w-0 text-sm">
              {status.isHealthy ? (
                <span className="text-foreground">Yedekleme sağlıklı.</span>
              ) : (
                <ul className="space-y-1">
                  {(status.problems ?? []).map((p, i) => (
                    <li key={i} className="text-red-600 dark:text-red-400">• {p}</li>
                  ))}
                </ul>
              )}
              <p className="text-muted-foreground mt-1">
                Kaynak DB: {formatBytes(status.latestDatabaseSizeBytes)} · Son arşiv:{' '}
                {formatBytes(status.latestBackupSizeBytes)}
              </p>
            </div>
          </CardContent>
        </Card>
      )}

      {events.length > 0 && (
        <div>
          <h2 className="text-sm font-semibold text-foreground mb-2">Durum değişiklikleri</h2>
          <p className="text-xs text-muted-foreground mb-2">
            Arka plan izleyicisi 30 dakikada bir kontrol eder ve yalnız durum değiştiğinde kaydeder —
            kimse ekrana bakmasa da yazılır.
          </p>
          <div className="rounded-lg border border-border divide-y divide-border">
            {events.map(e => (
              <div key={e.id} className="px-4 py-2.5 flex items-start gap-3 text-sm">
                <span
                  className={`mt-1.5 h-2 w-2 rounded-full shrink-0 ${
                    e.isHealthy ? 'bg-emerald-500' : 'bg-red-500'
                  }`}
                />
                <div className="min-w-0">
                  <span className="font-medium text-foreground">
                    {e.isHealthy ? 'Düzeldi' : 'Sorunlu'}
                  </span>
                  <span className="text-muted-foreground"> · {formatUtc(e.detectedAt)}</span>
                  {e.problems && (
                    <p className="text-xs text-red-600 dark:text-red-400 mt-0.5 whitespace-pre-line">
                      {e.problems}
                    </p>
                  )}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      <div className="flex gap-1 border-b border-border">
        {KINDS.map(k => (
          <button
            key={k.value}
            onClick={() => setKind(k.value)}
            className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors ${
              kind === k.value
                ? 'border-primary text-primary'
                : 'border-transparent text-muted-foreground hover:text-foreground'
            }`}
          >
            {k.label}
          </button>
        ))}
      </div>

      {isLoading && (
        <div className="flex items-center gap-2 text-muted-foreground text-sm">
          <Loader2 className="h-4 w-4 animate-spin" /> Yükleniyor…
        </div>
      )}

      {isError && (
        <div className="flex items-center gap-2 text-sm text-red-600 dark:text-red-400">
          <AlertTriangle className="h-4 w-4" /> {errMsg}
        </div>
      )}

      {!isLoading && !isError && runs.length === 0 && (
        <div className="flex flex-col items-center justify-center py-16 text-center gap-2">
          <AlertTriangle className="h-8 w-8 text-amber-500/60" />
          <p className="font-medium text-foreground">Hiç koşu kaydı yok</p>
          <p className="text-sm text-muted-foreground max-w-md">
            Kayıt bulunmaması iyi haber değildir: yedekleme boru hattı hiç çalışmamış olabilir.
          </p>
        </div>
      )}

      {!isLoading && runs.length > 0 && (
        <div className="rounded-lg border border-border overflow-x-auto">
          <table className="w-full text-sm min-w-[900px]">
            <thead>
              <tr className="bg-muted/40 border-b border-border">
                <th className="text-left px-4 py-3 font-medium text-muted-foreground">Başlangıç</th>
                <th className="text-left px-4 py-3 font-medium text-muted-foreground">Tür</th>
                <th className="text-left px-4 py-3 font-medium text-muted-foreground">Durum</th>
                <th className="text-left px-4 py-3 font-medium text-muted-foreground">Doğrulama</th>
                <th className="text-left px-4 py-3 font-medium text-muted-foreground">Yedek adı</th>
                <th className="text-right px-4 py-3 font-medium text-muted-foreground">Süre</th>
                <th className="text-right px-4 py-3 font-medium text-muted-foreground">Boyut</th>
                <th className="text-right px-4 py-3 font-medium text-muted-foreground">Log</th>
              </tr>
            </thead>
            <tbody>
              {runs.map((r, idx) => (
                <tr
                  key={r.id}
                  className={`border-b border-border/50 transition-colors hover:bg-muted/20 ${
                    idx === runs.length - 1 ? 'border-b-0' : ''
                  } ${r.status === 'Failed' ? 'bg-red-500/5' : ''}`}
                >
                  <td className="px-4 py-3 whitespace-nowrap">{formatUtc(r.startedAt)}</td>
                  <td className="px-4 py-3">{KIND_TR[r.kind] ?? r.kind}</td>
                  <td className="px-4 py-3">
                    <StatusBadge status={r.status} />
                    {r.message && (
                      <div className="text-xs text-muted-foreground mt-1 max-w-[280px] truncate" title={r.message}>
                        {r.message}
                      </div>
                    )}
                  </td>
                  <td className="px-4 py-3"><VerifyBadge run={r} /></td>
                  <td className="px-4 py-3 font-mono text-xs text-muted-foreground">
                    {r.backupName ?? '—'}
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums text-muted-foreground">
                    {formatDuration(r.durationSeconds)}
                  </td>
                  <td className="px-4 py-3 text-right tabular-nums">{formatBytes(r.sizeBytes)}</td>
                  <td className="px-4 py-3 text-right">
                    {r.runUrl ? (
                      <a
                        href={r.runUrl}
                        target="_blank"
                        rel="noreferrer"
                        className="text-primary hover:underline inline-flex items-center gap-1"
                      >
                        Aç <ExternalLink className="h-3 w-3" />
                      </a>
                    ) : (
                      <span className="text-muted-foreground">—</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Altyapı maliyeti — sözleşme §7.4: yedek ekranının ALTINDA. */}
      <div className="pt-2 border-t border-border">
        <InfraUsageSection />
      </div>
    </div>
  );
}
