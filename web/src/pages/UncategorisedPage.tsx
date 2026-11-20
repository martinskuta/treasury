import { useEffect, useState } from 'react'
import { CategoryOptions } from '../components/CategoryOptions'
import { listCategories, listUncategorised, updateTransaction, type Category, type Transaction } from '../lib/api'
import { formatAmount, formatDate } from '../lib/format'

/** The Uncategorised queue: every visible Income and Expense without a Category; picking one categorises it. */
export function UncategorisedPage() {
  const [transactions, setTransactions] = useState<Transaction[] | null>(null)
  const [categories, setCategories] = useState<Category[]>([])
  const [loadError, setLoadError] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    Promise.all([listUncategorised(controller.signal), listCategories(controller.signal)])
      .then(([t, c]) => {
        setTransactions(t)
        setCategories(c)
      })
      .catch(() => {
        if (!controller.signal.aborted) setLoadError(true)
      })
    return () => controller.abort()
  }, [])

  const done = (id: string) => setTransactions(list => list?.filter(t => t.id !== id) ?? null)

  return (
    <>
      <h1>Uncategorised</h1>
      {loadError && <p className="status status-bad">Couldn’t load the Uncategorised Transactions.</p>}
      {transactions === null && !loadError && <p>Loading…</p>}
      {transactions !== null && (
        <section className="card">
          {transactions.length === 0 ? (
            <p>Nothing to review: every Income and Expense has a Category.</p>
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

  async function categorise(categoryId: string) {
    if (!categoryId) return
    setBusy(true)
    setError(null)
    try {
      await updateTransaction(t.id, { categoryId })
      onCategorised()
    } catch {
      setError('Couldn’t set the Category, try again.')
      setBusy(false)
    }
  }

  return (
    <li className="transaction-row">
      <div className="transaction-date">{formatDate(t.date)}</div>
      <div className="transaction-main">
        <strong>{t.description}</strong>
        <div className="account-meta">
          {t.accountName} · {t.owner.name}
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
