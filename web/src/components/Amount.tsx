/*
 * Large amount display for key amounts (net, totals): small muted currency code, Swiss number, de-emphasised cents,
 * tabular figures. For amounts in lists and tables use formatAmount() inside an element with .num / .amount.
 *
 *   <Amount value={net} signed tone="auto" size="l" />      // CHF +3'567.65 in Income green
 *   <Amount value={total} size="m" />                       // CHF 23'417.15 in ink
 *   <Amount value={sum} currency={null} size="s" />         // 74'880.00 without the code
 *
 * size: 's' 1.15rem, 'm' 1.6rem, 'l' 2.1rem; omit it and set font-size via className for anything else.
 */
import { amountParts } from '../lib/format'

export type AmountTone = 'auto' | 'income' | 'expense' | 'transfer' | 'none'

type Props = {
  value: number
  /** Currency code shown small before the number; null hides it. Default CHF. */
  currency?: string | null
  /** Show '+' on positive amounts (net values). Negative amounts always carry '−'. */
  signed?: boolean
  /** Text colour: 'auto' = Income green when positive, Expense red when negative. Default 'none' (ink). */
  tone?: AmountTone
  size?: 's' | 'm' | 'l'
  className?: string
}

const toneClass = { income: 'inc', expense: 'exp', transfer: 'tr', none: '' } as const

export function Amount({ value, currency = 'CHF', signed = false, tone = 'none', size, className }: Props) {
  const { sign, integer, cents } = amountParts(value, { signed })
  const rounded = Number(value.toFixed(2))
  const resolved = tone !== 'auto' ? tone : rounded > 0 ? 'income' : rounded < 0 ? 'expense' : 'none'
  const classes = ['amount-display', toneClass[resolved], size && `size-${size}`, className].filter(Boolean).join(' ')
  return (
    <span className={classes}>
      {currency && <span className="cur">{currency}</span>}
      {sign}
      {integer}
      <span className="dec">{cents}</span>
    </span>
  )
}
