/*
 * Building blocks of the Transaction rows shared by Transactions and Uncategorised (docs/design-guidelines.md,
 * "Transaction rows"; styles in styles/transactions.css):
 *
 *   <DayGroups items={transactions} grouped>{t => <li className={`tx ${kindClass(t)}`}>…</li>}</DayGroups>
 *                                  (kindClass / kindOf live in ./shared)
 *   <KindIcon transaction={t} />   round 38px icon tinted by kind (Income, Expense, Transfer, Uncategorised)
 *   <RowAmount transaction={t} />  signed amount in the kind's colour, original amount / CHF equivalent below
 */
import type { ReactNode } from 'react'
import type { Transaction } from '../../lib/api'
import { amountParts, formatAmount } from '../../lib/format'
import { formatDayLabel, groupByDay, kindOf, type TransactionKind } from './shared'

const KIND_LABELS: Record<TransactionKind, string> = {
  income: 'Income',
  expense: 'Expense',
  transfer: 'Transfer',
  uncategorised: 'Uncategorised Transaction',
}

const kindLabel = (t: Transaction) => (t.type === 'reimbursement' ? 'Reimbursement' : KIND_LABELS[kindOf(t)])

/** The first letter of the shown name on a round icon tinted by kind (the kind is also named for screen readers). */
export function KindIcon({ transaction: t }: { transaction: Transaction }) {
  const name = (t.displayName ?? t.description).trim()
  return (
    <span className="tx-icon" role="img" aria-label={kindLabel(t)} title={kindLabel(t)}>
      {name.charAt(0).toUpperCase() || '·'}
    </span>
  )
}

/**
 * The amount with its sign ("−142.35", "+9'850.00"); the currency code only when it isn't CHF. Below it, muted: what
 * a foreign-currency purchase cost in its own currency, and for non-CHF Accounts the CHF equivalent.
 */
export function RowAmount({ transaction: t }: { transaction: Transaction }) {
  const { sign, integer, cents } = amountParts(t.amount, { signed: true })
  return (
    <div className="tx-amount num">
      <span>
        {t.currency !== 'CHF' && <span className="tx-cur">{t.currency} </span>}
        {sign}
        {integer}
        {cents}
      </span>
      {t.originalAmount !== null && t.originalCurrency !== null && (
        <small title="Paid in the original currency">
          {t.originalCurrency} {formatAmount(Math.abs(t.originalAmount))}
        </small>
      )}
      {t.currency !== 'CHF' && (
        <small title="CHF equivalent at the rate of the Transaction's date">
          {t.chfAmount === null ? 'CHF rate pending' : `≈ CHF ${formatAmount(t.chfAmount)}`}
        </small>
      )}
    </div>
  )
}

/**
 * The rows under a small date header per day (when `grouped`; e.g. not when sorted by amount, where `children`
 * should show the date itself). Each day is a heading followed by its own list.
 */
export function DayGroups({
  items,
  grouped,
  children,
}: {
  items: Transaction[]
  grouped: boolean
  children: (t: Transaction) => ReactNode
}) {
  if (!grouped) return <ul className="tx-list">{items.map(children)}</ul>
  return (
    <>
      {groupByDay(items).map(group => (
        <section key={group.date} className="tx-day">
          <h2 className="tx-day-head">{formatDayLabel(group.date)}</h2>
          <ul className="tx-list">{group.items.map(children)}</ul>
        </section>
      ))}
    </>
  )
}
