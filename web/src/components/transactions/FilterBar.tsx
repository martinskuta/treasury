import { useState, type FormEvent } from 'react'
import type { Account, Category, Owner, Tag } from '../../lib/api'
import { CategoryOptions } from '../CategoryOptions'
import { FormField } from '../FormField'
import { OwnerSelect } from '../OwnerSelect'
import { SelectField } from '../SelectField'
import { FILTER_KEYS, SORTS } from './shared'

/** Search box plus Account, Owner, Category, Tag, date range and sort; every change goes into the URL. */
export function FilterBar({
  params,
  accounts,
  owners,
  categories,
  tags,
  filtered,
  onChange,
  onClear,
}: {
  params: URLSearchParams
  accounts: Account[]
  owners: Owner[]
  categories: Category[]
  tags: Tag[]
  filtered: boolean
  onChange: (changes: Record<string, string>) => void
  onClear: () => void
}) {
  const value = (key: string) => params.get(key) ?? ''
  const q = value('q')
  const active = FILTER_KEYS.filter(k => k !== 'q' && params.get(k)).length
  // On a phone the filters fold away under the search box unless some are applied; on wider screens they show.
  const [open, setOpen] = useState(() => active > 0 || window.matchMedia('(min-width: 768px)').matches)

  function search(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const text = new FormData(e.currentTarget).get('q')
    onChange({ q: typeof text === 'string' ? text.trim() : '' })
  }

  return (
    <section className="transaction-filters" aria-label="Filters">
      {/* Keyed by the URL's text so Clear / Back show what is applied; Enter applies the search. */}
      <form key={q} className="transaction-search" role="search" onSubmit={search}>
        <FormField label="Search" name="q" type="search" defaultValue={q} placeholder="Description, note, …" />
        <button type="submit" className="button-secondary">
          Search
        </button>
      </form>
      <details className="transaction-filter-details" open={open} onToggle={e => setOpen(e.currentTarget.open)}>
        <summary>Filters{active > 0 ? ` · ${active} applied` : ''}</summary>
        <div className="transaction-filter-grid">
          <SelectField label="Account" name="account" value={value('account')} onChange={e => onChange({ account: e.target.value })}>
            <option value="">All Accounts</option>
            {accounts.map(a => (
              <option key={a.id} value={a.id}>
                {a.name}
              </option>
            ))}
          </SelectField>
          <OwnerSelect
            owners={owners}
            value={value('owner')}
            before={<option value="">Any Owner</option>}
            onChange={owner => onChange({ owner })}
          />
          <SelectField
            label="Category"
            name="category"
            value={value('category')}
            onChange={e => onChange({ category: e.target.value })}
          >
            <option value="">Any Category</option>
            <option value="uncategorised">Uncategorised</option>
            <CategoryOptions categories={categories} />
          </SelectField>
          {tags.length > 0 && (
            <SelectField label="Tag" name="tag" value={value('tag')} onChange={e => onChange({ tag: e.target.value })}>
              <option value="">Any Tag</option>
              {tags.map(t => (
                <option key={t.id} value={t.id}>
                  {t.name}
                </option>
              ))}
            </SelectField>
          )}
          <FormField label="From" name="from" type="date" value={value('from')} onChange={e => onChange({ from: e.target.value })} />
          <FormField label="To" name="to" type="date" value={value('to')} onChange={e => onChange({ to: e.target.value })} />
          <SelectField label="Sort" name="sort" value={value('sort') || 'date-desc'} onChange={e => onChange({ sort: e.target.value === 'date-desc' ? '' : e.target.value })}>
            {SORTS.map(s => (
              <option key={s.value} value={s.value}>
                {s.label}
              </option>
            ))}
          </SelectField>
        </div>
      </details>
      {filtered && (
        <button type="button" className="button-link transaction-filters-clear" onClick={onClear}>
          Clear filters
        </button>
      )}
    </section>
  )
}
