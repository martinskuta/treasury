import { useState, type FormEvent } from 'react'
import { linkTransactions, type Transaction } from '../../lib/api'
import type { Errors } from '../../lib/errors'
import { formatAmount, formatDate } from '../../lib/format'
import { SelectField } from '../SelectField'
import { HIDDEN_ACCOUNT, toTransactionErrors } from './shared'

const daysBetween = (a: string, b: string) => Math.abs(Date.parse(a) - Date.parse(b)) / 86_400_000

/** Possible other legs: unlinked, in another Account, opposite sign; closest date first. */
const linkCandidates = (t: Transaction, all: Transaction[]) =>
  all
    .filter(o => o.id !== t.id && !o.link && o.accountId !== t.accountId && Math.sign(o.amount) === -Math.sign(t.amount))
    .sort((a, b) => daysBetween(a.date, t.date) - daysBetween(b.date, t.date))

/** Pair this Transaction with another one by hand: a Transfer for the same Owner, a Contribution otherwise. */
export function LinkForm({
  transaction: t,
  all,
  onCancel,
  onLinked,
}: {
  transaction: Transaction
  all: Transaction[]
  onCancel: () => void
  onLinked: () => void
}) {
  const candidates = linkCandidates(t, all)
  const [otherId, setOtherId] = useState(candidates[0]?.id ?? '')
  const [errors, setErrors] = useState<Errors>({})
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setErrors({})
    try {
      await linkTransactions(t.id, otherId)
      onLinked()
    } catch (err) {
      setErrors(toTransactionErrors(err))
    } finally {
      setBusy(false)
    }
  }

  if (candidates.length === 0)
    return (
      <p className="account-meta">
        No unlinked Transaction with the opposite sign in another Account.{' '}
        <button type="button" className="button-link" onClick={onCancel}>
          Close
        </button>
      </p>
    )

  return (
    <form className="form transaction-link-form" onSubmit={submit}>
      <SelectField
        label="Pair with"
        name="transactionId"
        value={otherId}
        errors={errors.transactionId}
        onChange={e => setOtherId(e.target.value)}
      >
        {candidates.map(o => (
          <option key={o.id} value={o.id}>
            {formatDate(o.date)} · {o.displayName ?? o.description} · {o.accountName ?? HIDDEN_ACCOUNT} ·{' '}
            {formatAmount(o.amount)} {o.currency}
          </option>
        ))}
      </SelectField>
      {errors.form && (
        <p className="form-error" role="alert">
          {errors.form[0]}
        </p>
      )}
      <div className="form-actions">
        <button type="submit" className="button-primary" disabled={busy || !otherId}>
          Link
        </button>
        <button type="button" className="button-secondary" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </form>
  )
}
