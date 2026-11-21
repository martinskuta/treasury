import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router'
import { ReportBreakdown, ReportOwnerToggle, ReportSideToggle, ReportTotals } from '../components/ReportViews'
import { getMonthlyReport, type MonthlyReport } from '../lib/api'
import { reportOwnerFromParams, reportSideFromParams } from '../lib/reportParams'
import './reports.css'

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

/**
 * Monthly overview: Income versus Expense in CHF and a breakdown by Category that drills down through the tree.
 * Month, Owner and side live in the URL (?month=2026-11&owner=family&side=income) so Back works and links can be shared.
 */
export function MonthlyOverviewPage() {
  const [params, setParams] = useSearchParams()
  const month = /^\d{4}-\d{2}$/.test(params.get('month') ?? '') ? params.get('month')! : currentMonth()
  const owner = reportOwnerFromParams(params)
  const side = reportSideFromParams(params)

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
      <div className="page-header">
        <h1 className="page-title">Monthly overview</h1>
        <div className="report-toolbar">
          <ReportOwnerToggle owner={owner} onChange={o => update({ owner: o })} />
          <div className="month-switcher">
            <button
              type="button"
              className="button-secondary month-step"
              aria-label="Previous month"
              onClick={() => update({ month: shiftMonth(month, -1) })}
            >
              ‹
            </button>
            <span className="month-label" aria-live="polite">
              {monthLabel(month)}
            </span>
            <button
              type="button"
              className="button-secondary month-step"
              aria-label="Next month"
              onClick={() => update({ month: shiftMonth(month, 1) })}
            >
              ›
            </button>
          </div>
        </div>
      </div>

      {loadError && <p className="status status-bad">Couldn’t load the overview.</p>}
      {shown === null && !loadError && <p className="page-loading">Loading…</p>}
      {shown !== null && (
        <div className="report-grid">
          <ReportTotals report={shown} />
          <section className="ui-component report-breakdown" aria-label="Breakdown by Category">
            <div className="report-breakdown-head">
              <ReportSideToggle side={side} onChange={s => update({ side: s })} />
            </div>
            <ReportBreakdown
              section={shown[side]}
              emptyText={side === 'expense' ? 'No Expenses this month.' : 'No Income this month.'}
              drill={drill}
              onDrill={setDrill}
            />
          </section>
        </div>
      )}
    </>
  )
}
