import { useState, type FormEvent } from 'react'
import { bulkEditTransactions, bulkErrorKey, type BulkEdit, type Category, type Owner, type Tag, type Transaction } from '../../lib/api'
import type { Errors } from '../../lib/errors'
import { formatDate } from '../../lib/format'
import { ownerRefFromValue } from '../../lib/owners'
import { CategoryOptions } from '../CategoryOptions'
import { OwnerSelect } from '../OwnerSelect'
import { SelectField } from '../SelectField'
import { toTransactionErrors } from './shared'
import { TagPicker } from './TagPicker'

/**
 * Changes Category, Owner and/or Tags of the selected Transactions in one action (recorded as manual edits, exactly
 * like editing each one). All or nothing: when one of them can't take the change, nothing changes and it is named.
 */
export function BulkEditPanel({
  selected,
  owners,
  categories,
  tags,
  onClear,
  onDone,
}: {
  selected: Transaction[]
  owners: Owner[]
  categories: Category[]
  tags: Tag[]
  onClear: () => void
  onDone: (count: number) => void
}) {
  // "" = keep as it is.
  const [categoryId, setCategoryId] = useState('')
  const [owner, setOwner] = useState('')
  const [addTagIds, setAddTagIds] = useState<string[]>([])
  const [removeTagIds, setRemoveTagIds] = useState<string[]>([])
  const [errors, setErrors] = useState<Errors>({})
  const [busy, setBusy] = useState(false)

  const nothingChosen = !categoryId && !owner && addTagIds.length === 0 && removeTagIds.length === 0

  async function submit(e: FormEvent) {
    e.preventDefault()
    const edit: BulkEdit = { transactionIds: selected.map(t => t.id) }
    if (categoryId === 'uncategorised') edit.uncategorise = true
    else if (categoryId) edit.categoryId = categoryId
    if (owner) edit.owner = ownerRefFromValue(owner)
    if (addTagIds.length > 0) edit.addTagIds = addTagIds
    if (removeTagIds.length > 0) edit.removeTagIds = removeTagIds
    setBusy(true)
    setErrors({})
    try {
      const changed = await bulkEditTransactions(edit)
      onDone(changed.length)
    } catch (err) {
      setErrors(toTransactionErrors(err))
    } finally {
      setBusy(false)
    }
  }

  const refused = selected
    .map(t => ({ t, messages: errors[bulkErrorKey(t.id)] }))
    .filter(r => r.messages !== undefined)
  const generalError = errors.form ?? errors.transactionIds ?? errors.change

  return (
    <form className="ui-component tx-bulk" onSubmit={submit} aria-label="Change selected Transactions">
      <div className="tx-bulk-head">
        <strong>
          <span className="tx-bulk-count num">{selected.length}</span> selected
        </strong>
        <button type="button" className="button-link" onClick={onClear}>
          Clear selection
        </button>
      </div>
      <SelectField
        label="Category"
        name="bulkCategory"
        value={categoryId}
        errors={errors.categoryId}
        onChange={e => setCategoryId(e.target.value)}
      >
        <option value="">Keep as it is</option>
        <option value="uncategorised">Uncategorised</option>
        <CategoryOptions categories={categories} />
      </SelectField>
      <OwnerSelect
        name="bulkOwner"
        owners={owners}
        value={owner}
        errors={errors.owner}
        before={<option value="">Keep as it is</option>}
        onChange={setOwner}
      />
      {tags.length > 0 && (
        <details className="tx-bulk-tags" open={addTagIds.length + removeTagIds.length > 0}>
          <summary>Tags</summary>
          <TagPicker
            legend="Add Tags"
            tags={tags}
            selected={addTagIds}
            errors={errors.addTagIds}
            onChange={ids => {
              setAddTagIds(ids)
              setRemoveTagIds(removeTagIds.filter(id => !ids.includes(id)))
            }}
            hint="Added by hand to every selected Transaction."
          />
          <TagPicker
            legend="Remove Tags"
            tags={tags}
            selected={removeTagIds}
            errors={errors.removeTagIds}
            onChange={ids => {
              setRemoveTagIds(ids)
              setAddTagIds(addTagIds.filter(id => !ids.includes(id)))
            }}
            hint="Removed for good: date ranges won’t add them back."
          />
        </details>
      )}
      {refused.length > 0 && (
        <div className="form-error tx-bulk-error" role="alert">
          Nothing was changed. These can’t take the change:
          <ul>
            {refused.map(({ t, messages }) => (
              <li key={t.id}>
                {formatDate(t.date)} {t.displayName ?? t.description}: {messages![0]}
              </li>
            ))}
          </ul>
        </div>
      )}
      {generalError && (
        <p className="form-error tx-bulk-error" role="alert">
          {generalError[0]}
        </p>
      )}
      <div className="form-actions tx-bulk-actions">
        <button type="submit" className="button-primary" disabled={busy || nothingChosen}>
          Apply to {selected.length}
        </button>
      </div>
    </form>
  )
}
