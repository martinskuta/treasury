// Swiss number and date formatting (1'234.50, 20.11.2026).
// Hand-rolled on purpose: Intl 'de-CH' output differs between ICU versions (' vs ’), so it is not deterministic.

/**
 * Formats an amount with an apostrophe as thousands separator and two decimals: 1234.5 -> "1'234.50".
 * With `whole`, rounds to whole units without decimals (legends, the donut centre): 1842.6 -> "1'843".
 */
export function formatAmount(value: number, { whole = false }: { whole?: boolean } = {}): string {
  const fixed = Math.abs(value).toFixed(whole ? 0 : 2)
  const [int, frac] = fixed.split('.')
  const sign = value < 0 && Number(fixed) !== 0 ? '-' : ''
  const grouped = int.replace(/\B(?=(\d{3})+(?!\d))/g, "'")
  return frac === undefined ? `${sign}${grouped}` : `${sign}${grouped}.${frac}`
}

/** An amount split for the large amount display (see <Amount>): 1234.5 -> { sign: '', integer: "1'234", cents: '.50' }. */
export type AmountParts = { sign: '' | '+' | '−'; integer: string; cents: string }

/**
 * Splits an amount into sign, Swiss-formatted integer part and cents.
 * The sign is a typographic minus (U+2212) so it is as wide as a digit; with `signed`, positive amounts get '+'.
 * Amounts that round to zero carry no sign.
 */
export function amountParts(value: number, { signed = false }: { signed?: boolean } = {}): AmountParts {
  const unsigned = formatAmount(Math.abs(value))
  const dot = unsigned.lastIndexOf('.')
  const zero = Number(Math.abs(value).toFixed(2)) === 0
  const sign = zero ? '' : value < 0 ? '−' : signed ? '+' : ''
  return { sign, integer: unsigned.slice(0, dot), cents: unsigned.slice(dot) }
}

/** Short amount for chart axes: 12000 -> "12k", 12500 -> "12.5k", 800 -> "800". */
export function formatCompactAmount(value: number): string {
  if (Math.abs(value) < 1000) return String(Math.round(value))
  const k = value / 1000
  return `${Number.isInteger(k) ? k : k.toFixed(1)}k`
}

/**
 * Formats a date as dd.MM.yyyy.
 * Accepts a Date (local calendar date) or an ISO string from the API ("2026-11-20" or "2026-11-20T…").
 * ISO strings are not parsed with `new Date()`: "2026-11-20" would become UTC midnight and could shift a day.
 */
export function formatDate(value: Date | string): string {
  if (typeof value === 'string') {
    const [year, month, day] = value.slice(0, 10).split('-')
    return `${day}.${month}.${year}`
  }
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${pad(value.getDate())}.${pad(value.getMonth() + 1)}.${value.getFullYear()}`
}
