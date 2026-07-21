import { useEffect, useMemo, useRef, useState } from 'react';
import {
  AlertTriangle,
  ChevronDown,
  ChevronUp,
  Eye,
  EyeOff,
  ListChecks,
  Loader2,
  Plus,
  RotateCcw,
  Save,
  Trash2,
  Wrench,
  Zap,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  useCustomerChecklist,
  useResetCustomerChecklists,
  useUpdateCustomerChecklist,
  type ChecklistDoc,
  type ChecklistHeaderInput,
  type ChecklistKind,
} from '@/api/customers';
import { useToast } from '@/hooks/use-toast';
import { cn } from '@/lib/utils';

// ── Editor state ──────────────────────────────────────────────────────────────
// Local keys (not server ids) so add/remove/reorder work before save. The PUT is a
// full-document replace: array order becomes the sort order, ids are never sent.

type EditorItem = { key: string; text: string; isActive: boolean };
type EditorHeader = { key: string; title: string; isActive: boolean; items: EditorItem[] };

let keyCounter = 0;
const nextKey = () => `k${++keyCounter}`;

function docToEditor(doc: ChecklistDoc): EditorHeader[] {
  return doc.headers.map((h) => ({
    key: nextKey(),
    title: h.title,
    isActive: h.isActive,
    items: h.items.map((i) => ({ key: nextKey(), text: i.text, isActive: i.isActive })),
  }));
}

function editorToPayload(headers: EditorHeader[]): ChecklistHeaderInput[] {
  return headers.map((h) => ({
    title: h.title.trim(),
    isActive: h.isActive,
    items: h.items.map((i) => ({ text: i.text.trim(), isActive: i.isActive })),
  }));
}

function extractApiError(err: unknown): string {
  return (
    (err as { response?: { data?: { errors?: string[] } } })?.response?.data?.errors?.[0] ??
    (err as { response?: { data?: { message?: string } } })?.response?.data?.message ??
    (err as Error)?.message ??
    'Bilinmeyen hata'
  );
}

const KIND_LABEL: Record<ChecklistKind, string> = {
  maintenance: 'Bakım',
  fault: 'Arıza',
};

// ── Reset dialog ──────────────────────────────────────────────────────────────

function ResetDialog({
  customerId,
  currentKind,
  hasUnsavedChanges,
  onClose,
  onResetSuccess,
}: {
  customerId: string;
  currentKind: ChecklistKind;
  hasUnsavedChanges: boolean;
  onClose: () => void;
  onResetSuccess: (scope: ChecklistKind | 'both') => void;
}) {
  const { toast } = useToast();
  const resetMutation = useResetCustomerChecklists(customerId);
  const [scope, setScope] = useState<ChecklistKind | 'both'>('both');

  const options: { value: ChecklistKind | 'both'; label: string }[] = [
    { value: 'both', label: 'Her ikisi (Bakım + Arıza)' },
    { value: 'maintenance', label: 'Yalnızca Bakım' },
    { value: 'fault', label: 'Yalnızca Arıza' },
  ];

  async function handleReset() {
    try {
      await resetMutation.mutateAsync({ kind: scope });
      onResetSuccess(scope);
      toast({
        title: 'Varsayılana döndürüldü',
        description:
          scope === 'both'
            ? 'Bakım ve arıza checklistleri varsayılan şablona sıfırlandı.'
            : `${KIND_LABEL[scope as ChecklistKind]} checklisti varsayılan şablona sıfırlandı.`,
      });
      onClose();
    } catch (err) {
      toast({ title: 'Sıfırlanamadı', description: extractApiError(err), variant: 'destructive' });
    }
  }

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <RotateCcw className="h-5 w-5 text-red-400" />
            Varsayılana Döndür
          </DialogTitle>
          <DialogDescription>
            Seçilen checklist(ler) Liftdesk varsayılan şablonuna sıfırlanır.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-2">
          {options.map((opt) => (
            <label
              key={opt.value}
              className={cn(
                'flex items-center gap-3 rounded-lg border p-3 cursor-pointer transition-colors',
                scope === opt.value
                  ? 'border-primary bg-primary/5'
                  : 'border-border hover:border-primary/40'
              )}
            >
              <input
                type="radio"
                name="reset-scope"
                checked={scope === opt.value}
                onChange={() => setScope(opt.value)}
                className="accent-primary"
              />
              <span className="text-sm font-medium text-foreground">
                {opt.label}
                {opt.value !== 'both' && opt.value === currentKind && (
                  <span className="ml-2 text-xs text-muted-foreground">(şu an görüntülenen)</span>
                )}
              </span>
            </label>
          ))}
        </div>

        <div className="flex items-start gap-2 rounded-lg border border-red-500/30 bg-red-500/5 p-3">
          <AlertTriangle className="h-4 w-4 text-red-400 flex-shrink-0 mt-0.5" />
          <p className="text-xs text-red-300">
            Bu işlem geri alınamaz: firmanın mevcut checklist özelleştirmesi silinir ve yerine
            varsayılan şablon yazılır.
            {hasUnsavedChanges && (scope === 'both' || scope === currentKind) && (
              <> Ekranda kaydedilmemiş değişiklikleriniz de kaybolur.</>
            )}
          </p>
        </div>

        <DialogFooter>
          <Button variant="ghost" onClick={onClose} disabled={resetMutation.isPending}>
            İptal
          </Button>
          <Button
            onClick={handleReset}
            disabled={resetMutation.isPending}
            className="bg-red-500 hover:bg-red-600 text-white"
          >
            {resetMutation.isPending ? (
              <>
                <Loader2 className="h-4 w-4 mr-1.5 animate-spin" />
                Sıfırlanıyor...
              </>
            ) : (
              <>
                <RotateCcw className="h-4 w-4 mr-1.5" />
                Sıfırla
              </>
            )}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Main tab ──────────────────────────────────────────────────────────────────

export function ChecklistsTab({ customerId }: { customerId: string }) {
  const { toast } = useToast();
  const [kind, setKind] = useState<ChecklistKind>('maintenance');
  const [showResetDialog, setShowResetDialog] = useState(false);

  const { data, isLoading, error } = useCustomerChecklist(customerId, kind, true);
  const updateMutation = useUpdateCustomerChecklist(customerId);

  const [headers, setHeaders] = useState<EditorHeader[]>([]);
  const [dirty, setDirty] = useState(false);
  // While there are unsaved edits, background refetches must NOT wipe them; after a
  // save/reset/kind-switch the editor is rehydrated explicitly (hydratedFrom = null).
  const hydratedFrom = useRef<ChecklistDoc | null>(null);

  useEffect(() => {
    if (!data || dirty || hydratedFrom.current === data) return;
    hydratedFrom.current = data;
    setHeaders(docToEditor(data));
  }, [data, dirty]);

  const validationError = useMemo(() => {
    for (const h of headers) {
      if (!h.title.trim()) return 'Boş başlık adı var — kaydetmeden önce doldurun.';
      for (const i of h.items) {
        if (!i.text.trim()) return `"${h.title.trim() || '—'}" başlığında boş madde var.`;
      }
    }
    return null;
  }, [headers]);

  function mutateHeaders(fn: (prev: EditorHeader[]) => EditorHeader[]) {
    setHeaders(fn);
    setDirty(true);
  }

  function moveEntry<T>(arr: T[], index: number, delta: -1 | 1): T[] {
    const target = index + delta;
    if (target < 0 || target >= arr.length) return arr;
    const copy = [...arr];
    [copy[index], copy[target]] = [copy[target], copy[index]];
    return copy;
  }

  async function handleSave() {
    if (validationError) {
      toast({ title: 'Kaydedilemedi', description: validationError, variant: 'destructive' });
      return;
    }
    try {
      const saved = await updateMutation.mutateAsync({ kind, headers: editorToPayload(headers) });
      if (saved) {
        hydratedFrom.current = saved;
        setHeaders(docToEditor(saved));
      }
      setDirty(false);
      toast({
        title: 'Checklist kaydedildi',
        description: `${KIND_LABEL[kind]} checklisti Liftdesk'e yazıldı.`,
      });
    } catch (err) {
      toast({ title: 'Kaydedilemedi', description: extractApiError(err), variant: 'destructive' });
    }
  }

  function switchKind(next: ChecklistKind) {
    if (next === kind) return;
    if (dirty && !window.confirm('Kaydedilmemiş değişiklikler var. Sekme değiştirilirse kaybolur. Devam edilsin mi?')) {
      return;
    }
    hydratedFrom.current = null;
    setDirty(false);
    setKind(next);
  }

  // After a reset the server state is authoritative FOR THE RESET KINDS — rehydrate from the
  // (already-updated) query cache. When only the other kind was reset, the current editor and
  // its unsaved edits must stay untouched.
  function handleResetSuccess(scope: ChecklistKind | 'both') {
    if (scope !== 'both' && scope !== kind) return;
    hydratedFrom.current = null;
    setDirty(false);
  }

  // ── Render ──────────────────────────────────────────────────────────────────

  return (
    <div className="space-y-4">
      {/* Kind switcher + actions */}
      <div className="flex flex-wrap items-center gap-2">
        <div className="inline-flex rounded-lg border border-border p-0.5 bg-muted/30">
          {(['maintenance', 'fault'] as ChecklistKind[]).map((k) => (
            <button
              key={k}
              onClick={() => switchKind(k)}
              className={cn(
                'flex items-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium transition-colors',
                kind === k
                  ? 'bg-background text-foreground shadow-sm'
                  : 'text-muted-foreground hover:text-foreground'
              )}
            >
              {k === 'maintenance' ? <Wrench className="h-3.5 w-3.5" /> : <Zap className="h-3.5 w-3.5" />}
              {KIND_LABEL[k]}
            </button>
          ))}
        </div>

        <div className="ml-auto flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            className="gap-1.5 border-red-500/40 text-red-400 hover:bg-red-500/10 hover:text-red-300"
            onClick={() => setShowResetDialog(true)}
          >
            <RotateCcw className="h-3.5 w-3.5" />
            Varsayılana Döndür
          </Button>
          <Button
            size="sm"
            className="gap-1.5"
            onClick={handleSave}
            disabled={!dirty || updateMutation.isPending || isLoading}
          >
            {updateMutation.isPending ? (
              <>
                <Loader2 className="h-3.5 w-3.5 animate-spin" />
                Kaydediliyor...
              </>
            ) : (
              <>
                <Save className="h-3.5 w-3.5" />
                Kaydet
              </>
            )}
          </Button>
        </div>
      </div>

      {dirty && (
        <div className="flex items-center gap-2 rounded-lg border border-amber-500/30 bg-amber-500/5 px-3 py-2">
          <AlertTriangle className="h-4 w-4 text-amber-400 flex-shrink-0" />
          <p className="text-xs text-amber-300">
            Kaydedilmemiş değişiklikler var. "Kaydet" checklist'in tamamını Liftdesk'e yazar.
          </p>
        </div>
      )}

      {isLoading && (
        <div className="space-y-3">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-28 rounded-lg" />
          ))}
        </div>
      )}

      {!isLoading && error != null && !data && (
        <div className="flex flex-col items-center justify-center py-16 text-center gap-2">
          <AlertTriangle className="h-9 w-9 text-muted-foreground/40" />
          <p className="text-sm font-medium text-muted-foreground">Checklist alınamadı</p>
          <p className="text-xs text-muted-foreground/70 max-w-sm">{extractApiError(error)}</p>
        </div>
      )}

      {!isLoading && data && headers.length === 0 && (
        <div className="flex flex-col items-center justify-center py-16 text-center gap-3">
          <div className="w-16 h-16 rounded-full bg-muted flex items-center justify-center">
            <ListChecks className="h-8 w-8 text-muted-foreground/40" />
          </div>
          <p className="font-medium text-foreground">Checklist boş</p>
          <p className="text-sm text-muted-foreground max-w-sm">
            Bu firmanın {KIND_LABEL[kind].toLowerCase()} checklisti boş. "Varsayılana Döndür" ile
            hazır şablonu uygulayabilir veya elle başlık ekleyebilirsiniz.
          </p>
          <Button
            variant="outline"
            size="sm"
            className="gap-1.5"
            onClick={() => mutateHeaders((prev) => [...prev, { key: nextKey(), title: '', isActive: true, items: [] }])}
          >
            <Plus className="h-3.5 w-3.5" />
            Başlık Ekle
          </Button>
        </div>
      )}

      {!isLoading && data && headers.length > 0 && (
        <div className="space-y-3">
          {headers.map((header, hIdx) => (
            <div
              key={header.key}
              className={cn(
                'rounded-lg border border-border overflow-hidden',
                !header.isActive && 'opacity-60'
              )}
            >
              {/* Header row */}
              <div className="flex items-center gap-2 bg-muted/40 border-b border-border px-3 py-2">
                <div className="flex flex-col">
                  <button
                    className="text-muted-foreground hover:text-foreground disabled:opacity-30"
                    disabled={hIdx === 0}
                    onClick={() => mutateHeaders((prev) => moveEntry(prev, hIdx, -1))}
                    title="Yukarı taşı"
                  >
                    <ChevronUp className="h-3.5 w-3.5" />
                  </button>
                  <button
                    className="text-muted-foreground hover:text-foreground disabled:opacity-30"
                    disabled={hIdx === headers.length - 1}
                    onClick={() => mutateHeaders((prev) => moveEntry(prev, hIdx, 1))}
                    title="Aşağı taşı"
                  >
                    <ChevronDown className="h-3.5 w-3.5" />
                  </button>
                </div>
                <Input
                  value={header.title}
                  placeholder="Başlık adı"
                  className="h-8 font-medium"
                  onChange={(e) =>
                    mutateHeaders((prev) =>
                      prev.map((h) => (h.key === header.key ? { ...h, title: e.target.value } : h))
                    )
                  }
                />
                <button
                  className={cn(
                    'flex items-center gap-1 rounded-md border px-2 py-1 text-xs font-medium transition-colors flex-shrink-0',
                    header.isActive
                      ? 'border-green-500/40 text-green-400 hover:bg-green-500/10'
                      : 'border-border text-muted-foreground hover:text-foreground'
                  )}
                  onClick={() =>
                    mutateHeaders((prev) =>
                      prev.map((h) => (h.key === header.key ? { ...h, isActive: !h.isActive } : h))
                    )
                  }
                  title={header.isActive ? 'Pasife al' : 'Aktifleştir'}
                >
                  {header.isActive ? <Eye className="h-3 w-3" /> : <EyeOff className="h-3 w-3" />}
                  {header.isActive ? 'Aktif' : 'Pasif'}
                </button>
                <button
                  className="text-muted-foreground hover:text-red-400 flex-shrink-0"
                  onClick={() => mutateHeaders((prev) => prev.filter((h) => h.key !== header.key))}
                  title="Başlığı sil"
                >
                  <Trash2 className="h-4 w-4" />
                </button>
              </div>

              {/* Items */}
              <div className="divide-y divide-border/50">
                {header.items.map((item, iIdx) => (
                  <div key={item.key} className={cn('flex items-center gap-2 px-3 py-1.5', !item.isActive && 'opacity-60')}>
                    <div className="flex flex-col">
                      <button
                        className="text-muted-foreground hover:text-foreground disabled:opacity-30"
                        disabled={iIdx === 0}
                        onClick={() =>
                          mutateHeaders((prev) =>
                            prev.map((h) =>
                              h.key === header.key ? { ...h, items: moveEntry(h.items, iIdx, -1) } : h
                            )
                          )
                        }
                        title="Yukarı taşı"
                      >
                        <ChevronUp className="h-3 w-3" />
                      </button>
                      <button
                        className="text-muted-foreground hover:text-foreground disabled:opacity-30"
                        disabled={iIdx === header.items.length - 1}
                        onClick={() =>
                          mutateHeaders((prev) =>
                            prev.map((h) =>
                              h.key === header.key ? { ...h, items: moveEntry(h.items, iIdx, 1) } : h
                            )
                          )
                        }
                        title="Aşağı taşı"
                      >
                        <ChevronDown className="h-3 w-3" />
                      </button>
                    </div>
                    <Input
                      value={item.text}
                      placeholder="Madde metni"
                      className="h-7 text-sm"
                      onChange={(e) =>
                        mutateHeaders((prev) =>
                          prev.map((h) =>
                            h.key === header.key
                              ? {
                                  ...h,
                                  items: h.items.map((i) =>
                                    i.key === item.key ? { ...i, text: e.target.value } : i
                                  ),
                                }
                              : h
                          )
                        )
                      }
                    />
                    <button
                      className={cn(
                        'flex-shrink-0 transition-colors',
                        item.isActive ? 'text-green-400 hover:text-green-300' : 'text-muted-foreground hover:text-foreground'
                      )}
                      onClick={() =>
                        mutateHeaders((prev) =>
                          prev.map((h) =>
                            h.key === header.key
                              ? {
                                  ...h,
                                  items: h.items.map((i) =>
                                    i.key === item.key ? { ...i, isActive: !i.isActive } : i
                                  ),
                                }
                              : h
                          )
                        )
                      }
                      title={item.isActive ? 'Pasife al' : 'Aktifleştir'}
                    >
                      {item.isActive ? <Eye className="h-3.5 w-3.5" /> : <EyeOff className="h-3.5 w-3.5" />}
                    </button>
                    <button
                      className="text-muted-foreground hover:text-red-400 flex-shrink-0"
                      onClick={() =>
                        mutateHeaders((prev) =>
                          prev.map((h) =>
                            h.key === header.key
                              ? { ...h, items: h.items.filter((i) => i.key !== item.key) }
                              : h
                          )
                        )
                      }
                      title="Maddeyi sil"
                    >
                      <Trash2 className="h-3.5 w-3.5" />
                    </button>
                  </div>
                ))}

                <div className="px-3 py-1.5">
                  <button
                    className="flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground transition-colors"
                    onClick={() =>
                      mutateHeaders((prev) =>
                        prev.map((h) =>
                          h.key === header.key
                            ? { ...h, items: [...h.items, { key: nextKey(), text: '', isActive: true }] }
                            : h
                        )
                      )
                    }
                  >
                    <Plus className="h-3 w-3" />
                    Madde ekle
                  </button>
                </div>
              </div>
            </div>
          ))}

          <Button
            variant="outline"
            size="sm"
            className="gap-1.5"
            onClick={() =>
              mutateHeaders((prev) => [...prev, { key: nextKey(), title: '', isActive: true, items: [] }])
            }
          >
            <Plus className="h-3.5 w-3.5" />
            Başlık Ekle
          </Button>
        </div>
      )}

      {showResetDialog && (
        <ResetDialog
          customerId={customerId}
          currentKind={kind}
          hasUnsavedChanges={dirty}
          onClose={() => setShowResetDialog(false)}
          onResetSuccess={handleResetSuccess}
        />
      )}
    </div>
  );
}
