/**
 * ISO-3166 alpha-2 → Türkçe ülke adı. Tarayıcının Intl.DisplayNames verisini kullanır (tüm ülkeler), birkaç
 * yaygın ad kısaltılmış biçimiyle ezilir; tanınmayan kod olduğu gibi döner. "unknown" → "Bilinmiyor".
 */
const OVERRIDES: Record<string, string> = {
  GB: 'Birleşik Krallık', US: 'ABD', AE: 'BAE', TR: 'Türkiye', KR: 'Güney Kore', CZ: 'Çekya',
};

let display: Intl.DisplayNames | null | undefined;

function displayNames(): Intl.DisplayNames | null {
  if (display !== undefined) return display;
  try { display = new Intl.DisplayNames(['tr'], { type: 'region' }); }
  catch { display = null; }
  return display;
}

export function countryName(code: string | null | undefined): string {
  if (!code) return '—';
  const c = code.trim().toUpperCase();
  if (!c || c === 'UNKNOWN' || c === 'ZZ') return 'Bilinmiyor';
  if (OVERRIDES[c]) return OVERRIDES[c];
  if (!/^[A-Z]{2}$/.test(c)) return code;
  try { return displayNames()?.of(c) ?? c; } catch { return c; }
}
