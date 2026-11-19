// Swiss number and date formatting (1'234.50, 20.11.2026).
// Hand-rolled on purpose: Intl 'de-CH' output differs between ICU versions (' vs ’), so it is not deterministic.

/** Formats an amount with an apostrophe as thousands separator and two decimals: 1234.5 -> "1'234.50". */
export function formatAmount(value: number): string {
  const [int, frac] = Math.abs(value).toFixed(2).split('.')
  const sign = value < 0 && Number(`${int}.${frac}`) !== 0 ? '-' : ''
  return `${sign}${int.replace(/\B(?=(\d{3})+(?!\d))/g, "'")}.${frac}`
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
