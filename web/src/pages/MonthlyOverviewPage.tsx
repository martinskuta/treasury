import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router'
import {
  getMonthlyReport,
  type CategoryTotal,
  type MonthlyReport,
  type ReportOwner,
  type ReportSection,
} from '../lib/api'
import { formatAmount } from '../lib/format'

const monthNames = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
]

function currentMonth(): string {
  const now = new Date()
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`
}

function shiftMonth(month: string, by: number): string {
  const [year, m] = month.split('-').map(Number)
  const d = new Date(year, m - 1 + by, 1)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
}

function monthLabel(month: string): string {
  const [year, m] = month.split('-').map(Number)
  return `${monthNames[m - 1]} ${year}`
}

type Side = 'expense' | 'income'

/**
 * Monthly overview: Income versus Expense in CHF and a breakdown by Category that drills down through the tree.
 * Month, Owner and side live in the URL (?month=2026-11&owner=family&side=income) so Back works and links can be shared.
 */
export function MonthlyOverviewPage() {
  const [params, setParams] = useSearchParams()
  const month = /^\d{4}-\d{2}$/.test(params.get('month') ?? '') ? params.get('month')! : currentMonth()
  const owner: ReportOwner = params.get('owner') === 'family' ? 'family' : 'me'
  const side: Side = params.get('side') === 'income' ? 'income' : 'expense'

  const [report, setReport] = useState<MonthlyReport | null>(null)
  // The month|owner whose load failed (derived per request, so switching month clears the error).
  const [failedKey, setFailedKey] = useState<string | null>(null)
  const [drill, setDrill] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    getMonthlyReport(month, owner, controller.signal)
      .then(setReport)
      .catch(() => {
        if (!controller.signal.aborted) setFailedKey(`${month}|${owner}`)
      })
    return () => controller.abort()
  }, [month, owner])

  const loadError = failedKey === `${month}|${owner}`

  const update = (changes: Record<string, string>) => {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) next.set(key, value)
    setParams(next)
    setDrill(null)
  }

  const shown = report !== null && report.month === month && report.owner === owner ? report : null

  return (
    <>
      <h1>Monthly overview</h1>
      <div className="report-controls">
        <div className="segmented" role="group" aria-label="Owner">
          <button type="button" aria-pressed={owner === 'me'} onClick={() => update({ owner: 'me' })}>
            Mine
          </button>
          <button type="button" aria-pressed={owner === 'family'} onClick={() => update({ owner: 'family' })}>
            Family
          </button>
        </div>
        <div className="month-switcher">
          <button
            type="button"
            className="button-secondary"
            aria-label="Previous month"
            onClick={() => update({ month: shiftMonth(month, -1) })}
          >
            ‹
          </button>
          <span className="month-label">{monthLabel(month)}</span>
          <button
            type="button"
            className="button-secondary"
            aria-label="Next month"
            onClick={() => update({ month: shiftMonth(month, 1) })}
          >
            ›
          </button>
        </div>
      </div>

      {loadError && <p className="status status-bad">Couldn’t load the overview.</p>}
      {shown === null && !loadError && <p>Loading…</p>}
      {shown !== null && (
        <>
          <Totals report={shown} />
          <section className="card">
            <div className="segmented" role="group" aria-label="Breakdown">
              <button type="button" aria-pressed={side === 'expense'} onClick={() => update({ side: 'expense' })}>
                Expenses by Category
              </button>
              <button type="button" aria-pressed={side === 'income'} onClick={() => update({ side: 'income' })}>
                Income by Category
              </button>
            </div>
            <Breakdown section={shown[side]} side={side} drill={drill} onDrill={setDrill} />
          </section>
        </>
      )}
    </>
  )
}

function Totals({ report }: { report: MonthlyReport }) {
  const pending = report.income.pendingConversion + report.expense.pendingConversion
  return (
    <section className="card">
      <dl className="report-totals">
        <div>
          <dt>Income</dt>
          <dd className="amount income">{formatAmount(report.income.total)} CHF</dd>
        </div>
        <div>
          <dt>Expense</dt>
          <dd className="amount">{formatAmount(report.expense.total)} CHF</dd>
        </div>
        <div>
          <dt>Net</dt>
          <dd className={`amount ${report.net >= 0 ? 'income' : 'expense'}`}>{formatAmount(report.net)} CHF</dd>
        </div>
      </dl>
      {report.expense.contributions > 0 && (
        <p className="report-note">
          Expense without Contributions: {formatAmount(report.expense.total - report.expense.contributions)} CHF
        </p>
      )}
      {pending > 0 && (
        <p className="report-note report-pending">
          {pending === 1 ? '1 amount is' : `${pending} amounts are`} pending conversion to CHF and not included in
          these totals.
        </p>
      )}
    </section>
  )
}

function Breakdown({
  section,
  side,
  drill,
  onDrill,
}: {
  section: ReportSection
  side: Side
  drill: string | null
  onDrill: (id: string | null) => void
}) {
  const byId = new Map(section.categories.map(c => [c.id, c]))
  const current = drill === null ? undefined : byId.get(drill)
  // A drilled-into Category that no longer has amounts (e.g. after switching side) falls back to the top level.
  const level = current ?? null
  const children = section.categories.filter(c => c.parentId === (level?.id ?? null))
  const hasChildren = (c: CategoryTotal) => section.categories.some(x => x.parentId === c.id)
  const levelTotal = level?.total ?? section.total

  const trail: CategoryTotal[] = []
  for (let c = current; c; c = c.parentId ? byId.get(c.parentId) : undefined) trail.unshift(c)

  const rows: { key: string; label: string; amount: number; category?: CategoryTotal; muted?: boolean }[] = [
    ...children.map(c => ({ key: c.id, label: c.name, amount: c.total, category: c })),
  ]
  if (level && level.direct > 0 && children.length > 0)
    rows.push({ key: 'direct', label: `${level.name} (not in a subcategory)`, amount: level.direct, muted: true })
  if (!level && section.uncategorised > 0)
    rows.push({ key: 'uncategorised', label: 'Uncategorised', amount: section.uncategorised, muted: true })

  return (
    <>
      <nav className="breadcrumb" aria-label="Category path">
        {trail.length === 0 ? (
          <span>All Categories</span>
        ) : (
          <>
            <button type="button" className="button-link" onClick={() => onDrill(null)}>
              All Categories
            </button>
            {trail.map((c, i) => (
              <span key={c.id}>
                {' › '}
                {i === trail.length - 1 ? (
                  <span>{c.name}</span>
                ) : (
                  <button type="button" className="button-link" onClick={() => onDrill(c.id)}>
                    {c.name}
                  </button>
                )}
              </span>
            ))}
          </>
        )}
      </nav>
      {rows.length === 0 ? (
        <p>{side === 'expense' ? 'No Expenses' : 'No Income'} this month.</p>
      ) : (
        <ul className="breakdown-list">
          {rows.map(r => (
            <li key={r.key} className={`breakdown-row${r.muted ? ' muted' : ''}`}>
              <span className="breakdown-name">
                {r.category && hasChildren(r.category) ? (
                  <button type="button" className="button-link" onClick={() => onDrill(r.category!.id)}>
                    {r.label} ›
                  </button>
                ) : (
                  r.label
                )}
                {r.category?.builtInKey && <span className="category-badge">Contribution</span>}
              </span>
              <span className="breakdown-share">
                {levelTotal > 0 ? `${Math.round((r.amount / levelTotal) * 100)} %` : ''}
              </span>
              <span className="amount">{formatAmount(r.amount)} CHF</span>
            </li>
          ))}
          <li className="breakdown-row breakdown-total">
            <span className="breakdown-name">Total</span>
            <span className="breakdown-share" />
            <span className="amount">{formatAmount(levelTotal)} CHF</span>
          </li>
        </ul>
      )}
    </>
  )
}
