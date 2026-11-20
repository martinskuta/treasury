import type { CategoryTotal, ReportSection } from '../lib/api'
import { formatAmount } from '../lib/format'

/** Which side of a report is broken down by Category. */
export type ReportSide = 'expense' | 'income'

/** What every report has at the top: both sides plus Net (MonthlyReport, TagReport). */
export type ReportFigures = { income: ReportSection; expense: ReportSection; net: number }

/** Income, Expense and Net in CHF, plus the Contributions and pending-conversion notes. */
export function ReportTotals({ report }: { report: ReportFigures }) {
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

/** One side of a report by Category; each level with subcategories can be drilled into. */
export function ReportBreakdown({
  section,
  emptyText,
  drill,
  onDrill,
}: {
  section: ReportSection
  emptyText: string
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
        <p>{emptyText}</p>
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
