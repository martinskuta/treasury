import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { useSearchParams } from 'react-router'
import { CategoryOptions } from '../components/CategoryOptions'
import { FormField } from '../components/FormField'
import { OwnerSelect } from '../components/OwnerSelect'
import { SelectField } from '../components/SelectField'
import {
  applyRules,
  createRule,
  deleteRule,
  listCategories,
  listOwners,
  listRules,
  listTags,
  reorderRules,
  suggestRule,
  updateRule,
  type Category,
  type Owner,
  type OwnerRef,
  type Rule,
  type RuleField,
  type RuleInput,
  type RuleMatch,
  type Tag,
} from '../lib/api'
import { useMe } from '../lib/auth'
import { toErrors, type Errors } from '../lib/errors'
import { ownerRefFromValue, ownerValue } from '../lib/owners'

/** Owner <select> value: "" (don't set an Owner), "family" or a Member id. */
const ruleOwnerValue = (owner: Owner | OwnerRef | null) => (owner === null ? '' : ownerValue(owner))
const ruleOwnerRef = (value: string): OwnerRef | null => (value === '' ? null : ownerRefFromValue(value))

const emptyRule: RuleInput = { field: 'description', match: 'contains', text: '', categoryId: null, tagIds: [], owner: null }

const toInput = (rule: Rule): RuleInput => ({
  field: rule.field,
  match: rule.match,
  text: rule.text,
  categoryId: rule.category?.id ?? null,
  tagIds: rule.tags.map(t => t.id),
  owner: rule.owner === null ? null : ownerRefFromValue(ownerValue(rule.owner)),
})

/** "Description contains “migros” → Food › Groceries · Tags: holiday · Owner: Family". */
function describe(rule: Rule) {
  const condition = `${rule.field === 'description' ? 'Description' : 'Counterparty'} ${rule.match} “${rule.text}”`
  const actions = [
    rule.category?.path,
    rule.tags.length > 0 ? `Tags: ${rule.tags.map(t => t.name).join(', ')}` : null,
    rule.owner ? `Owner: ${rule.owner.name}` : null,
  ].filter(Boolean)
  return `${condition} → ${actions.join(' · ') || 'nothing'}`
}

type Options = { categories: Category[]; tags: Tag[]; owners: Owner[] }

/** Settings › Rules: the Family's shared Rules in priority order, plus re-applying them to Uncategorised Transactions. */
export function RulesPage() {
  const [rules, setRules] = useState<Rule[] | null>(null)
  const [options, setOptions] = useState<Options>({ categories: [], tags: [], owners: [] })
  const [loadError, setLoadError] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [searchParams, setSearchParams] = useSearchParams()
  const fromTransaction = searchParams.get('fromTransaction')
  const [draft, setDraft] = useState<RuleInput | null>(null)

  const load = useCallback(
    (signal?: AbortSignal) =>
      Promise.all([listRules(signal), listCategories(signal), listTags(signal), listOwners(signal)])
        .then(([r, categories, tags, owners]) => {
          setRules(r)
          setOptions({ categories, tags, owners })
        })
        .catch(() => {
          if (!signal?.aborted) setLoadError(true)
        }),
    [],
  )

  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [load])

  // Coming from a corrected Transaction: pre-fill the new-Rule form with the suggested draft.
  useEffect(() => {
    if (!fromTransaction) return
    const controller = new AbortController()
    suggestRule(fromTransaction, controller.signal)
      .then(setDraft)
      .catch(() => {
        if (!controller.signal.aborted) setMessage('Couldn’t build a Rule from that Transaction.')
      })
    return () => controller.abort()
  }, [fromTransaction])

  async function move(index: number, by: -1 | 1) {
    if (!rules) return
    const ids = rules.map(r => r.id)
    ;[ids[index], ids[index + by]] = [ids[index + by], ids[index]]
    setBusy(true)
    try {
      setRules(await reorderRules(ids))
    } catch {
      setMessage('Couldn’t reorder the Rules (someone may have changed them).')
      void load()
    } finally {
      setBusy(false)
    }
  }

  async function apply() {
    setBusy(true)
    setMessage(null)
    try {
      const result = await applyRules()
      setMessage(
        result.categorised === 0
          ? `No Rule categorised any of the ${result.checked} Uncategorised Transactions.`
          : `Categorised ${result.categorised} of ${result.checked} Uncategorised Transactions.`,
      )
    } catch {
      setMessage('Couldn’t apply the Rules, try again.')
    } finally {
      setBusy(false)
    }
  }

  function draftDone() {
    setDraft(null)
    setSearchParams({})
    void load()
  }

  return (
    <>
      <h1>Rules</h1>
      <p>
        Rules categorise imported Transactions automatically. They run in order: the first matching Rule with a
        Category sets the Category, the first with an Owner sets the Owner, and the Tags of every matching Rule are
        added. Rules never change what a Member set by hand, never categorise Transfers or Contributions, and only
        reach Accounts their author (the Member who last saved them) can see.
      </p>
      {loadError && <p className="status status-bad">Couldn’t load the Rules.</p>}
      {rules === null && !loadError && <p>Loading…</p>}
      {message && (
        <p className="status" role="status">
          {message}
        </p>
      )}
      {rules !== null && (
        <>
          <section className="ui-component">
            <h2>{draft ? 'New Rule from your correction' : 'New Rule'}</h2>
            <RuleForm
              key={draft ? `draft-${fromTransaction}` : 'new'}
              initial={draft ?? emptyRule}
              options={options}
              submitLabel="Add Rule"
              onSubmit={createRule}
              onDone={draftDone}
              onCancel={draft ? draftDone : undefined}
              resetOnDone
            />
          </section>
          <section className="ui-component">
            <div className="form-actions">
              <button type="button" className="button-secondary" disabled={busy} onClick={() => void apply()}>
                Apply Rules to Uncategorised Transactions
              </button>
            </div>
            {rules.length === 0 ? (
              <p>No Rules yet.</p>
            ) : (
              <ol className="category-list rule-list">
                {rules.map((r, i) => (
                  <RuleRow
                    key={r.id}
                    rule={r}
                    options={options}
                    busy={busy}
                    onUp={i > 0 ? () => void move(i, -1) : undefined}
                    onDown={i < rules.length - 1 ? () => void move(i, 1) : undefined}
                    onChanged={() => void load()}
                  />
                ))}
              </ol>
            )}
          </section>
        </>
      )}
    </>
  )
}

function RuleRow({
  rule,
  options,
  busy,
  onUp,
  onDown,
  onChanged,
}: {
  rule: Rule
  options: Options
  busy: boolean
  onUp?: () => void
  onDown?: () => void
  onChanged: () => void
}) {
  const [editing, setEditing] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function remove() {
    if (!window.confirm(`Delete the Rule “${describe(rule)}”? Transactions it already categorised stay as they are.`))
      return
    setError(null)
    try {
      await deleteRule(rule.id)
      onChanged()
    } catch {
      setError('Couldn’t delete it.')
    }
  }

  return (
    <li className="category-row">
      <div className="category-main">
        <span className="category-name">{describe(rule)}</span>
        <span className="account-meta">
          by {rule.author.name}
          {!rule.active && ' · inactive: its author was deactivated (save it to take it over)'}
        </span>
        <button type="button" className="button-link" disabled={busy || !onUp} onClick={onUp} aria-label="Move up">
          ↑
        </button>
        <button type="button" className="button-link" disabled={busy || !onDown} onClick={onDown} aria-label="Move down">
          ↓
        </button>
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
          <RuleForm
            initial={toInput(rule)}
            options={options}
            extraOwner={rule.owner}
            author={rule.author}
            submitLabel="Save"
            onSubmit={input => updateRule(rule.id, input)}
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

function RuleForm({
  initial,
  options,
  extraOwner,
  author,
  submitLabel,
  onSubmit,
  onDone,
  onCancel,
  resetOnDone,
}: {
  initial: RuleInput
  options: Options
  /** The Rule's current Owner, kept selectable even if no longer offered (deactivated Member). */
  extraOwner?: Owner | null
  /** The saved Rule's author: saving makes the current Member the author, which changes the Accounts it reaches. */
  author?: Rule['author']
  submitLabel: string
  onSubmit: (input: RuleInput) => Promise<unknown>
  onDone: () => void
  onCancel?: () => void
  resetOnDone?: boolean
}) {
  const [field, setField] = useState<RuleField>(initial.field)
  const [match, setMatch] = useState<RuleMatch>(initial.match)
  const [text, setText] = useState(initial.text)
  const [categoryId, setCategoryId] = useState(initial.categoryId ?? '')
  const [tagIds, setTagIds] = useState(initial.tagIds)
  const [owner, setOwner] = useState(ruleOwnerValue(initial.owner))
  const [errors, setErrors] = useState<Errors>({})
  const [busy, setBusy] = useState(false)
  const me = useMe()

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setErrors({})
    try {
      await onSubmit({ field, match, text, categoryId: categoryId || null, tagIds, owner: ruleOwnerRef(owner) })
      if (resetOnDone) {
        setField(emptyRule.field)
        setMatch(emptyRule.match)
        setText('')
        setCategoryId('')
        setTagIds([])
        setOwner('')
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
      <SelectField
        label="When the"
        name="field"
        value={field}
        errors={errors.field}
        onChange={e => setField(e.target.value as RuleField)}
      >
        <option value="description">Description</option>
        <option value="counterparty">Counterparty</option>
      </SelectField>
      <SelectField
        label="Condition"
        name="match"
        value={match}
        errors={errors.match}
        onChange={e => setMatch(e.target.value as RuleMatch)}
      >
        <option value="contains">contains</option>
        <option value="equals">equals</option>
      </SelectField>
      <FormField
        label="Text (ignoring case)"
        name="text"
        required
        value={text}
        errors={errors.text}
        onChange={e => setText(e.target.value)}
      />
      <SelectField
        label="Set Category"
        name="categoryId"
        value={categoryId}
        errors={errors.categoryId}
        onChange={e => setCategoryId(e.target.value)}
      >
        <option value="">Don’t set a Category</option>
        <CategoryOptions categories={options.categories} />
      </SelectField>
      <fieldset className="checkbox-group">
        <legend>Add Tags</legend>
        {options.tags.length === 0 && <p className="account-meta">No Tags yet.</p>}
        {options.tags.map(t => (
          <label key={t.id} className="checkbox-field">
            <input
              type="checkbox"
              checked={tagIds.includes(t.id)}
              onChange={e => setTagIds(e.target.checked ? [...tagIds, t.id] : tagIds.filter(id => id !== t.id))}
            />{' '}
            {t.name}
          </label>
        ))}
        {errors.tagIds && <p className="form-error">{errors.tagIds[0]}</p>}
      </fieldset>
      <OwnerSelect
        label="Set Owner"
        owners={options.owners}
        current={extraOwner}
        value={owner}
        errors={errors.owner}
        before={<option value="">Don’t set an Owner</option>}
        onChange={setOwner}
      />
      {author && author.id !== me.id && (
        <p className="account-meta">
          Saving makes you this Rule’s author: it will then reach your Accounts and the Family’s instead of{' '}
          {author.name}’s.
        </p>
      )}
      {(errors.actions ?? errors.form) && (
        <p className="form-error" role="alert">
          {(errors.actions ?? errors.form)[0]}
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
