import { useEffect, useState, type FormEvent } from 'react'
import { CategoryOptions } from '../components/CategoryOptions'
import { FormField } from '../components/FormField'
import { SelectField } from '../components/SelectField'
import {
  createCategory,
  listCategories,
  moveCategory,
  removeCategory,
  renameCategory,
  type Category,
} from '../lib/api'
import { firstError, GENERIC_ERROR, toErrors, type Errors } from '../lib/errors'

/** Depth in the tree (0 = top level), following parentId. */
function depthOf(c: Category, byId: Map<string, Category>) {
  let depth = 0
  for (let p = c.parentId; p; p = byId.get(p)?.parentId ?? null) depth++
  return depth
}

/** Whether `c` is `ancestor` or lies below it. */
function isInSubtree(c: Category, ancestor: Category, byId: Map<string, Category>) {
  for (let cur: Category | undefined = c; cur; cur = cur.parentId ? byId.get(cur.parentId) : undefined)
    if (cur.id === ancestor.id) return true
  return false
}

/** Settings › Categories: the Family's shared Category tree. Built-in Contribution Categories are locked. */
export function CategoriesPage() {
  const [categories, setCategories] = useState<Category[] | null>(null)
  const [loadError, setLoadError] = useState(false)

  const load = (signal?: AbortSignal) =>
    listCategories(signal)
      .then(setCategories)
      .catch(() => {
        if (!signal?.aborted) setLoadError(true)
      })

  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [])

  const byId = new Map((categories ?? []).map(c => [c.id, c]))

  return (
    <>
      <h1>Categories</h1>
      <p>
        One Category tree for the whole Family. Every Income and Expense gets one Category; the built-in Contribution
        Categories are assigned automatically and can’t be changed.
      </p>
      {loadError && <p className="status status-bad">Couldn’t load the Categories.</p>}
      {categories === null && !loadError && <p>Loading…</p>}
      {categories !== null && (
        <>
          <NewCategoryForm categories={categories} onCreated={() => void load()} />
          <section className="card">
            <ul className="category-list">
              {categories.map(c => (
                <CategoryRow
                  key={c.id}
                  category={c}
                  depth={depthOf(c, byId)}
                  categories={categories}
                  byId={byId}
                  onChanged={() => void load()}
                />
              ))}
            </ul>
          </section>
        </>
      )}
    </>
  )
}

function NewCategoryForm({ categories, onCreated }: { categories: Category[]; onCreated: () => void }) {
  const [name, setName] = useState('')
  const [parentId, setParentId] = useState('')
  const [errors, setErrors] = useState<Errors>({})
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setErrors({})
    try {
      await createCategory(name, parentId || null)
      setName('')
      onCreated()
    } catch (err) {
      setErrors(toErrors(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="card form" onSubmit={submit}>
      <h2>New Category</h2>
      <FormField label="Name" name="name" required value={name} errors={errors.name} onChange={e => setName(e.target.value)} />
      <SelectField
        label="Under"
        name="parentId"
        value={parentId}
        errors={errors.parentId}
        onChange={e => setParentId(e.target.value)}
      >
        <option value="">Top level</option>
        <CategoryOptions categories={categories} exclude={c => c.isBuiltIn} />
      </SelectField>
      {errors.form && (
        <p className="form-error" role="alert">
          {errors.form[0]}
        </p>
      )}
      <div className="form-actions">
        <button type="submit" className="button-primary" disabled={busy}>
          Add Category
        </button>
      </div>
    </form>
  )
}

type Mode = 'view' | 'rename' | 'move' | 'remove'

function CategoryRow({
  category: c,
  depth,
  categories,
  byId,
  onChanged,
}: {
  category: Category
  depth: number
  categories: Category[]
  byId: Map<string, Category>
  onChanged: () => void
}) {
  const [mode, setMode] = useState<Mode>('view')
  const [name, setName] = useState(c.name)
  const [parentId, setParentId] = useState(c.parentId ?? '')
  // What happens to Transactions using a removed Category: '' = Uncategorised, otherwise the Category to move them to.
  const [moveTo, setMoveTo] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const hasChildren = categories.some(x => x.parentId === c.id)

  function open(next: Mode) {
    setName(c.name)
    setParentId(c.parentId ?? '')
    setMoveTo('')
    setError(null)
    setMode(next)
  }

  async function run(action: () => Promise<unknown>) {
    setBusy(true)
    setError(null)
    try {
      await action()
      setMode('view')
      onChanged()
    } catch (err) {
      setError(firstError(toErrors(err)) ?? GENERIC_ERROR)
    } finally {
      setBusy(false)
    }
  }

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (mode === 'rename') void run(() => renameCategory(c.id, name))
    if (mode === 'move') void run(() => moveCategory(c.id, parentId || null))
    if (mode === 'remove') void run(() => removeCategory(c.id, moveTo ? { moveTo } : { uncategorise: true }))
  }

  return (
    <li className="category-row" style={{ paddingLeft: `${depth * 20}px` }}>
      <div className="category-main">
        <span className="category-name">{c.name}</span>
        {c.isBuiltIn && <span className="category-badge">built-in</span>}
        {!c.isBuiltIn && mode === 'view' && (
          <div className="transaction-actions">
            <button type="button" className="button-link" onClick={() => open('rename')}>
              Rename
            </button>
            <button type="button" className="button-link" onClick={() => open('move')}>
              Move
            </button>
            <button type="button" className="button-link" onClick={() => open('remove')}>
              Remove
            </button>
          </div>
        )}
      </div>
      {mode !== 'view' && (
        <form className="category-edit" onSubmit={submit}>
          {mode === 'rename' && (
            <FormField label="Name" name="name" required value={name} onChange={e => setName(e.target.value)} />
          )}
          {mode === 'move' && (
            <SelectField label="Move under" name="parentId" value={parentId} onChange={e => setParentId(e.target.value)}>
              <option value="">Top level</option>
              <CategoryOptions categories={categories} exclude={x => x.isBuiltIn || isInSubtree(x, c, byId)} />
            </SelectField>
          )}
          {mode === 'remove' &&
            (hasChildren ? (
              <p>Move or remove its subcategories first.</p>
            ) : (
              <SelectField
                label="Transactions with this Category"
                name="moveTo"
                value={moveTo}
                onChange={e => setMoveTo(e.target.value)}
              >
                <option value="">Become Uncategorised</option>
                <CategoryOptions categories={categories} exclude={x => x.id === c.id} />
              </SelectField>
            ))}
          {error && (
            <p className="form-error" role="alert">
              {error}
            </p>
          )}
          <div className="form-actions">
            {!(mode === 'remove' && hasChildren) && (
              <button type="submit" className="button-primary" disabled={busy}>
                {mode === 'rename' ? 'Save' : mode === 'move' ? 'Move' : 'Remove'}
              </button>
            )}
            <button type="button" className="button-secondary" onClick={() => setMode('view')}>
              Cancel
            </button>
          </div>
        </form>
      )}
    </li>
  )
}
