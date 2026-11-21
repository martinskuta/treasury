/*
 * Building blocks of the reports (Monthly overview, Tag report); styles in pages/reports.css (imported by the report pages).
 *
 *   <ReportOwnerToggle owner={owner} onChange={…} />        // Mine / Family segmented control
 *   <ReportTotals report={report} />                        // UI component: Income and Expense bars, Net
 *   <UiComponent title="By Category" actions={<ReportSideToggle side={side} onChange={…} />}>
 *     <ReportBreakdown section={report[side]} … />          // donut + drillable list
 *   </UiComponent>
 */
import type { CategoryTotal, ReportOwner, ReportSection } from '../lib/api'
import { formatAmount } from '../lib/format'
import type { ReportSide } from '../lib/reportParams'
import { Amount } from './Amount'
import { Donut, ProgressBar, seriesColor, toneColors } from './charts'
import { SegmentedControl, type SegmentedOption } from './SegmentedControl'
import { UiComponent } from './UiComponent'

export type { ReportSide }

const ownerOptions = [
  { value: 'me', label: 'Mine' },
  { value: 'family', label: 'Family' },
] as const satisfies readonly SegmentedOption<ReportOwner>[]

const sideOptions = [
  { value: 'expense', label: 'Expenses by Category' },
  { value: 'income', label: 'Income by Category' },
] as const satisfies readonly SegmentedOption<ReportSide>[]

/** "Mine" / "Family" toggle of a report (?owner=). */
export function ReportOwnerToggle({ owner, onChange }: { owner: ReportOwner; onChange: (owner: ReportOwner) => void }) {
  return <SegmentedControl label="Owner" value={owner} options={ownerOptions} onChange={onChange} />
}

/** "Expenses by Category" / "Income by Category" toggle of a report (?side=). */
export function ReportSideToggle({ side, onChange }: { side: ReportSide; onChange: (side: ReportSide) => void }) {
  return <SegmentedControl label="Breakdown" value={side} options={sideOptions} onChange={onChange} />
}

/** What every report has at the top: both sides plus Net (MonthlyReport, TagReport). */
export type ReportFigures = { income: ReportSection; expense: ReportSection; net: number }

/** Income and Expense in CHF as pill bars, Net below, plus the Contributions and pending-conversion notes. */
export function ReportTotals({ report }: { report: ReportFigures }) {
  const pending = report.income.pendingConversion + report.expense.pendingConversion
  const max = Math.max(report.income.total, report.expense.total)
  return (
    <UiComponent title="Income and Expense" className="report-totals">
      <div className="report-bar">
        <div className="report-bar-top">
          <span className="label">Income</span>
          <Amount value={report.income.total} size="s" />
        </div>
        <ProgressBar label="Income" value={report.income.total} max={max} color={toneColors.income.fill} />
      </div>
      <div className="report-bar">
        <div className="report-bar-top">
          <span className="label">Expense</span>
          <Amount value={report.expense.total} size="s" />
        </div>
        <ProgressBar label="Expense" value={report.expense.total} max={max} color={toneColors.expense.fill} />
      </div>
      <div className="report-net">
        <span className="label">Net</span>
        <Amount value={report.net} signed tone="auto" size="l" />
      </div>
      {report.expense.contributions > 0 && (
        <p className="report-note">
          Expense without Contributions:{' '}
          <span className="num">CHF {formatAmount(report.expense.total - report.expense.contributions)}</span>
        </p>
      )}
      {pending > 0 && (
        <p className="report-note report-pending">
          {pending === 1 ? '1 amount is' : `${pending} amounts are`} pending conversion to CHF and not included in
          these totals.
        </p>
      )}
    </UiComponent>
  )
}

type Row = { key: string; label: string; amount: number; color: string; category?: CategoryTotal; kind?: 'direct' | 'uncategorised' }

/** One side of a report by Category: a donut and a list; each level with subcategories can be drilled into. */
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

  const rows: Row[] = children.map((c, i) => ({ key: c.id, label: c.name, amount: c.total, color: seriesColor(i), category: c }))
  if (level && level.direct > 0 && children.length > 0)
    rows.push({
      key: 'direct',
      label: `${level.name} (not in a subcategory)`,
      amount: level.direct,
      color: 'var(--muted)',
      kind: 'direct',
    })
  if (!level && section.uncategorised > 0)
    rows.push({
      key: 'uncategorised',
      label: 'Uncategorised',
      amount: section.uncategorised,
      color: 'var(--coral-soft)',
      kind: 'uncategorised',
    })

  const share = (amount: number) => (levelTotal > 0 ? Math.round((amount / levelTotal) * 100) : 0)

  return (
    <>
      <nav className="breadcrumb" aria-label="Category path">
        {trail.length === 0 ? (
          <span aria-current="page">All Categories</span>
        ) : (
          <>
            <button type="button" className="button-link" onClick={() => onDrill(null)}>
              All Categories
            </button>
            {trail.map((c, i) => (
              <span key={c.id}>
                <span className="breadcrumb-sep" aria-hidden="true">
                  ›
                </span>
                {i === trail.length - 1 ? (
                  <span aria-current="page">{c.name}</span>
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
        <p className="empty">{emptyText}</p>
      ) : (
        <div className="breakdown">
          <div className="breakdown-chart">
            <Donut
              ariaLabel={`${level?.name ?? 'All Categories'} by Category`}
              segments={rows.map(r => ({ label: r.label, value: r.amount, color: r.color }))}
              size={168}
              thickness={18}
              centreLabel="CHF"
              centreValue={formatAmount(levelTotal).slice(0, -3)}
            />
          </div>
          <ul className="breakdown-list">
            {rows.map(r => (
              <li key={r.key} className={`breakdown-row${r.kind ? ` breakdown-${r.kind}` : ''}`}>
                <div className="breakdown-top">
                  <span className="breakdown-name">
                    <span className="dot" style={{ background: r.color }} />
                    {r.category && hasChildren(r.category) ? (
                      <button type="button" className="breakdown-drill" onClick={() => onDrill(r.category!.id)}>
                        {r.label}
                        <span aria-hidden="true"> ›</span>
                      </button>
                    ) : r.kind === 'uncategorised' ? (
                      <span className="chip-uncategorised">{r.label}</span>
                    ) : (
                      <span>{r.label}</span>
                    )}
                    {r.category?.builtInKey && <span className="chip">Contribution</span>}
                  </span>
                  <span className="breakdown-share num">{levelTotal > 0 ? `${share(r.amount)} %` : ''}</span>
                  <span className="breakdown-amount num">{formatAmount(r.amount)}</span>
                </div>
                <div className="breakdown-bar" aria-hidden="true">
                  <span style={{ width: `${share(r.amount)}%`, background: r.color }} />
                </div>
              </li>
            ))}
            <li className="breakdown-row breakdown-total">
              <div className="breakdown-top">
                <span className="breakdown-name">Total</span>
                <span className="breakdown-share" />
                <Amount value={levelTotal} size="s" className="breakdown-amount" />
              </div>
            </li>
          </ul>
        </div>
      )}
    </>
  )
}
