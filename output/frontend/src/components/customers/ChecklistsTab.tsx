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
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from '@/components/ui/select';
import {
  CHECKLIST_LISTS,
  useCustomerChecklist,
  useResetCustomerChecklists,
  useUpdateCustomerChecklist,
  type ChecklistDoc,
  type ChecklistHeaderInput,
  type ChecklistListKey,
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

const LIST_ORDER: ChecklistListKey[] = ['maintenance', 'escalator', 'fault'];

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

// ── Small building blocks ─────────────────────────────────────────────────────

/** Fixed-size icon button so every row control lines up on the same grid. */
function IconButton({
  title,
  onClick,
  disabled,
  danger,
  className,
  children,
}: {
  title: string;
  onClick: () => void;
  disabled?: boolean;
  danger?: boolean;
  className?: string;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      title={title}
      aria-label={title}
      disabled={disabled}
      onClick={onClick}
      className={cn(
        'inline-flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-md transition-colors',
        'text-muted-foreground hover:bg-muted disabled:pointer-events-none disabled:opacity-30',
        danger ? 'hover:text-red-400' : 'hover:text-foreground',
        className
      )}
    >
      {children}
    </button>
  );
}

/** Up/down pair laid out horizontally so the row keeps a single, predictable height. */
function ReorderControls({
  onUp,
  onDown,
  disableUp,
  disableDown,
}: {
  onUp: () => void;
  onDown: () => void;
  disableUp: boolean;
  disableDown: boolean;
}) {
  return (
    <div className="flex flex-shrink-0 items-center">
      <IconButton title="Yukarı taşı" onClick={onUp} disabled={disableUp} className="h-7 w-6">
        <ChevronUp className="h-3.5 w-3.5" />
      </IconButton>
      <IconButton title="Aşağı taşı" onClick={onDown} disabled={disableDown} className="h-7 w-6">
        <ChevronDown className="h-3.5 w-3.5" />
      </IconButton>
    </div>
  );
}

// ── Reset dialog ──────────────────────────────────────────────────────────────

type ResetScope = ChecklistListKey | 'both';

function ResetDialog({
  customerId,
  currentList,
  hasUnsavedChanges,
  onClose,
  onResetSuccess,
}: {
  customerId: string;
  currentList: ChecklistListKey;
  hasUnsavedChanges: boolean;
  onClose: () => void;
  onResetSuccess: (scope: ResetScope) => void;
}) {
  const { toast } = useToast();
  const resetMutation = useResetCustomerChecklists(customerId);
  const [scope, setScope] = useState<ResetScope>(currentList);

  const options: { value: ResetScope; label: string }[] = [
    { value: 'both', label: 'Tümü (Asansör + Yürüyen Merdiven + Arıza)' },
    ...LIST_ORDER.map((k) => ({ value: k as ResetScope, label: CHECKLIST_LISTS[k].label })),
  ];

  async function handleReset() {
    try {
      await resetMutation.mutateAsync({ kind: scope });
      onResetSuccess(scope);
      toast({
        title: 'Varsayılana döndürüldü',
        description:
          scope === 'both'
            ? 'Tüm checklistler varsayılan şablona sıfırlandı.'
            : `${CHECKLIST_LISTS[scope].label} listesi varsayılan şablona sıfırlandı.`,
      });
      onClose();
    } catch (err) {
      toast({ title: 'Sıfırlanamadı', description: extractApiError(err), variant: 'destructive' });
    }
  }

  const affectsOpenList = scope === 'both' || scope === currentList;

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <RotateCcw className="h-5 w-5 text-red-400" />
            Varsayılana Döndür
          </DialogTitle>
          <DialogDescription>
            Seçilen liste(ler) Liftdesk varsayılan şablonuna sıfırlanır.
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
                {opt.value !== 'both' && opt.value === currentList && (
                  <span className="ml-2 text-xs text-muted-foreground">(açık olan)</span>
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
            {hasUnsavedChanges && affectsOpenList && (
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
  const [list, setList] = useState<ChecklistListKey>('maintenance');
  const [showResetDialog, setShowResetDialog] = useState(false);

  const { data, isLoading, error } = useCustomerChecklist(customerId, list, true);
  const updateMutation = useUpdateCustomerChecklist(customerId);

  const [headers, setHeaders] = useState<EditorHeader[]>([]);
  const [dirty, setDirty] = useState(false);
  // While there are unsaved edits, background refetches must NOT wipe them; after a
  // save/reset/list-switch the editor is rehydrated explicitly (hydratedFrom = null).
  const hydratedFrom = useRef<ChecklistDoc | null>(null);

  useEffect(() => {
    if (!data || dirty || hydratedFrom.current === data) return;
    hydratedFrom.current = data;
    setHeaders(docToEditor(data));
  }, [data, dirty]);

  const validationError = useMemo(() => {
    if (headers.length === 0) return 'Checklist en az bir başlık içermelidir.';
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

  function addHeader() {
    mutateHeaders((prev) => [...prev, { key: nextKey(), title: '', isActive: true, items: [] }]);
  }

  async function handleSave() {
    if (validationError) {
      toast({ title: 'Kaydedilemedi', description: validationError, variant: 'destructive' });
      return;
    }
    try {
      const saved = await updateMutation.mutateAsync({ list, headers: editorToPayload(headers) });
      if (saved) {
        hydratedFrom.current = saved;
        setHeaders(docToEditor(saved));
      }
      setDirty(false);
      toast({
        title: 'Checklist kaydedildi',
        description: `${CHECKLIST_LISTS[list].label} listesi Liftdesk'e yazıldı.`,
      });
    } catch (err) {
      toast({ title: 'Kaydedilemedi', description: extractApiError(err), variant: 'destructive' });
    }
  }

  function switchList(next: ChecklistListKey) {
    if (next === list) return;
    if (dirty && !window.confirm('Kaydedilmemiş değişiklikler var. Liste değiştirilirse kaybolur. Devam edilsin mi?')) {
      return;
    }
    hydratedFrom.current = null;
    setDirty(false);
    setList(next);
  }

  // After a reset the server state is authoritative FOR THE RESET LISTS — rehydrate from the
  // (already-updated) query cache. When only another list was reset, keep the open editor intact.
  function handleResetSuccess(scope: ResetScope) {
    if (scope !== 'both' && scope !== list) return;
    hydratedFrom.current = null;
    setDirty(false);
  }

  const itemCount = headers.reduce((sum, h) => sum + h.items.length, 0);

  // ── Render ──────────────────────────────────────────────────────────────────

  return (
    <div className="space-y-4">
      {/* Toolbar: which list + actions */}
      <div className="flex flex-wrap items-center gap-2">
        <Select value={list} onValueChange={(v) => switchList(v as ChecklistListKey)}>
          <SelectTrigger className="h-9 w-full sm:w-64">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {LIST_ORDER.map((k) => (
              <SelectItem key={k} value={k}>{CHECKLIST_LISTS[k].label}</SelectItem>
            ))}
          </SelectContent>
        </Select>

        {!isLoading && data && (
          <span className="text-xs text-muted-foreground whitespace-nowrap">
            {headers.length} başlık · {itemCount} madde
          </span>
        )}

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
            Kaydedilmemiş değişiklikler var. “Kaydet” yalnız açık olan listeyi ({CHECKLIST_LISTS[list].label})
            baştan yazar; diğer listeler etkilenmez.
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
          <p className="font-medium text-foreground">Liste boş</p>
          <p className="text-sm text-muted-foreground max-w-sm">
            Bu firmanın “{CHECKLIST_LISTS[list].label}” listesi boş. “Varsayılana Döndür” ile hazır
            şablonu uygulayabilir veya elle başlık ekleyebilirsiniz.
          </p>
          <Button variant="outline" size="sm" className="gap-1.5" onClick={addHeader}>
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
              <div className="flex items-center gap-2 bg-muted/40 border-b border-border px-2 py-2">
                <ReorderControls
                  onUp={() => mutateHeaders((prev) => moveEntry(prev, hIdx, -1))}
                  onDown={() => mutateHeaders((prev) => moveEntry(prev, hIdx, 1))}
                  disableUp={hIdx === 0}
                  disableDown={hIdx === headers.length - 1}
                />
                {/* Capped width: a title field spanning the whole card is unreadable on wide screens. */}
                <Input
                  value={header.title}
                  placeholder="Başlık adı"
                  className="h-8 w-full max-w-md font-medium"
                  onChange={(e) =>
                    mutateHeaders((prev) =>
                      prev.map((h) => (h.key === header.key ? { ...h, title: e.target.value } : h))
                    )
                  }
                />
                <div className="ml-auto flex items-center gap-1">
                  <button
                    type="button"
                    className={cn(
                      'inline-flex h-7 items-center gap-1 rounded-md border px-2 text-xs font-medium transition-colors',
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
                  <IconButton
                    title="Başlığı sil"
                    danger
                    onClick={() => mutateHeaders((prev) => prev.filter((h) => h.key !== header.key))}
                  >
                    <Trash2 className="h-4 w-4" />
                  </IconButton>
                </div>
              </div>

              {/* Items */}
              <div className="divide-y divide-border/50">
                {header.items.map((item, iIdx) => (
                  <div
                    key={item.key}
                    className={cn('flex items-center gap-2 px-2 py-1', !item.isActive && 'opacity-60')}
                  >
                    <ReorderControls
                      onUp={() =>
                        mutateHeaders((prev) =>
                          prev.map((h) =>
                            h.key === header.key ? { ...h, items: moveEntry(h.items, iIdx, -1) } : h
                          )
                        )
                      }
                      onDown={() =>
                        mutateHeaders((prev) =>
                          prev.map((h) =>
                            h.key === header.key ? { ...h, items: moveEntry(h.items, iIdx, 1) } : h
                          )
                        )
                      }
                      disableUp={iIdx === 0}
                      disableDown={iIdx === header.items.length - 1}
                    />
                    <Input
                      value={item.text}
                      placeholder="Madde metni"
                      className="h-8 w-full max-w-lg text-sm"
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
                    <div className="ml-auto flex items-center gap-1">
                      <IconButton
                        title={item.isActive ? 'Pasife al' : 'Aktifleştir'}
                        className={item.isActive ? 'text-green-400 hover:text-green-300' : undefined}
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
                      >
                        {item.isActive ? <Eye className="h-3.5 w-3.5" /> : <EyeOff className="h-3.5 w-3.5" />}
                      </IconButton>
                      <IconButton
                        title="Maddeyi sil"
                        danger
                        onClick={() =>
                          mutateHeaders((prev) =>
                            prev.map((h) =>
                              h.key === header.key
                                ? { ...h, items: h.items.filter((i) => i.key !== item.key) }
                                : h
                            )
                          )
                        }
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </IconButton>
                    </div>
                  </div>
                ))}

                <div className="px-2 py-1.5">
                  <button
                    type="button"
                    className="inline-flex items-center gap-1 rounded-md px-1.5 py-1 text-xs text-muted-foreground hover:bg-muted hover:text-foreground transition-colors"
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

          <Button variant="outline" size="sm" className="gap-1.5" onClick={addHeader}>
            <Plus className="h-3.5 w-3.5" />
            Başlık Ekle
          </Button>
        </div>
      )}

      {showResetDialog && (
        <ResetDialog
          customerId={customerId}
          currentList={list}
          hasUnsavedChanges={dirty}
          onClose={() => setShowResetDialog(false)}
          onResetSuccess={handleResetSuccess}
        />
      )}
    </div>
  );
}
