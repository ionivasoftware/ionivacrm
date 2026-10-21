import type { ReactNode } from 'react';
import type { SiteLocale } from '@/api/siteBlog';

// Sitenin Markdown ALT KÜMESİ (sözleşme §4): `## ` / `### ` başlık, paragraf (boş satırla ayrılır), `- ` ve `1. `
// listeleri (devam satırı iki boşlukla), `> ` vurgu, satır içi **kalın**, *eğik*, [metin](adres). Başka bir şey
// desteklenmez; ön izleme de tam olarak bunu çizer. Hızlı kontroller Liftdesk doğrulayıcısının (§4) aynasıdır —
// kaydetmeden önce uyarı verir; nihai karar sunucuda (400 errors[]).

const LINK_RX = /\[([^\]]+)\]\(([^)\s]+)\)/g;
const LIST_RX = /^(\s*)([-*]|\d+\.)\s+/;

export function mdLines(body: string): string[] { return body.replace(/\r\n/g, '\n').split('\n'); }

export function plainText(line: string): string {
  let t = line.trim();
  if (t.startsWith('#')) t = t.replace(/^#+/, '').trim();
  else if (t.startsWith('> ')) t = t.slice(2);
  t = t.replace(LIST_RX, '');
  t = t.replace(LINK_RX, '$1');
  t = t.replace(/\*\*|\*/g, '');
  return t;
}

/** Kelime sayısı — Liftdesk ile aynı mantık (boşlukla ayrılmış parçalar, işaretleme atılır). */
export function countWords(body: string): number {
  return mdLines(body).reduce((n, l) => n + plainText(l).split(/\s+/).filter(Boolean).length, 0);
}

export function forbiddenMarkup(line: string): string | null {
  if (line.startsWith('# ')) return 'H1 başlık (# )';
  if (line.includes('![')) return 'görsel (![)';
  if (line.includes('```')) return 'kod bloğu (```)';
  if (line.includes('|')) return 'tablo / dikey çizgi (|)';
  if (line.includes('<')) return 'HTML (<)';
  return null;
}

function isFaqHeading(text: string, faqHeading: string): boolean {
  const t = text.trim().toLocaleLowerCase('tr');
  return t === faqHeading.trim().toLocaleLowerCase('tr') || t.includes('sık sorulan') || t.includes('frequently asked');
}

export interface QuickCheck { code: string; message: string }

const TERMS: Record<string, string[]> = {
  'en-GB-IE': ['LOLER', 'RIDDOR', 'HSE', 'United Kingdom', 'UK'],
  'en-GB-GB': ['S.I. No. 299', 'HSA', 'Eircode', 'Ireland'],
  'en-global': ['LOLER', 'HSA'],
};

/** Sözleşme §4'ün istemci aynası: alan altı uyarılar. Kod adları Liftdesk ile aynı. */
export function quickChecks(v: { title: string; description: string; slug: string; bodyMarkdown: string; publishDate: string }, locale: SiteLocale | undefined): QuickCheck[] {
  const out: QuickCheck[] = [];
  if (!v.title.trim()) out.push({ code: 'title_required', message: 'Başlık boş olamaz.' });
  const dl = v.description.trim().length;
  if (dl < 120 || dl > 160) out.push({ code: 'description_length', message: `Açıklama 120–160 karakter olmalı (şu an ${dl}).` });
  if (!/^[a-z0-9-]+$/.test(v.slug.trim())) out.push({ code: 'slug_format', message: 'Slug yalnız a-z, 0-9 ve - içerebilir.' });
  if (!/^\d{4}-\d{2}-\d{2}$/.test(v.publishDate.trim())) out.push({ code: 'publish_date', message: 'Yayın tarihi yyyy-MM-dd olmalı.' });

  const lines = mdLines(v.bodyMarkdown);
  const bad = lines.map((l, i) => ({ i, f: forbiddenMarkup(l) })).filter(x => x.f);
  if (bad.length) out.push({ code: 'forbidden_markup', message: `Alt küme dışı işaretleme: ${bad.slice(0, 3).map(b => `${b.f} (satır ${b.i + 1})`).join(', ')}.` });
  const words = countWords(v.bodyMarkdown);
  if (words < 450 || words > 1000) out.push({ code: 'word_count', message: `Gövde 450–1000 kelime olmalı (şu an ${words}).` });
  const h2 = lines.filter(l => l.startsWith('## '));
  if (h2.length < 2) out.push({ code: 'heading_count', message: 'En az iki "## " başlık gerekli.' });

  const faqHeading = locale?.faqHeading ?? (locale?.language === 'tr' ? 'Sık sorulan sorular' : 'Frequently asked questions');
  const start = lines.findIndex(l => l.startsWith('## ') && isFaqHeading(l.replace(/^#+/, ''), faqHeading));
  if (start < 0) out.push({ code: 'faq_missing', message: `SSS bölümü yok ("## ${faqHeading}").` });
  else {
    let questions = 0, unanswered = 0, answered = true;
    for (let i = start + 1; i < lines.length; i++) {
      const l = lines[i];
      if (l.startsWith('## ')) break;
      if (l.startsWith('### ')) { if (questions > 0 && !answered) unanswered++; questions++; answered = false; continue; }
      if (questions > 0 && l.trim()) answered = true;
    }
    if (questions > 0 && !answered) unanswered++;
    if (questions !== 3) out.push({ code: 'faq_count', message: `SSS altında tam 3 "### " soru olmalı (şu an ${questions}).` });
    if (unanswered > 0) out.push({ code: 'faq_answer', message: `${unanswered} SSS sorusunun altında cevap paragrafı yok.` });
  }

  const links = [...v.bodyMarkdown.matchAll(LINK_RX)].map(m => m[2]);
  if (locale) {
    const badPrefix = links.filter(h => h.startsWith('/') && !h.startsWith(locale.home) && !h.startsWith(locale.blogPath));
    if (badPrefix.length) out.push({ code: 'link_prefix', message: `İç bağlantı ${locale.home} ile başlamalı: ${badPrefix[0]}` });
  }
  const external = links.filter(h => /^https?:\/\//i.test(h)).length;
  if (external > 2) out.push({ code: 'external_links', message: `En fazla 2 dış bağlantı (şu an ${external}).` });
  if (links.some(h => h.startsWith('mailto:'))) out.push({ code: 'external_links', message: 'mailto: bağlantısı kullanılamaz.' });

  if (locale) {
    const text = `${v.title}\n${v.description}\n${v.bodyMarkdown}`;
    const terms: string[] = [];
    if (locale.language === 'tr' || locale.spelling === 'en-GB') terms.push('elevator');
    if (locale.country === 'IE') terms.push(...TERMS['en-GB-IE']);
    if (locale.country === 'GB') terms.push(...TERMS['en-GB-GB']);
    if (locale.language === 'en' && !locale.country) terms.push(...TERMS['en-global']);
    const hit = terms.find(t => new RegExp(`(^|[^A-Za-z])${t.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}([^A-Za-z]|$)`, 'i').test(text));
    if (hit) out.push({ code: 'forbidden_term', message: `Bu yerel ayarda "${hit}" kullanılmaz.` });
  }
  return out;
}

// ── Ön izleme ────────────────────────────────────────────────────────────────

function inline(text: string, keyPrefix: string): ReactNode[] {
  const parts: ReactNode[] = [];
  const rx = /(\*\*[^*]+\*\*|\*[^*]+\*|\[[^\]]+\]\([^)\s]+\))/g;
  let last = 0; let m: RegExpExecArray | null; let k = 0;
  while ((m = rx.exec(text)) !== null) {
    if (m.index > last) parts.push(text.slice(last, m.index));
    const tok = m[0];
    if (tok.startsWith('**')) parts.push(<strong key={`${keyPrefix}-${k++}`}>{tok.slice(2, -2)}</strong>);
    else if (tok.startsWith('*')) parts.push(<em key={`${keyPrefix}-${k++}`}>{tok.slice(1, -1)}</em>);
    else {
      const lm = /^\[([^\]]+)\]\(([^)\s]+)\)$/.exec(tok);
      if (lm) parts.push(<a key={`${keyPrefix}-${k++}`} href={lm[2]} target="_blank" rel="noopener noreferrer" className="text-primary underline underline-offset-2">{lm[1]}</a>);
      else parts.push(tok);
    }
    last = m.index + tok.length;
  }
  if (last < text.length) parts.push(text.slice(last));
  return parts;
}

type Block =
  | { kind: 'h2' | 'h3' | 'p' | 'quote'; text: string }
  | { kind: 'ul' | 'ol'; items: string[] }
  | { kind: 'bad'; text: string; why: string };

export function parseBlocks(body: string): Block[] {
  const lines = mdLines(body);
  const blocks: Block[] = [];
  let para: string[] = [];
  let list: { kind: 'ul' | 'ol'; items: string[] } | null = null;
  const flushPara = () => { if (para.length) { blocks.push({ kind: 'p', text: para.join(' ') }); para = []; } };
  const flushList = () => { if (list) { blocks.push(list); list = null; } };
  for (const raw of lines) {
    const why = forbiddenMarkup(raw);
    if (why) { flushPara(); flushList(); blocks.push({ kind: 'bad', text: raw, why }); continue; }
    if (!raw.trim()) { flushPara(); flushList(); continue; }
    if (raw.startsWith('## ')) { flushPara(); flushList(); blocks.push({ kind: 'h2', text: raw.slice(3).trim() }); continue; }
    if (raw.startsWith('### ')) { flushPara(); flushList(); blocks.push({ kind: 'h3', text: raw.slice(4).trim() }); continue; }
    if (raw.startsWith('> ')) { flushPara(); flushList(); blocks.push({ kind: 'quote', text: raw.slice(2).trim() }); continue; }
    const lm = LIST_RX.exec(raw);
    if (lm && !lm[1]) {
      flushPara();
      const kind: 'ul' | 'ol' = /\d/.test(lm[2]) ? 'ol' : 'ul';
      if (!list || list.kind !== kind) { flushList(); list = { kind, items: [] }; }
      list.items.push(raw.replace(LIST_RX, '').trim());
      continue;
    }
    if (list && /^\s{2,}\S/.test(raw)) { list.items[list.items.length - 1] += ` ${raw.trim()}`; continue; }
    flushList();
    para.push(raw.trim());
  }
  flushPara(); flushList();
  return blocks;
}

export function MarkdownPreview({ body, className }: { body: string; className?: string }) {
  const blocks = parseBlocks(body);
  return (
    <div className={className}>
      {blocks.map((b, i) => {
        switch (b.kind) {
          case 'h2': return <h2 key={i} className="text-lg font-semibold text-foreground mt-5 mb-2 first:mt-0">{inline(b.text, `h${i}`)}</h2>;
          case 'h3': return <h3 key={i} className="text-base font-semibold text-foreground mt-4 mb-1.5">{inline(b.text, `h${i}`)}</h3>;
          case 'quote': return <blockquote key={i} className="border-l-2 border-primary/60 pl-3 my-3 text-muted-foreground italic">{inline(b.text, `q${i}`)}</blockquote>;
          case 'ul': return <ul key={i} className="list-disc pl-5 my-2 space-y-1">{b.items.map((it, j) => <li key={j}>{inline(it, `l${i}-${j}`)}</li>)}</ul>;
          case 'ol': return <ol key={i} className="list-decimal pl-5 my-2 space-y-1">{b.items.map((it, j) => <li key={j}>{inline(it, `l${i}-${j}`)}</li>)}</ol>;
          case 'bad': return <div key={i} className="my-2 rounded border border-red-500/40 bg-red-500/5 px-2 py-1 text-xs text-red-300 font-mono whitespace-pre-wrap" title={`Desteklenmez: ${b.why}`}>{b.text}</div>;
          default: return <p key={i} className="my-2 leading-relaxed">{inline(b.text, `p${i}`)}</p>;
        }
      })}
    </div>
  );
}
