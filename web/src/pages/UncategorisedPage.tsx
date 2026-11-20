import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router'
import { CategoryOptions } from '../components/CategoryOptions'
import { OwnerSelect } from '../components/OwnerSelect'
import { RuleOffer } from '../components/RuleOffer'
import { HIDDEN_ACCOUNT } from '../components/transactions/shared'
import {
  listCategories,
  listOwners,
  listUncategorised,
  updateTransaction,
  type Category,
  type Owner,
  type Transaction,
} from '../lib/api'
import { formatAmount, formatDate } from '../lib/format'

/**
 * The Uncategorised queue: every visible Income and Expense without a Category; picking one categorises it.
 * Filter by the Transaction's Owner in the URL: ?owner=me|family|<memberId> (none = everyone's).
 */
export function UncategorisedPage() {
  const [params, setParams] = useSearchParams()
  const owner = params.get('owner') ?? ''
  const [loaded, setLoaded] = useState<{ owner: string; transactions: Transaction[] } | null>(null)
  const [categories, setCategories] = useState<Category[]>([])
  const [owners, setOwners] = useState<Owner[]>([])
  const [loadError, setLoadError] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    Promise.all([
      listUncategorised(controller.signal, owner || undefined),
      listCategories(controller.signal),
      listOwners(controller.signal),
    ])
      .then(([t, c, o]) => {
        setLoaded({ owner, transactions: t })
        setCategories(c)
        setOwners(o)
        setLoadError(false)
      })
      .catch(() => {
        if (!controller.signal.aborted) setLoadError(true)
      })
    return () => controller.abort()
  }, [owner])

  const transactions = loaded?.owner === owner ? loaded.transactions : null
  const done = (id: string) =>
    setLoaded(l => (l === null ? null : { ...l, transactions: l.transactions.filter(t => t.id !== id) }))

  return (
    <>
      <h1>Uncategorised</h1>
      <div className="report-controls">
        <OwnerSelect
          owners={owners}
          value={owner}
          before={
            <>
              <option value="">Everyone’s</option>
              <option value="me">Mine</option>
            </>
          }
          onChange={value => setParams(value ? { owner: value } : {})}
        />
      </div>
      {loadError && <p className="status status-bad">Couldn’t load the Uncategorised Transactions.</p>}
      {transactions === null && !loadError && <p>Loading…</p>}
      {transactions !== null && (
        <section className="card">
          {transactions.length === 0 ? (
            <p>
              {owner
                ? 'Nothing to review for this Owner.'
                : 'Nothing to review: every Income and Expense has a Category.'}
            </p>
          ) : (
            <ul className="transaction-list">
              {transactions.map(t => (
                <UncategorisedRow key={t.id} transaction={t} categories={categories} onCategorised={() => done(t.id)} />
              ))}
            </ul>
          )}
        </section>
      )}
    </>
  )
}

function UncategorisedRow({
  transaction: t,
  categories,
  onCategorised,
}: {
  transaction: Transaction
  categories: Category[]
  onCategorised: () => void
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [categorisedAs, setCategorisedAs] = useState<string | null>(null)

  async function categorise(categoryId: string) {
    if (!categoryId) return
    setBusy(true)
    setError(null)
    try {
      const updated = await updateTransaction(t.id, { categoryId })
      // Keep the row for a moment to offer a Rule built from this correction.
      setCategorisedAs(updated.category?.path ?? '')
    } catch {
      setError('Couldn’t set the Category, try again.')
      setBusy(false)
    }
  }

  if (categorisedAs !== null)
    return (
      <li className="transaction-row">
        <div className="transaction-date">{formatDate(t.date)}</div>
        <div className="transaction-main">
          <strong>{t.description}</strong>
          <div className="account-meta">Categorised as {categorisedAs}</div>
          <RuleOffer transactionId={t.id} onDismiss={onCategorised} />
        </div>
        <div className={`amount transaction-amount ${t.type}`}>
          {formatAmount(t.amount)} {t.currency}
        </div>
      </li>
    )

  return (
    <li className="transaction-row">
      <div className="transaction-date">{formatDate(t.date)}</div>
      <div className="transaction-main">
        <strong>{t.description}</strong>
        <div className="account-meta">
          {t.accountName ?? HIDDEN_ACCOUNT} · {t.owner.name}
          {t.note && <> · {t.note}</>}
        </div>
        <select
          className="category-picker"
          aria-label={`Category for ${t.description}`}
          value=""
          disabled={busy}
          onChange={e => void categorise(e.target.value)}
        >
          <option value="">Choose a Category…</option>
          <CategoryOptions categories={categories} />
        </select>
        {error && (
          <p className="form-error" role="alert">
            {error}
          </p>
        )}
      </div>
      <div className={`amount transaction-amount ${t.type}`}>
        {formatAmount(t.amount)} {t.currency}
      </div>
    </li>
  )
}
