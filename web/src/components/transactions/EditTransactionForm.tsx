import { useState, type FormEvent } from 'react'
import { updateTransaction, type Category, type Owner, type Tag, type Transaction, type TransactionChanges } from '../../lib/api'
import type { Errors } from '../../lib/errors'
import { formatAmount, formatDate } from '../../lib/format'
import { ownerRefFromValue, ownerValue } from '../../lib/owners'
import { FormField } from '../FormField'
import { OwnerSelect } from '../OwnerSelect'
import { CategoryField, FactFields } from './FormParts'
import { parseAmount, toTransactionErrors } from './shared'
import { TagPicker } from './TagPicker'

/** Edit one Transaction: classification always, facts only for manual ones in Accounts the Member manages. */
export function EditTransactionForm({
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
  /** categorySet: the Member chose a Category by hand (we then offer a Rule built from it). */
  onSaved: (categorySet: boolean) => void
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
      onSaved(changes.categoryId !== undefined)
    } catch (err) {
      setErrors(toTransactionErrors(err))
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
      <OwnerSelect owners={owners} current={t.owner} value={owner} errors={errors.owner} onChange={setOwner} />
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
