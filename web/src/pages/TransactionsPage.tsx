import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { CategoryOptions } from '../components/CategoryOptions'
import { FormField } from '../components/FormField'
import { SelectField } from '../components/SelectField'
import {
  ApiError,
  createTransaction,
  deleteTransaction,
  listAccounts,
  listCategories,
  listOwners,
  linkTransactions,
  listTags,
  listTransactions,
  sourceFileContentUrl,
  unlinkTransaction,
  updateTransaction,
  type Account,
  type Category,
  type Owner,
  type OwnerRef,
  type Tag,
  type TagRef,
  type Transaction,
  type TransactionChanges,
  type TransactionLink,
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
  const [categories, setCategories] = useState<Category[]>([])
  const [tags, setTags] = useState<Tag[]>([])
  const [tagFilter, setTagFilter] = useState('')
  const [loadError, setLoadError] = useState(false)
  const [adding, setAdding] = useState(false)

  const load = useCallback(
    (signal?: AbortSignal) =>
      Promise.all([
        listTransactions(signal, { tagId: tagFilter || undefined }),
        listAccounts(signal),
        listOwners(signal),
        listCategories(signal),
        listTags(signal),
      ])
        .then(([t, a, o, c, g]) => {
          setTransactions(t)
          setAccounts(a)
          setOwners(o)
          setCategories(c)
          setTags(g)
        })
        .catch(() => {
          if (!signal?.aborted) setLoadError(true)
        }),
    [tagFilter],
  )

  // Reloads when the Tag filter changes (load depends on it).
  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [load])

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
      {tags.length > 0 && (
        <div className="transaction-filters">
          <SelectField label="Tag" name="tagFilter" value={tagFilter} onChange={e => setTagFilter(e.target.value)}>
            <option value="">Any</option>
            {tags.map(t => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
          </SelectField>
        </div>
      )}
      {transactions !== null && (
        <section className="card">
          {transactions.length === 0 ? (
            <p>{tagFilter ? 'No Transactions with this Tag.' : 'No Transactions yet.'}</p>
          ) : (
            <ul className="transaction-list">
              {transactions.map(t => (
                <TransactionRow
                  key={t.id}
                  transaction={t}
                  all={transactions}
                  owners={owners}
                  categories={categories}
                  tags={tags}
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
  all,
  owners,
  categories,
  tags,
  canEditFacts,
  onChanged,
}: {
  transaction: Transaction
  all: Transaction[]
  owners: Owner[]
  categories: Category[]
  tags: Tag[]
  canEditFacts: boolean
  onChanged: () => void
}) {
  const [editing, setEditing] = useState(false)
  const [linking, setLinking] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function unlink() {
    setError(null)
    try {
      await unlinkTransaction(t.id)
      onChanged()
    } catch {
      setError('Couldn’t unlink it.')
    }
  }

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
          categories={categories}
          tags={tags}
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
        <strong>{t.displayName ?? t.description}</strong>
        {(t.displayName || t.counterparty) && (
          <div className="account-meta transaction-original">
            {t.displayName && <>{t.description}</>}
            {t.displayName && t.counterparty && ' · '}
            {t.counterparty}
          </div>
        )}
        <div className="account-meta">
          {t.type === 'transfer' ? 'Transfer' : (t.category?.path ?? 'Uncategorised')} · {t.accountName} ·{' '}
          {t.owner.name}
          {t.link && (
            <>
              {' · '}
              <LinkLabel link={t.link} all={all} />
            </>
          )}
          {t.note && <> · {t.note}</>}
          {t.sourceFile && (
            <>
              {' · '}
              <a href={sourceFileContentUrl(t.sourceFile.id)} title="Download the Source file">
                {t.sourceFile.fileName}
              </a>
            </>
          )}
        </div>
        <TagChips tags={t.tags} />
        <div className="transaction-actions">
          <button type="button" className="button-link" onClick={() => setEditing(true)}>
            Edit
          </button>
          {canEditFacts && (
            <button type="button" className="button-link" onClick={() => void remove()}>
              Delete
            </button>
          )}
          {t.link ? (
            <button type="button" className="button-link" onClick={() => void unlink()}>
              Unlink
            </button>
          ) : (
            !linking && (
              <button type="button" className="button-link" onClick={() => setLinking(true)}>
                Link…
              </button>
            )
          )}
        </div>
        {linking && !t.link && (
          <LinkForm
            transaction={t}
            all={all}
            onCancel={() => setLinking(false)}
            onLinked={() => {
              setLinking(false)
              onChanged()
            }}
          />
        )}
        {error && (
          <p className="form-error" role="alert">
            {error}
          </p>
        )}
      </div>
      <div className={`amount transaction-amount ${t.type}`}>
        {formatAmount(t.amount)} {t.currency}
        <OriginalAmount transaction={t} />
        {t.currency !== 'CHF' && (
          <div className="transaction-chf" title="CHF equivalent at the rate of the Transaction's date">
            {t.chfAmount === null ? 'CHF rate pending' : `≈ ${formatAmount(t.chfAmount)} CHF`}
          </div>
        )}
      </div>
    </li>
  )
}

/** The Tags on a Transaction; automatically applied ones (date range, Rule) are drawn dashed. */
function TagChips({ tags }: { tags: TagRef[] }) {
  if (tags.length === 0) return null
  return (
    <div className="tag-list">
      {tags.map(tag => {
        const auto = !tag.sources.includes('manual')
        return (
          <span
            key={tag.id}
            className={`tag-chip${auto ? ' auto' : ''}`}
            title={auto ? 'Applied automatically' : 'Added by hand'}
          >
            {tag.name}
          </span>
        )
      })}
    </div>
  )
}

/** "Transfer with Savings"; the other leg may sit in an Account the viewer can't see. */
function LinkLabel({ link, all }: { link: TransactionLink; all: Transaction[] }) {
  const other = all.find(o => o.id === link.transactionId)
  return (
    <span className="transaction-link">
      {link.kind === 'transfer' ? 'Transfer' : 'Contribution'} with {other ? other.accountName : 'another Member’s Account'}
    </span>
  )
}

const daysBetween = (a: string, b: string) => Math.abs(Date.parse(a) - Date.parse(b)) / 86_400_000

/** Possible other legs: unlinked, in another Account, opposite sign; closest date first. */
const linkCandidates = (t: Transaction, all: Transaction[]) =>
  all
    .filter(o => o.id !== t.id && !o.link && o.accountId !== t.accountId && Math.sign(o.amount) === -Math.sign(t.amount))
    .sort((a, b) => daysBetween(a.date, t.date) - daysBetween(b.date, t.date))

/** Pair this Transaction with another one by hand: a Transfer for the same Owner, a Contribution otherwise. */
function LinkForm({
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
      setErrors(toErrors(err))
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
            {formatDate(o.date)} · {o.displayName ?? o.description} · {o.accountName} · {formatAmount(o.amount)}{' '}
            {o.currency}
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

/** A foreign-currency purchase: what was paid, next to what the Account was charged. */
function OriginalAmount({ transaction: t }: { transaction: Transaction }) {
  if (t.originalAmount === null || t.originalCurrency === null) return null
  return (
    <div className="account-meta transaction-original-amount">
      {formatAmount(t.originalAmount)} {t.originalCurrency}
    </div>
  )
}

function NewTransactionForm({
  accounts,
  owners,
  categories,
  tags,
  onCancel,
  onCreated,
}: {
  accounts: Account[]
  owners: Owner[]
  categories: Category[]
  tags: Tag[]
  onCancel: () => void
  onCreated: () => void
}) {
  const [accountId, setAccountId] = useState(accounts[0]?.id ?? '')
  const [date, setDate] = useState(today())
  const [amount, setAmount] = useState('')
  const [description, setDescription] = useState('')
  const [note, setNote] = useState('')
  const [owner, setOwner] = useState('')
  const [categoryId, setCategoryId] = useState('')
  const [tagIds, setTagIds] = useState<string[]>([])
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
        categoryId: categoryId || undefined,
        tagIds: tagIds.length > 0 ? tagIds : undefined,
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
      <CategoryField categories={categories} value={categoryId} errors={errors.categoryId} onChange={setCategoryId} />
      <TagPicker
        tags={tags}
        selected={tagIds}
        errors={errors.tagIds}
        onChange={setTagIds}
        hint="Date-range Tags are added automatically."
      />
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
  categories,
  tags,
  canEditFacts,
  onCancel,
  onSaved,
}: {
  transaction: Transaction
  owners: Owner[]
  categories: Category[]
  tags: Tag[]
  canEditFacts: boolean
  onCancel: () => void
  onSaved: () => void
}) {
  const [date, setDate] = useState(t.date)
  const [amount, setAmount] = useState(String(t.amount))
  const [description, setDescription] = useState(t.description)
  const [note, setNote] = useState(t.note ?? '')
  const [displayName, setDisplayName] = useState(t.displayName ?? '')
  const [owner, setOwner] = useState(ownerValue(t.owner))
  const [categoryId, setCategoryId] = useState(t.category?.id ?? '')
  const [tagIds, setTagIds] = useState(t.tags.map(tag => tag.id))
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
    if (displayName.trim() !== (t.displayName ?? '')) changes.displayName = displayName.trim()
    if (owner !== ownerValue(t.owner)) changes.owner = ownerRefFromValue(owner)
    if (categoryId !== (t.category?.id ?? '')) {
      if (categoryId) changes.categoryId = categoryId
      else changes.uncategorise = true
    }
    const current = t.tags.map(tag => tag.id)
    const added = tagIds.filter(id => !current.includes(id))
    const removed = current.filter(id => !tagIds.includes(id))
    if (added.length > 0) changes.addTagIds = added
    if (removed.length > 0) changes.removeTagIds = removed
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
          {t.originalAmount !== null && t.originalCurrency !== null && (
            <>
              {' '}
              ({formatAmount(t.originalAmount)} {t.originalCurrency})
            </>
          )}
          <br />
          <span className="account-meta">
            {t.origin === 'imported'
              ? 'Imported from the bank: date, amount and description can’t be changed.'
              : 'Only the Members who manage this Account can change its date, amount and description.'}
          </span>
        </p>
      )}
      <FormField
        label="Display name (optional, shown instead of the description)"
        name="displayName"
        value={displayName}
        placeholder={t.description}
        errors={errors.displayName}
        onChange={e => setDisplayName(e.target.value)}
      />
      <FormField label="Note (optional)" name="note" value={note} errors={errors.note} onChange={e => setNote(e.target.value)} />
      <SelectField label="Owner" name="owner" value={owner} errors={errors.owner} onChange={e => setOwner(e.target.value)}>
        <OwnerOptions owners={owners} current={t.owner} />
      </SelectField>
      {t.type === 'transfer' ? (
        <p className="account-meta">A Transfer has no Category. Unlink it to make it an Income or Expense.</p>
      ) : (
        <CategoryField categories={categories} value={categoryId} errors={errors.categoryId} onChange={setCategoryId} />
      )}
      <TagPicker
        tags={tags}
        selected={tagIds}
        errors={errors.addTagIds ?? errors.removeTagIds}
        onChange={setTagIds}
        hint="Unticking an automatically applied Tag removes it for good from this Transaction."
      />
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

/** Category picker; "" = Uncategorised. */
function CategoryField(props: {
  categories: Category[]
  value: string
  errors: string[] | undefined
  onChange: (v: string) => void
}) {
  return (
    <SelectField
      label="Category"
      name="categoryId"
      value={props.value}
      errors={props.errors}
      onChange={e => props.onChange(e.target.value)}
    >
      <option value="">Uncategorised</option>
      <CategoryOptions categories={props.categories} />
    </SelectField>
  )
}

/** Tag checkboxes. */
function TagPicker(props: {
  tags: Tag[]
  selected: string[]
  errors: string[] | undefined
  hint: string
  onChange: (ids: string[]) => void
}) {
  if (props.tags.length === 0) return null
  return (
    <fieldset className="checkbox-group">
      <legend>Tags</legend>
      <span className="account-meta">{props.hint}</span>
      {props.tags.map(tag => (
        <label key={tag.id} className="checkbox-field">
          <input
            type="checkbox"
            checked={props.selected.includes(tag.id)}
            onChange={e =>
              props.onChange(e.target.checked ? [...props.selected, tag.id] : props.selected.filter(id => id !== tag.id))
            }
          />{' '}
          {tag.name}
        </label>
      ))}
      {props.errors && <p className="form-error">{props.errors[0]}</p>}
    </fieldset>
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
