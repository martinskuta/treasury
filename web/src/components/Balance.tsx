/*
 * An Account balance in a list: small muted currency code, Swiss number, Expense red when negative, "–" when the
 * value is missing (e.g. a total still waiting for an exchange rate).
 *
 *   <Balance value={account.balance} currency={account.currency} className="acct-amount" />   // EUR 1'234.50
 *   <Balance value={total} currency={null} />                                               // 1'234.50 without the code
 */
import { formatAmount } from '../lib/format'

type Props = {
  value: number | null
  /** Currency code shown small before the number; null hides it. */
  currency: string | null
  className?: string
  title?: string
}

export function Balance({ value, currency, className, title }: Props) {
  const negative = value !== null && Number(value.toFixed(2)) < 0
  const classes = ['balance num', negative && 'exp', className].filter(Boolean).join(' ')
  return (
    <span className={classes} title={title}>
      {currency && <span className="balance-cur">{currency}</span>}
      {value === null ? '–' : formatAmount(value)}
    </span>
  )
}
