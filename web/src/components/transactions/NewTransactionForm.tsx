import { useState, type FormEvent } from 'react'
import { createTransaction, type Account, type Category, type Owner, type Tag } from '../../lib/api'
import type { Errors } from '../../lib/errors'
import { ownerRefFromValue } from '../../lib/owners'
import { FormField } from '../FormField'
import { OwnerSelect } from '../OwnerSelect'
import { SelectField } from '../SelectField'
import { CategoryField, FactFields } from './FormParts'
import { parseAmount, today, toTransactionErrors } from './shared'
import { TagPicker } from './TagPicker'

/** A manual Transaction in one of the Accounts the Member manages. */
export function NewTransactionForm({
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
      setErrors(toTransactionErrors(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="ui-component form" onSubmit={submit}>
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
      <OwnerSelect
        owners={owners}
        value={owner}
        errors={errors.owner}
        before={<option value="">Account’s Owner{account ? ` (${account.owner.name})` : ''}</option>}
        onChange={setOwner}
      />
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
