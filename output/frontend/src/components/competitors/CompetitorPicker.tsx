import { useMemo, useState } from 'react';
import { Building2, Loader2, Plus, Search, X } from 'lucide-react';
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { useToast } from '@/hooks/use-toast';
import { cn } from '@/lib/utils';
import { useCompetitors, useCreateCompetitor } from '@/api/competitors';

export interface CompetitorChoice {
  /** Müşteri farklı bir firma ile çalışıyor. */
  usesCompetitor: boolean;
  /** Hangi firma — bilinmiyorsa null. */
  competitorId: string | null;
}

interface Props {
  projectId: string;
  value: CompetitorChoice;
  onChange: (next: CompetitorChoice) => void;
  disabled?: boolean;
  className?: string;
}

/**
 * Görüşme kaydında rakip seçimi: önce "farklı firma ile çalışıyor" işareti, sonra firma listesi.
 * Yazılan ad listede yoksa tek tıkla yeni firma açılır ve seçilir. Firma bilinmiyorsa boş bırakılabilir.
 */
export function CompetitorPicker({ projectId, value, onChange, disabled, className }: Props) {
  const { toast } = useToast();
  const { data: competitors = [], isLoading } = useCompetitors(value.usesCompetitor ? projectId : null);
  const create = useCreateCompetitor();
  const [search, setSearch] = useState('');

  const selected = useMemo(() => competitors.find(c => c.id === value.competitorId) ?? null, [competitors, value.competitorId]);
  const q = search.trim().toLocaleLowerCase('tr-TR');
  const matches = useMemo(() => {
    const list = q ? competitors.filter(c => c.name.toLocaleLowerCase('tr-TR').includes(q)) : competitors;
    return list.slice(0, 8);
  }, [competitors, q]);
  const exact = q.length > 0 && competitors.some(c => c.name.toLocaleLowerCase('tr-TR') === q);
  const canCreate = search.trim().length >= 2 && !exact;

  async function createAndPick() {
    const name = search.trim();
    if (name.length < 2) return;
    try {
      const c = await create.mutateAsync({ projectId, name });
      onChange({ usesCompetitor: true, competitorId: c.id });
      setSearch('');
      toast({ title: 'Rakip firma eklendi', description: c.name });
    } catch {
      toast({ title: 'Firma eklenemedi', variant: 'destructive' });
    }
  }

  return (
    <div className={cn('space-y-2', className)}>
      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          checked={value.usesCompetitor}
          disabled={disabled}
          onChange={e => onChange({ usesCompetitor: e.target.checked, competitorId: e.target.checked ? value.competitorId : null })}
        />
        Farklı bir firma ile çalışıyor
      </label>

      {value.usesCompetitor && (
        <div className="rounded-md border border-border p-2.5 space-y-2">
          <Label className="text-xs text-muted-foreground">Hangi firma? <span className="font-normal">(bilinmiyorsa boş bırakın)</span></Label>
          {value.competitorId ? (
            <div className="flex items-center justify-between gap-2 rounded-md border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-sm">
              <span className="inline-flex items-center gap-2 min-w-0">
                <Building2 className="h-4 w-4 shrink-0 text-rose-300" />
                <span className="font-medium truncate">{selected?.name ?? (isLoading ? 'Yükleniyor…' : 'Seçili firma')}</span>
                {selected && selected.customerCount > 0 && <span className="text-[11px] text-muted-foreground shrink-0">{selected.customerCount} müşteri</span>}
              </span>
              {!disabled && (
                <Button type="button" variant="ghost" size="sm" className="h-7 px-2" onClick={() => onChange({ usesCompetitor: true, competitorId: null })}>
                  <X className="h-3.5 w-3.5" /> Değiştir
                </Button>
              )}
            </div>
          ) : (
            <>
              <div className="relative">
                <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
                <Input value={search} onChange={e => setSearch(e.target.value)} disabled={disabled} placeholder="Firma adı yazın…" className="pl-8 h-9" maxLength={200}
                  onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); if (matches.length === 1 && exact) onChange({ usesCompetitor: true, competitorId: matches[0].id }); else if (canCreate) void createAndPick(); } }} />
              </div>
              <div className="max-h-44 overflow-y-auto rounded-md border border-border divide-y divide-border/50">
                {isLoading && <div className="px-3 py-2 text-xs text-muted-foreground">Yükleniyor…</div>}
                {!isLoading && matches.length === 0 && !canCreate && (
                  <div className="px-3 py-2 text-xs text-muted-foreground">{competitors.length === 0 ? 'Henüz rakip firma kaydı yok — adı yazıp ekleyin.' : 'Eşleşen firma yok.'}</div>
                )}
                {matches.map(c => (
                  <button key={c.id} type="button" disabled={disabled} onClick={() => { onChange({ usesCompetitor: true, competitorId: c.id }); setSearch(''); }}
                    className="w-full text-left px-3 py-2 text-sm hover:bg-accent flex items-center justify-between gap-2">
                    <span className="truncate inline-flex items-center gap-2"><Building2 className="h-3.5 w-3.5 text-muted-foreground" /> {c.name}</span>
                    {c.customerCount > 0 && <span className="text-[11px] text-muted-foreground shrink-0">{c.customerCount} müşteri</span>}
                  </button>
                ))}
                {canCreate && (
                  <button type="button" disabled={disabled || create.isPending} onClick={createAndPick}
                    className="w-full text-left px-3 py-2 text-sm hover:bg-accent flex items-center gap-2 text-primary">
                    {create.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Plus className="h-3.5 w-3.5" />}
                    "{search.trim()}" adıyla yeni firma ekle
                  </button>
                )}
              </div>
            </>
          )}
        </div>
      )}
    </div>
  );
}
