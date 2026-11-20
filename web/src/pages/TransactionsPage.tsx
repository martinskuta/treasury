import { useEffect, useState, type FormEvent } from 'react'
import { FormField } from '../components/FormField'
import { SelectField } from '../components/SelectField'
import {
  ApiError,
  createTransaction,
  deleteTransaction,
  listAccounts,
  listOwners,
  listTransactions,
  updateTransaction,
  type Account,
  type Owner,
  type OwnerRef,
  type Transaction,
  type TransactionChanges,
} from '../lib/api'
import { formatAmount, formatDate } from '../lib/format'

type Errors = Record<string, string[]>

const toErrors = (err: unknown): Errors =>
  err instanceof ApiError && err.status === 400
    ? err.fieldErrors
    : err instanceof ApiError && err.status === 403
      ? { form: ['Only the Members who manage this Account can change that.'] }
      : { form: ['Something went wrong, try again.'] }

/** Owner <select> value: "family", a Member id, or "" (= the Account's Owner, new Transactions only). */
const ownerValue = (owner: Owner) => (owner.kind === 'family' ? 'family' : (owner.memberId ?? 'family'))
const ownerRefFromValue = (value: string): OwnerRef =>
  value === 'family' ? { kind: 'family' } : { kind: 'member', memberId: value }

/** "1'234.50" or "-12.5" -> number; null when empty; NaN when not a number. */
const parseAmount = (text: string) => (text.trim() === '' ? null : Number(text.replace(/['\s]/g, '')))

const today = () => {
  const d = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

/** The Transactions the Member can see (theirs, the Family's and everything in their Accounts); add, edit, delete. */
export function TransactionsPage() {
  const [transactions, setTransactions] = useState<Transaction[] | null>(null)
  const [accounts, setAccounts] = useState<Account[]>([])
  const [owners, setOwners] = useState<Owner[]>([])
  const [loadError, setLoadError] = useState(false)
  const [adding, setAdding] = useState(false)

  const load = (signal?: AbortSignal) =>
    Promise.all([listTransactions(signal), listAccounts(signal), listOwners(signal)])
      .then(([t, a, o]) => {
        setTransactions(t)
        setAccounts(a)
        setOwners(o)
      })
      .catch(() => {
        if (!signal?.aborted) setLoadError(true)
      })

  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [])

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
      {transactions !== null && (
        <section className="card">
          {transactions.length === 0 ? (
            <p>No Transactions yet.</p>
          ) : (
            <ul className="transaction-list">
              {transactions.map(t => (
                <TransactionRow
                  key={t.id}
                  transaction={t}
                  owners={owners}
                  canEditFacts={t.origin === 'manual' && manageable.has(t.accountId)}
                  onChanged={() => void load()}
                />
              ))}
            </ul>
          )}
        </section>
      )}
    </>
  )
}

function TransactionRow({
  transaction: t,
  owners,
  canEditFacts,
  onChanged,
}: {
  transaction: Transaction
  owners: Owner[]
  canEditFacts: boolean
  onChanged: () => void
}) {
  const [editing, setEditing] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function remove() {
    if (!window.confirm(`Delete “${t.description}”?`)) return
    setError(null)
    try {
      await deleteTransaction(t.id)
      onChanged()
    } catch (err) {
      setError(toErrors(err).form?.[0] ?? 'Couldn’t delete it.')
    }
  }

  if (editing)
    return (
      <li className="transaction-row">
        <EditTransactionForm
          transaction={t}
          owners={owners}
          canEditFacts={canEditFacts}
          onCancel={() => setEditing(false)}
          onSaved={() => {
            setEditing(false)
            onChanged()
          }}
        />
      </li>
    )

  return (
    <li className="transaction-row">
      <div className="transaction-date">{formatDate(t.date)}</div>
      <div className="transaction-main">
        <strong>{t.description}</strong>
        <div className="account-meta">
          {t.accountName} · {t.owner.name}
          {t.note && <> · {t.note}</>}
        </div>
        <div className="transaction-actions">
          <button type="button" className="button-link" onClick={() => setEditing(true)}>
            Edit
          </button>
          {canEditFacts && (
            <button type="button" className="button-link" onClick={() => void remove()}>
              Delete
            </button>
          )}
        </div>
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

function NewTransactionForm({
  accounts,
  owners,
  onCancel,
  onCreated,
}: {
  accounts: Account[]
  owners: Owner[]
  onCancel: () => void
  onCreated: () => void
}) {
  const [accountId, setAccountId] = useState(accounts[0]?.id ?? '')
  const [date, setDate] = useState(today())
  const [amount, setAmount] = useState('')
  const [description, setDescription] = useState('')
  const [note, setNote] = useState('')
  const [owner, setOwner] = useState('')
  const [errors, setErrors] = useState<Errors>({})
  const [busy, setBusy] = useState(false)

  const account = accounts.find(a => a.id === accountId)

  async function submit(e: FormEvent) {
    e.preventDefault()
    const parsed = parseAmount(amount)
    if (parsed !== null && Number.isNaN(parsed)) {
      setErrors({ amount: ['Enter a number, e.g. -12.50 for an Expense or 1234.50 for Income'] })
      return
    }
    setBusy(true)
    setErrors({})
    try {
      await createTransaction({
        accountId,
        date: date || null,
        amount: parsed,
        description,
        note: note.trim() || null,
        owner: owner ? ownerRefFromValue(owner) : undefined,
      })
      onCreated()
    } catch (err) {
      setErrors(toErrors(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="card form" onSubmit={submit}>
      <h2>New Transaction</h2>
      <SelectField
        label="Account"
        name="accountId"
        required
        value={accountId}
        errors={errors.accountId}
        onChange={e => setAccountId(e.target.value)}
      >
        {accounts.map(a => (
          <option key={a.id} value={a.id}>
            {a.name} ({a.currency})
          </option>
        ))}
      </SelectField>
      <FactFields
        date={date}
        amount={amount}
        description={description}
        currency={account?.currency}
        errors={errors}
        onDate={setDate}
        onAmount={setAmount}
        onDescription={setDescription}
      />
      <FormField label="Note (optional)" name="note" value={note} errors={errors.note} onChange={e => setNote(e.target.value)} />
      <SelectField label="Owner" name="owner" value={owner} errors={errors.owner} onChange={e => setOwner(e.target.value)}>
        <option value="">Account’s Owner{account ? ` (${account.owner.name})` : ''}</option>
        <OwnerOptions owners={owners} />
      </SelectField>
      {errors.form && (
        <p className="form-error" role="alert">
          {errors.form[0]}
        </p>
      )}
      <div className="form-actions">
        <button type="submit" className="button-primary" disabled={busy}>
          Add Transaction
        </button>
        <button type="button" className="button-secondary" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </form>
  )
}

function EditTransactionForm({
  transaction: t,
  owners,
  canEditFacts,
  onCancel,
  onSaved,
}: {
  transaction: Transaction
  owners: Owner[]
  canEditFacts: boolean
  onCancel: () => void
  onSaved: () => void
}) {
  const [date, setDate] = useState(t.date)
  const [amount, setAmount] = useState(String(t.amount))
  const [description, setDescription] = useState(t.description)
  const [note, setNote] = useState(t.note ?? '')
  const [owner, setOwner] = useState(ownerValue(t.owner))
  const [errors, setErrors] = useState<Errors>({})
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    // Send only what changed: an unchanged Owner must not count as "set by hand".
    const changes: TransactionChanges = {}
    if (canEditFacts) {
      const parsed = parseAmount(amount)
      if (parsed === null || Number.isNaN(parsed)) {
        setErrors({ amount: ['Enter a number, e.g. -12.50 for an Expense or 1234.50 for Income'] })
        return
      }
      if (date !== t.date) changes.date = date
      if (parsed !== t.amount) changes.amount = parsed
      if (description !== t.description) changes.description = description
    }
    if (note !== (t.note ?? '')) changes.note = note
    if (owner !== ownerValue(t.owner)) changes.owner = ownerRefFromValue(owner)
    setBusy(true)
    setErrors({})
    try {
      await updateTransaction(t.id, changes)
      onSaved()
    } catch (err) {
      setErrors(toErrors(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="form transaction-edit" onSubmit={submit}>
      {canEditFacts ? (
        <FactFields
          date={date}
          amount={amount}
          description={description}
          currency={t.currency}
          errors={errors}
          onDate={setDate}
          onAmount={setAmount}
          onDescription={setDescription}
        />
      ) : (
        <p>
          <strong>{t.description}</strong> · {formatDate(t.date)} · {formatAmount(t.amount)} {t.currency}
        </p>
      )}
      <FormField label="Note (optional)" name="note" value={note} errors={errors.note} onChange={e => setNote(e.target.value)} />
      <SelectField label="Owner" name="owner" value={owner} errors={errors.owner} onChange={e => setOwner(e.target.value)}>
        <OwnerOptions owners={owners} current={t.owner} />
      </SelectField>
      {(errors.form ?? errors.transaction) && (
        <p className="form-error" role="alert">
          {(errors.form ?? errors.transaction)[0]}
        </p>
      )}
      <div className="form-actions">
        <button type="submit" className="button-primary" disabled={busy}>
          Save
        </button>
        <button type="button" className="button-secondary" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </form>
  )
}

function FactFields(props: {
  date: string
  amount: string
  description: string
  currency: string | undefined
  errors: Errors
  onDate: (v: string) => void
  onAmount: (v: string) => void
  onDescription: (v: string) => void
}) {
  return (
    <>
      <FormField
        label="Date"
        name="date"
        type="date"
        required
        value={props.date}
        errors={props.errors.date}
        onChange={e => props.onDate(e.target.value)}
      />
      <FormField
        label={`Amount${props.currency ? ` (${props.currency})` : ''}: negative for an Expense`}
        name="amount"
        required
        value={props.amount}
        errors={props.errors.amount}
        onChange={e => props.onAmount(e.target.value)}
      />
      <FormField
        label="Description"
        name="description"
        required
        value={props.description}
        errors={props.errors.description}
        onChange={e => props.onDescription(e.target.value)}
      />
    </>
  )
}

/** The offered Owners; `current` is added when it isn't offered any more (e.g. a deactivated Member). */
function OwnerOptions({ owners, current }: { owners: Owner[]; current?: Owner }) {
  const offered = current && !owners.some(o => ownerValue(o) === ownerValue(current)) ? [...owners, current] : owners
  return (
    <>
      {offered.map(o => (
        <option key={ownerValue(o)} value={ownerValue(o)}>
          {o.name}
        </option>
      ))}
    </>
  )
}
