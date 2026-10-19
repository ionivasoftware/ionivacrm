import { useEffect, useState } from 'react';
import { Mail, Loader2, Send, Trash2, Eye, EyeOff, CheckCircle, AlertTriangle, ExternalLink } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { useToast } from '@/hooks/use-toast';
import { useMailSettings, useSaveMailSettings, useClearMailSettings, useTestMail } from '@/api/marketing';

function errorMessage(err: unknown, fallback: string): string {
  if (err && typeof err === 'object' && 'response' in err && err.response && typeof err.response === 'object' && 'data' in err.response) {
    const data = (err.response as { data?: { message?: string; errors?: string[] } }).data;
    if (data?.errors?.length) return data.errors[0];
    if (data?.message) return data.message;
  }
  return fallback;
}

/** Ayarlar → "E-posta Gönderimi (Gmail)": tanıtım maili kullanıcının kendi Gmail'inden çıkar. */
export function MailSettingsCard() {
  const { toast } = useToast();
  const { data, isLoading } = useMailSettings();
  const save = useSaveMailSettings();
  const clear = useClearMailSettings();
  const test = useTestMail();
  const [address, setAddress] = useState('');
  const [password, setPassword] = useState('');
  const [showPw, setShowPw] = useState(false);
  const [senderName, setSenderName] = useState('');
  const [bccSelf, setBccSelf] = useState(true);
  const [allowNonGmail, setAllowNonGmail] = useState(false);
  const busy = save.isPending || clear.isPending || test.isPending;

  useEffect(() => {
    if (!data) return;
    setAddress(data.address ?? ''); setSenderName(data.senderName ?? ''); setBccSelf(data.bccSelf); setPassword('');
    if (data.address && !/@(gmail|googlemail)\.com$/i.test(data.address)) setAllowNonGmail(true);
  }, [data]);

  async function onSave() {
    try {
      await save.mutateAsync({ address: address.trim(), password: password.trim() || undefined, senderName: senderName.trim() || undefined, bccSelf, allowNonGmail });
      setPassword('');
      toast({ title: 'E-posta ayarları kaydedildi', description: 'Test maili göndererek doğrulayın.' });
    } catch (err) { toast({ title: 'Kaydedilemedi', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }
  async function onTest() {
    try { const r = await test.mutateAsync(); toast({ title: 'Test maili gönderildi', description: `${r.to} adresine düştü; gelen kutusunu kontrol edin.` }); }
    catch (err) { toast({ title: 'Test başarısız', description: errorMessage(err, 'Gönderilemedi.'), variant: 'destructive' }); }
  }
  async function onClear() {
    try { await clear.mutateAsync(); setPassword(''); toast({ title: 'E-posta ayarları silindi' }); }
    catch (err) { toast({ title: 'Silinemedi', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base"><Mail className="h-4 w-4" /> E-posta Gönderimi (Gmail)</CardTitle>
        <CardDescription>
          Tanıtım maili (şablon + katalog + fiyat listesi) müşteriye <b>senin Gmail adresinden</b> gider; takip için kendine BCC düşer. Normal şifre değil, Google <b>Uygulama Şifresi</b> gerekir.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        {isLoading ? <div className="text-sm text-muted-foreground flex items-center gap-2"><Loader2 className="h-4 w-4 animate-spin" /> Yükleniyor…</div> : (
          <>
            <div className={`flex items-center gap-2 rounded-lg border px-3 py-2 text-sm ${data?.configured ? 'border-emerald-500/30 bg-emerald-500/10 text-emerald-300' : 'border-amber-500/30 bg-amber-500/10 text-amber-200'}`}>
              {data?.configured ? <CheckCircle className="h-4 w-4" /> : <AlertTriangle className="h-4 w-4" />}
              {data?.configured ? <span>Hazır: <b>{data.address}</b>{data.bccSelf ? ' · kendine BCC açık' : ''}</span> : <span>Henüz ayarlanmadı — tanıtım maili gönderilemez.</span>}
            </div>
            <div className="grid gap-3 sm:grid-cols-2">
              <div className="space-y-1.5 sm:col-span-2">
                <Label htmlFor="mail-address">Gmail adresi</Label>
                <Input id="mail-address" type="email" value={address} onChange={e => setAddress(e.target.value)} placeholder="ad.soyad@gmail.com" className="h-11" autoComplete="off" />
              </div>
              <div className="space-y-1.5 sm:col-span-2">
                <Label htmlFor="mail-password">Uygulama şifresi (16 hane){data?.hasPassword && <span className="text-muted-foreground font-normal"> — kayıtlı, değiştirmek için yeni şifre girin</span>}</Label>
                <div className="relative">
                  <Input id="mail-password" type={showPw ? 'text' : 'password'} value={password} onChange={e => setPassword(e.target.value)} placeholder={data?.hasPassword ? '••••••••••••••••' : 'xxxx xxxx xxxx xxxx'} className="h-11 pr-10" autoComplete="new-password" />
                  <button type="button" onClick={() => setShowPw(v => !v)} className="absolute right-2 top-3 text-muted-foreground" aria-label="Şifreyi göster/gizle">{showPw ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}</button>
                </div>
                <p className="text-[11px] text-muted-foreground">
                  Google Hesabı → Güvenlik → 2 Adımlı Doğrulama açık olmalı → <a href="https://myaccount.google.com/apppasswords" target="_blank" rel="noopener noreferrer" className="text-primary inline-flex items-center gap-1 hover:underline">Uygulama şifreleri <ExternalLink className="h-3 w-3" /></a> → "ION CRM" adıyla oluştur, 16 haneli şifreyi buraya yapıştır. Şifre sunucuda şifreli saklanır, ekranda bir daha gösterilmez.
                </p>
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="mail-sender">Gönderen adı</Label>
                <Input id="mail-sender" value={senderName} onChange={e => setSenderName(e.target.value)} placeholder="Ad Soyad (boşsa profil adın)" className="h-11" maxLength={120} />
              </div>
              <div className="space-y-1.5">
                <Label>Seçenekler</Label>
                <label className="flex items-center gap-2 h-11 text-sm"><input type="checkbox" checked={bccSelf} onChange={e => setBccSelf(e.target.checked)} /> Her maili kendime BCC gönder (takip)</label>
              </div>
              {!/@(gmail|googlemail)\.com$/i.test(address) && address.includes('@') && (
                <label className="flex items-center gap-2 text-xs text-muted-foreground sm:col-span-2"><input type="checkbox" checked={allowNonGmail} onChange={e => setAllowNonGmail(e.target.checked)} /> Bu bir Google Workspace (şirket alan adı) Gmail adresi — yine smtp.gmail.com üzerinden gönder</label>
              )}
            </div>
            <div className="flex flex-wrap gap-2">
              <Button type="button" disabled={busy || !address.trim() || (!password.trim() && !data?.hasPassword)} onClick={onSave} className="h-10">
                {save.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <CheckCircle className="h-4 w-4" />} Kaydet
              </Button>
              <Button type="button" variant="outline" disabled={busy || !data?.configured} onClick={onTest} className="h-10" title="Kendi adresine test maili gönderir">
                {test.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />} Test maili gönder
              </Button>
              {data?.configured && (
                <Button type="button" variant="ghost" disabled={busy} onClick={onClear} className="h-10 text-red-400 hover:text-red-300 ml-auto"><Trash2 className="h-4 w-4" /> Ayarları sil</Button>
              )}
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}
