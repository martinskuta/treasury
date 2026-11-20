import { useEffect, useState, type FormEvent } from 'react'
import { FormField } from '../components/FormField'
import {
  ApiError,
  createTag,
  deleteTag,
  listOwners,
  listTags,
  updateTag,
  type Owner,
  type OwnerRef,
  type Tag,
  type TagInput,
} from '../lib/api'
import { formatDate } from '../lib/format'

type Errors = Record<string, string[]>

const toErrors = (err: unknown): Errors =>
  err instanceof ApiError && err.status === 400 ? err.fieldErrors : { form: ['Something went wrong, try again.'] }

/** Checkbox value of an Owner: "family" or the Member id. */
const ownerKey = (o: Owner) => (o.kind === 'family' ? 'family' : (o.memberId ?? 'family'))
const ownerRef = (key: string): OwnerRef => (key === 'family' ? { kind: 'family' } : { kind: 'member', memberId: key })

/** "1.7.2026 – 14.7.2026 · Family, anna" or "No date range". */
function describeRange(tag: Tag) {
  if (!tag.dateFrom || !tag.dateTo) return 'No date range: added by hand only'
  const range = `${formatDate(tag.dateFrom)} – ${formatDate(tag.dateTo)}`
  return tag.owners.length === 0
    ? `${range} · every Owner`
    : `${range} · only ${tag.owners.map(o => o.name).join(', ')}`
}

/** Settings › Tags: the Family's shared Tags, with optional date range and Owner limit. */
export function TagsPage() {
  const [tags, setTags] = useState<Tag[] | null>(null)
  const [owners, setOwners] = useState<Owner[]>([])
  const [loadError, setLoadError] = useState(false)

  const load = (signal?: AbortSignal) =>
    Promise.all([listTags(signal), listOwners(signal)])
      .then(([t, o]) => {
        setTags(t)
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

  return (
    <>
      <h1>Tags</h1>
      <p>
        Tags group Transactions across Categories, for example a holiday. Give a Tag a date range to apply it
        automatically to every Transaction in that period (optionally only for some Owners). Removing it from a single
        Transaction sticks.
      </p>
      {loadError && <p className="status status-bad">Couldn’t load the Tags.</p>}
      {tags === null && !loadError && <p>Loading…</p>}
      {tags !== null && (
        <>
          <section className="card">
            <h2>New Tag</h2>
            <TagForm owners={owners} submitLabel="Add Tag" onSubmit={createTag} onDone={() => void load()} resetOnDone />
          </section>
          <section className="card">
            {tags.length === 0 ? (
              <p>No Tags yet.</p>
            ) : (
              <ul className="category-list">
                {tags.map(t => (
                  <TagRow key={t.id} tag={t} owners={owners} onChanged={() => void load()} />
                ))}
              </ul>
            )}
          </section>
        </>
      )}
    </>
  )
}

function TagRow({ tag, owners, onChanged }: { tag: Tag; owners: Owner[]; onChanged: () => void }) {
  const [editing, setEditing] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function remove() {
    if (!window.confirm(`Delete the Tag “${tag.name}”? It is taken off every Transaction.`)) return
    setError(null)
    try {
      await deleteTag(tag.id)
      onChanged()
    } catch {
      setError('Couldn’t delete it.')
    }
  }

  return (
    <li className="category-row">
      <div className="category-main">
        <span className="tag-chip">{tag.name}</span>
        <span className="account-meta">{describeRange(tag)}</span>
        <button type="button" className="button-link" onClick={() => setEditing(!editing)}>
          {editing ? 'Close' : 'Edit'}
        </button>
        <button type="button" className="button-link" onClick={() => void remove()}>
          Delete
        </button>
      </div>
      {error && (
        <p className="form-error" role="alert">
          {error}
        </p>
      )}
      {editing && (
        <div className="category-edit">
          <TagForm
            tag={tag}
            owners={owners}
            submitLabel="Save"
            onSubmit={input => updateTag(tag.id, input)}
            onDone={() => {
              setEditing(false)
              onChanged()
            }}
            onCancel={() => setEditing(false)}
          />
        </div>
      )}
    </li>
  )
}

function TagForm({
  tag,
  owners,
  submitLabel,
  onSubmit,
  onDone,
  onCancel,
  resetOnDone,
}: {
  tag?: Tag
  owners: Owner[]
  submitLabel: string
  onSubmit: (input: TagInput) => Promise<unknown>
  onDone: () => void
  onCancel?: () => void
  resetOnDone?: boolean
}) {
  const [name, setName] = useState(tag?.name ?? '')
  const [hasRange, setHasRange] = useState(Boolean(tag?.dateFrom))
  const [dateFrom, setDateFrom] = useState(tag?.dateFrom ?? '')
  const [dateTo, setDateTo] = useState(tag?.dateTo ?? '')
  const [limited, setLimited] = useState<string[]>(tag?.owners.map(ownerKey) ?? [])
  const [errors, setErrors] = useState<Errors>({})
  const [busy, setBusy] = useState(false)

  // Owners no longer offered (e.g. deactivated Members) stay listed while the Tag is limited to them.
  const offered = [...owners, ...(tag?.owners ?? []).filter(o => !owners.some(x => ownerKey(x) === ownerKey(o)))]

  function toggleOwner(key: string, on: boolean) {
    setLimited(on ? [...limited, key] : limited.filter(k => k !== key))
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setErrors({})
    try {
      await onSubmit({
        name,
        dateFrom: hasRange ? dateFrom || null : null,
        dateTo: hasRange ? dateTo || null : null,
        owners: hasRange ? limited.map(ownerRef) : [],
      })
      if (resetOnDone) {
        setName('')
        setHasRange(false)
        setDateFrom('')
        setDateTo('')
        setLimited([])
      }
      onDone()
    } catch (err) {
      setErrors(toErrors(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="form" onSubmit={submit}>
      <FormField label="Name" name="name" required value={name} errors={errors.name} onChange={e => setName(e.target.value)} />
      <label className="checkbox-field">
        <input type="checkbox" checked={hasRange} onChange={e => setHasRange(e.target.checked)} /> Apply automatically
        over a date range
      </label>
      {hasRange && (
        <>
          <FormField
            label="From"
            name="dateFrom"
            type="date"
            required
            value={dateFrom}
            errors={errors.dateFrom}
            onChange={e => setDateFrom(e.target.value)}
          />
          <FormField
            label="To"
            name="dateTo"
            type="date"
            required
            value={dateTo}
            errors={errors.dateTo}
            onChange={e => setDateTo(e.target.value)}
          />
          <fieldset className="checkbox-group">
            <legend>Only for these Owners (none ticked = every Owner)</legend>
            {offered.map(o => (
              <label key={ownerKey(o)} className="checkbox-field">
                <input
                  type="checkbox"
                  checked={limited.includes(ownerKey(o))}
                  onChange={e => toggleOwner(ownerKey(o), e.target.checked)}
                />{' '}
                {o.name}
              </label>
            ))}
            {errors.owners && <p className="form-error">{errors.owners[0]}</p>}
          </fieldset>
        </>
      )}
      {errors.form && (
        <p className="form-error" role="alert">
          {errors.form[0]}
        </p>
      )}
      <div className="form-actions">
        <button type="submit" className="button-primary" disabled={busy}>
          {submitLabel}
        </button>
        {onCancel && (
          <button type="button" className="button-secondary" onClick={onCancel}>
            Cancel
          </button>
        )}
      </div>
    </form>
  )
}
