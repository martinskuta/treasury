import { useCallback, useEffect, useState } from 'react'
import { useSearchParams } from 'react-router'
import { BulkEditPanel } from '../components/transactions/BulkEditPanel'
import { FilterBar } from '../components/transactions/FilterBar'
import { NewTransactionForm } from '../components/transactions/NewTransactionForm'
import { FILTER_KEYS, filterFromParams, PAGE_SIZE, pageFromParams } from '../components/transactions/shared'
import { TransactionRow } from '../components/transactions/TransactionRow'
import {
  listAccounts,
  listCategories,
  listOwners,
  listTags,
  listTransactions,
  type Account,
  type Category,
  type Owner,
  type Tag,
  type Transaction,
} from '../lib/api'

/**
 * The Transactions the Member can see (theirs, the Family's and everything in their Accounts), filtered and paged;
 * add, edit, delete, and change Category / Tags / Owner of several selected Transactions at once.
 */
export function TransactionsPage() {
  const [params, setParams] = useSearchParams()
  const paramKey = params.toString()
  const [page, setPage] = useState<{ key: string; items: Transaction[]; total: number } | null>(null)
  const [accounts, setAccounts] = useState<Account[]>([])
  const [owners, setOwners] = useState<Owner[]>([])
  const [categories, setCategories] = useState<Category[]>([])
  const [tags, setTags] = useState<Tag[]>([])
  const [loadError, setLoadError] = useState(false)
  const [adding, setAdding] = useState(false)
  // Selection belongs to one list (filters + page): changing them drops it.
  const [selection, setSelection] = useState<{ key: string; ids: string[] }>({ key: '', ids: [] })
  const [bulkMessage, setBulkMessage] = useState<string | null>(null)

  const load = useCallback(
    (signal?: AbortSignal) =>
      Promise.all([
        listTransactions(signal, filterFromParams(new URLSearchParams(paramKey))),
        listAccounts(signal),
        listOwners(signal),
        listCategories(signal),
        listTags(signal),
      ])
        .then(([t, a, o, c, g]) => {
          setPage({ key: paramKey, ...t })
          setAccounts(a)
          setOwners(o)
          setCategories(c)
          setTags(g)
          setLoadError(false)
        })
        .catch(() => {
          if (!signal?.aborted) setLoadError(true)
        }),
    [paramKey],
  )

  // Reloads whenever the URL's filters, sort or page change (load depends on them).
  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [load])

  const transactions = page?.items ?? null
  const selectedIds = selection.key === paramKey ? selection.ids : []
  const setSelected = (ids: string[]) => setSelection({ key: paramKey, ids })
  const filtered = FILTER_KEYS.some(k => params.get(k))
  const pageNumber = pageFromParams(params)
  const pageCount = page ? Math.max(1, Math.ceil(page.total / PAGE_SIZE)) : 1

  /** Sets (or with '' removes) URL parameters; any filter change goes back to the first page. */
  const update = (changes: Record<string, string>) => {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) {
      if (value) next.set(key, value)
      else next.delete(key)
    }
    if (!('page' in changes)) next.delete('page')
    setParams(next)
    setBulkMessage(null)
  }

  // The Accounts listed are exactly the ones the Member can manage (own + Family).
  const manageable = new Set(accounts.map(a => a.id))

  return (
    <>
      <h1>Transactions</h1>
      {loadError && <p className="status status-bad">Couldn’t load the Transactions.</p>}
      {transactions === null && !loadError && <p>Loading…</p>}
      {adding ? (
        <NewTransactionForm
          accounts={accounts}
          owners={owners}
          categories={categories}
          tags={tags}
          onCancel={() => setAdding(false)}
          onCreated={() => {
            setAdding(false)
            void load()
          }}
        />
      ) : (
        <p>
          <button
            type="button"
            className="button-primary"
            disabled={accounts.length === 0}
            onClick={() => setAdding(true)}
          >
            New Transaction
          </button>
        </p>
      )}
      <FilterBar
        params={params}
        accounts={accounts}
        owners={owners}
        categories={categories}
        tags={tags}
        filtered={filtered}
        onChange={update}
        onClear={() => update(Object.fromEntries(FILTER_KEYS.map(k => [k, ''])))}
      />
      {bulkMessage && (
        <p className="status status-good" role="status">
          {bulkMessage}
        </p>
      )}
      {transactions !== null && page !== null && (
        <section className="card">
          {transactions.length === 0 ? (
            <p>{filtered ? 'No Transactions match these filters.' : 'No Transactions yet.'}</p>
          ) : (
            <>
              <div className="transaction-list-header">
                <label className="checkbox-field">
                  <input
                    type="checkbox"
                    checked={transactions.every(t => selectedIds.includes(t.id))}
                    onChange={e => setSelected(e.target.checked ? transactions.map(t => t.id) : [])}
                  />{' '}
                  Select all on this page
                </label>
                <span className="account-meta">
                  {page.total} {page.total === 1 ? 'Transaction' : 'Transactions'}
                </span>
              </div>
              <ul className="transaction-list">
                {transactions.map(t => (
                  <TransactionRow
                    key={t.id}
                    transaction={t}
                    all={transactions}
                    owners={owners}
                    categories={categories}
                    tags={tags}
                    canEditFacts={t.origin === 'manual' && t.accountId !== null && manageable.has(t.accountId)}
                    selected={selectedIds.includes(t.id)}
                    onSelect={checked =>
                      setSelected(checked ? [...selectedIds, t.id] : selectedIds.filter(id => id !== t.id))
                    }
                    onChanged={() => void load()}
                  />
                ))}
              </ul>
            </>
          )}
          {pageCount > 1 && (
            <nav className="pager" aria-label="Pages">
              <button
                type="button"
                className="button-secondary"
                disabled={pageNumber <= 1}
                onClick={() => update({ page: pageNumber - 1 > 1 ? String(pageNumber - 1) : '' })}
              >
                Previous
              </button>
              <span>
                Page {pageNumber} of {pageCount}
              </span>
              <button
                type="button"
                className="button-secondary"
                disabled={pageNumber >= pageCount}
                onClick={() => update({ page: String(pageNumber + 1) })}
              >
                Next
              </button>
            </nav>
          )}
        </section>
      )}
      {selectedIds.length > 0 && transactions !== null && (
        <BulkEditPanel
          selected={transactions.filter(t => selectedIds.includes(t.id))}
          owners={owners}
          categories={categories}
          tags={tags}
          onClear={() => setSelected([])}
          onDone={count => {
            setSelected([])
            setBulkMessage(`Changed ${count} ${count === 1 ? 'Transaction' : 'Transactions'}.`)
            void load()
          }}
        />
      )}
    </>
  )
}
