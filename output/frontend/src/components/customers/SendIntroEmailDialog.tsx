import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Mail, Loader2, Send, Paperclip, AlertTriangle, Settings } from 'lucide-react';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { useToast } from '@/hooks/use-toast';
import { useMailSettings, useMarketingMaterials, useSendIntroEmail } from '@/api/marketing';

function errorMessage(err: unknown, fallback: string): string {
  if (err && typeof err === 'object' && 'response' in err && err.response && typeof err.response === 'object' && 'data' in err.response) {
    const data = (err.response as { data?: { message?: string; errors?: string[] } }).data;
    if (data?.errors?.length) return data.errors[0];
    if (data?.message) return data.message;
  }
  return fallback;
}
function fmtSize(bytes: number): string { return bytes >= 1_000_000 ? `${(bytes / 1_000_000).toFixed(1)} MB` : `${Math.round(bytes / 1000)} KB`; }

interface Props { open: boolean; onClose: () => void; customerId: string; companyName: string; contactName?: string | null; email?: string | null }

/** "Tanıtım Maili": şablon + katalog + fiyat listesi, kullanıcının Gmail'inden, kendine BCC; zaman çizelgesine E-posta kaydı düşer. */
export function SendIntroEmailDialog({ open, onClose, customerId, companyName, contactName, email }: Props) {
  const { toast } = useToast();
  const navigate = useNavigate();
  const { data: settings } = useMailSettings();
  const { data: materials } = useMarketingMaterials(open);
  const send = useSendIntroEmail();
  const [to, setTo] = useState(email ?? '');
  const [templateKey, setTemplateKey] = useState('');
  const [subjectIndex, setSubjectIndex] = useState(0);
  const [note, setNote] = useState('');

  useEffect(() => { if (open) { setTo(email ?? ''); setNote(''); setSubjectIndex(0); } }, [open, email]);
  useEffect(() => { if (materials?.templates.length && !templateKey) setTemplateKey(materials.templates[0].key); }, [materials, templateKey]);
  const template = materials?.templates.find(t => t.key === templateKey) ?? materials?.templates[0];

  async function submit() {
    if (!to.trim()) { toast({ title: 'Alıcı e-posta gerekli', variant: 'destructive' }); return; }
    try {
      const r = await send.mutateAsync({ customerId, templateKey: template?.key, subjectIndex, note: note.trim() || undefined, to: to.trim() !== (email ?? '') ? to.trim() : undefined });
      toast({ title: 'Tanıtım maili gönderildi', description: `${r.to} · ${r.subject}${settings?.bccSelf ? ' · kopya sana düştü' : ''}` });
      onClose();
    } catch (err) { toast({ title: 'Gönderilemedi', description: errorMessage(err, 'Hata'), variant: 'destructive' }); }
  }

  return (
    <Dialog open={open} onOpenChange={(o) => !o && !send.isPending && onClose()}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2"><Mail className="h-5 w-5" /> Tanıtım maili gönder</DialogTitle>
          <DialogDescription>{companyName}{contactName ? ` · ${contactName}` : ''} — şablon metni, katalog ve fiyat listesi senin Gmail adresinden gider.</DialogDescription>
        </DialogHeader>

        {settings && !settings.configured ? (
          <div className="rounded-md border border-amber-500/40 bg-amber-500/5 px-3 py-2 text-sm text-amber-200 flex items-start gap-2">
            <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" />
            <span>Önce Ayarlar'da Gmail adresini ve uygulama şifresini kaydet. <button type="button" className="underline inline-flex items-center gap-1" onClick={() => { onClose(); navigate('/settings'); }}><Settings className="h-3 w-3" /> Ayarlara git</button></span>
          </div>
        ) : (
          <div className="space-y-3">
            <div className="text-xs text-muted-foreground">Gönderen: <b className="text-foreground">{settings?.address}</b>{settings?.bccSelf ? ' · kendine BCC' : ''}</div>
            <div className="space-y-1.5">
              <Label htmlFor="intro-to">Alıcı</Label>
              <Input id="intro-to" type="email" value={to} onChange={e => setTo(e.target.value)} placeholder="musteri@firma.com" className="h-10" />
              {!email && <p className="text-[11px] text-amber-400">Müşteri kartında e-posta yok; buraya yazdığın adrese gider.</p>}
            </div>
            {materials && materials.templates.length > 1 && (
              <div className="space-y-1.5">
                <Label>Şablon</Label>
                <select value={template?.key ?? ''} onChange={e => { setTemplateKey(e.target.value); setSubjectIndex(0); }} className="w-full h-10 rounded-md border border-input bg-transparent px-2 text-sm">
                  {materials.templates.map(t => <option key={t.key} value={t.key}>{t.fileName}{t.language ? ` (${t.language})` : ''}</option>)}
                </select>
              </div>
            )}
            {template && template.subjects.length > 0 && (
              <div className="space-y-1.5">
                <Label>Konu</Label>
                <select value={subjectIndex} onChange={e => setSubjectIndex(Number(e.target.value))} className="w-full h-10 rounded-md border border-input bg-transparent px-2 text-sm">
                  {template.subjects.map((s, i) => <option key={i} value={i}>{s}</option>)}
                </select>
              </div>
            )}
            <div className="space-y-1.5">
              <Label htmlFor="intro-note">Kişisel not (opsiyonel, selamlamanın hemen altına girer)</Label>
              <Textarea id="intro-note" rows={3} value={note} onChange={e => setNote(e.target.value)} maxLength={2000} placeholder="Ör: Bugün telefonda konuştuğumuz gibi katalog ve fiyat listemizi ekte bulabilirsiniz." />
            </div>
            <div className="rounded-md border border-border bg-muted/20 px-3 py-2 text-xs">
              <div className="font-medium text-foreground inline-flex items-center gap-1.5 mb-1"><Paperclip className="h-3.5 w-3.5" /> Ekler</div>
              {materials?.attachments.length ? materials.attachments.map(a => <div key={a.fileName} className="text-muted-foreground">{a.fileName} · {fmtSize(a.sizeBytes)}</div>) : <div className="text-muted-foreground">Ek bulunamadı (docs/marketing/*.pdf)</div>}
            </div>
          </div>
        )}

        <DialogFooter className="gap-2">
          <Button type="button" variant="outline" disabled={send.isPending} onClick={onClose}>İptal</Button>
          <Button type="button" disabled={send.isPending || !settings?.configured || !to.trim()} onClick={submit}>
            {send.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />} Gönder
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
