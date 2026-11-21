import { Fragment, useEffect, useState, type ReactNode } from 'react'
import { useSearchParams } from 'react-router'
import { CategoryOptions } from '../components/CategoryOptions'
import { OwnerOptions } from '../components/OwnerSelect'
import { RuleOffer } from '../components/RuleOffer'
import { DayGroups, KindIcon, RowAmount } from '../components/transactions/RowParts'
import { HIDDEN_ACCOUNT, kindClass } from '../components/transactions/shared'
import {
  listCategories,
  listOwners,
  listUncategorised,
  updateTransaction,
  type Category,
  type Owner,
  type Transaction,
} from '../lib/api'
import '../styles/transactions.css'

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
      <div className="page-header">
        <h1>Uncategorised</h1>
        <select
          className="input tx-owner-filter"
          aria-label="Owner"
          value={owner}
          onChange={e => setParams(e.target.value ? { owner: e.target.value } : {})}
        >
          <option value="">Everyone’s</option>
          <option value="me">Mine</option>
          <OwnerOptions owners={owners} />
        </select>
      </div>
      {loadError && <p className="status status-bad">Couldn’t load the Uncategorised Transactions.</p>}
      {transactions === null && !loadError && <p className="page-loading">Loading…</p>}
      {transactions !== null && (
        <>
          {transactions.length > 0 && (
            <div className="tx-summary">
              <span>
                <b className="num">{transactions.length}</b> to review: pick a Category for each
              </span>
            </div>
          )}
          <section className="ui-component tx-box" aria-label="Uncategorised Transactions">
            {transactions.length === 0 ? (
              <p className="empty">
                {owner
                  ? 'Nothing to review for this Owner.'
                  : 'Nothing to review: every Income and Expense has a Category.'}
              </p>
            ) : (
              <DayGroups items={transactions} grouped>
                {t => (
                  <UncategorisedRow
                    key={t.id}
                    transaction={t}
                    categories={categories}
                    onCategorised={() => done(t.id)}
                  />
                )}
              </DayGroups>
            )}
          </section>
        </>
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

  const details: ReactNode[] = [
    t.counterparty,
    categorisedAs === null ? <span className="tx-uncat">Uncategorised</span> : `Categorised as ${categorisedAs}`,
    `${t.accountName ?? HIDDEN_ACCOUNT} · ${t.owner.name}`,
    t.note,
  ].filter(Boolean)

  return (
    <li className={`tx ${categorisedAs === null ? kindClass(t) : `tx-${t.type}`}`}>
      <KindIcon transaction={t} />
      <div className="tx-main">
        <div className="tx-name">
          <span className="tx-name-text">{t.displayName ?? t.description}</span>
        </div>
        <div className="tx-sub">
          {details.map((d, i) => (
            <Fragment key={i}>
              {i > 0 && ' · '}
              {d}
            </Fragment>
          ))}
        </div>
      </div>
      {categorisedAs === null && (
        <select
          className="input tx-category-picker"
          aria-label={`Category for ${t.displayName ?? t.description}`}
          value=""
          disabled={busy}
          onChange={e => void categorise(e.target.value)}
        >
          <option value="">Choose a Category…</option>
          <CategoryOptions categories={categories} />
        </select>
      )}
      <RowAmount transaction={t} />
      {(categorisedAs !== null || error) && (
        <div className="tx-extra">
          {categorisedAs !== null && <RuleOffer transactionId={t.id} onDismiss={onCategorised} />}
          {error && (
            <p className="form-error" role="alert">
              {error}
            </p>
          )}
        </div>
      )}
    </li>
  )
}
