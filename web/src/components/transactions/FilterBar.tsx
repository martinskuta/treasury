import { useState, type FormEvent } from 'react'
import type { Account, Category, Owner, Tag } from '../../lib/api'
import { CategoryOptions } from '../CategoryOptions'
import { OwnerOptions } from '../OwnerSelect'
import { FILTER_KEYS, NOT_PAID_BACK, SORTS } from './shared'

/**
 * Search box plus Account, Category, Tag, Owner, paid back, date range and sort as pill fields; every change goes into the URL.
 * The pills carry no visible label: their first option says what they filter ("All Accounts"), and each has an
 * accessible name.
 */
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
  // On a phone the filters fold away under the search box unless some are applied; from 768px on they always show.
  const [open, setOpen] = useState(() => active > 0)

  function search(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const text = new FormData(e.currentTarget).get('q')
    onChange({ q: typeof text === 'string' ? text.trim() : '' })
  }

  return (
    <section className={`tx-filters${open ? ' open' : ''}`} aria-label="Filters">
      {/* Keyed by the URL's text so Clear / Back show what is applied; Enter applies the search. */}
      <form key={q} className="tx-search" role="search" onSubmit={search}>
        <input
          className="input"
          type="search"
          name="q"
          defaultValue={q}
          placeholder="Search"
          aria-label="Search (description, note, counterparty, …)"
        />
        <button type="submit" className="button-secondary button-small">
          Search
        </button>
      </form>
      <button
        type="button"
        className="button-secondary tx-filters-toggle"
        aria-expanded={open}
        aria-controls="tx-filter-fields"
        onClick={() => setOpen(!open)}
      >
        Filters{active > 0 ? ` · ${active}` : ''}
      </button>
      <div id="tx-filter-fields" className="tx-filter-fields">
        <select
          className="input"
          aria-label="Account"
          value={value('account')}
          onChange={e => onChange({ account: e.target.value })}
        >
          <option value="">All Accounts</option>
          {accounts.map(a => (
            <option key={a.id} value={a.id}>
              {a.name}
            </option>
          ))}
        </select>
        <select
          className="input"
          aria-label="Category"
          value={value('category')}
          onChange={e => onChange({ category: e.target.value })}
        >
          <option value="">All Categories</option>
          <option value="uncategorised">Uncategorised</option>
          <CategoryOptions categories={categories} />
        </select>
        {tags.length > 0 && (
          <select className="input" aria-label="Tag" value={value('tag')} onChange={e => onChange({ tag: e.target.value })}>
            <option value="">Any Tag</option>
            {tags.map(t => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
          </select>
        )}
        <div className="tx-dates">
          <label className="input tx-date">
            <span>From</span>
            <input type="date" name="from" value={value('from')} onChange={e => onChange({ from: e.target.value })} />
          </label>
          <label className="input tx-date">
            <span>To</span>
            <input type="date" name="to" value={value('to')} onChange={e => onChange({ to: e.target.value })} />
          </label>
        </div>
        <select className="input" aria-label="Owner" value={value('owner')} onChange={e => onChange({ owner: e.target.value })}>
          <option value="">Any Owner</option>
          <OwnerOptions owners={owners} />
        </select>
        <select
          className="input"
          aria-label="Paid back"
          value={value('paidBack')}
          onChange={e => onChange({ paidBack: e.target.value })}
        >
          <option value="">Paid back or not</option>
          <option value={NOT_PAID_BACK}>Not yet paid back</option>
        </select>
        <select
          className="input"
          aria-label="Sort"
          value={value('sort') || 'date-desc'}
          onChange={e => onChange({ sort: e.target.value === 'date-desc' ? '' : e.target.value })}
        >
          {SORTS.map(s => (
            <option key={s.value} value={s.value}>
              {s.label}
            </option>
          ))}
        </select>
      </div>
      {filtered && (
        <button type="button" className="button-link tx-filters-clear" onClick={onClear}>
          Clear filters
        </button>
      )}
    </section>
  )
}
