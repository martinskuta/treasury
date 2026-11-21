/*
 * The Dashboard's UI components (pages/DashboardPage.tsx), each taking its already-loaded data as a Load<T>.
 * Layout and look: styles/dashboard.css, the cream-v3 prototype and docs/design-guidelines.md ("Dashboard").
 */
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import type { MonthlyReport, ReportOwner, TagReport } from '../../lib/api'
import {
  accountBadge,
  tagRange,
  topLevelSlices,
  type AccountEntry,
  type AccountsSummary,
} from '../../lib/dashboard'
import { formatAmount } from '../../lib/format'
import { Amount } from '../Amount'
import { ChartLegend, Donut, ProgressBar, StackedBar, TrendChart, seriesColor, toneColors } from '../charts'
import { Frankie } from '../frankie/Frankie'
import { ReportOwnerToggle } from '../ReportViews'
import { UiComponent } from '../UiComponent'
import type { Load } from './useLoad'

/** Whole francs for legends and the donut centre: 1842.6 -> "1'843". */
const wholeFrancs = (value: number) => formatAmount(Math.round(value)).slice(0, -3)

/** Loading / failed text in place of a UI component's body; null once the data is ready. */
function NotReady({ load, what }: { load: Load<unknown>; what: string }) {
  if (load.status === 'loading') return <p className="dash-note">Loading…</p>
  if (load.status === 'failed') return <p className="dash-note status-bad">Couldn’t load {what}.</p>
  return null
}

function Empty({ children }: { children: ReactNode }) {
  return <p className="dash-empty">{children}</p>
}

// ---- Welcome strip ----

export function WelcomeStrip({
  name,
  subtitle,
  owner,
  onOwnerChange,
  net,
}: {
  name: string
  subtitle: string
  owner: ReportOwner
  onOwnerChange: (owner: ReportOwner) => void
  net: Load<number>
}) {
  return (
    <section className="ui-component dash-welcome a-welcome" aria-label="Welcome">
      {/* Frankie's mood rules come later (#32); until then he chews calmly. The slot leaves room for the run. */}
      <div className="dash-frankie">
        <Frankie mood="neutral" size={112} className="dash-frankie-cow" />
      </div>
      <div className="dash-greeting">
        <h1>Grüezi, {name}</h1>
        <p>{subtitle}</p>
      </div>
      <div className="dash-net">
        <div className="dash-net-label">
          <ReportOwnerToggle owner={owner} onChange={onOwnerChange} />
          <span>Net this month</span>
        </div>
        {net.status === 'ready' ? (
          <Amount value={net.data} signed tone="auto" size="l" />
        ) : (
          <span className="amount-display size-l dash-net-pending" role="status">
            <span aria-hidden="true">–</span>
            <span className="visually-hidden">
              {net.status === 'failed' ? 'Couldn’t load the net' : 'Loading the net'}
            </span>
          </span>
        )}
      </div>
    </section>
  )
}

// ---- This month ----

export function ThisMonth({ report }: { report: Load<MonthlyReport> }) {
  return (
    <UiComponent title="This month" className="dash-month a-month">
      <NotReady load={report} what="this month" />
      {report.status === 'ready' && <ThisMonthBody report={report.data} />}
    </UiComponent>
  )
}

function ThisMonthBody({ report }: { report: MonthlyReport }) {
  const income = report.income.total
  const expense = report.expense.total
  const max = Math.max(income, expense)
  const pending = report.income.pendingConversion + report.expense.pendingConversion
  if (income === 0 && expense === 0 && pending === 0) return <Empty>No Transactions this month yet.</Empty>
  return (
    <>
      <div className="dash-bar-row">
        <div className="dash-bar-top">
          <span>Income</span>
          <b className="num">{formatAmount(income)}</b>
        </div>
        <ProgressBar label="Income" value={income} max={max} color={toneColors.income.fill} />
      </div>
      <div className="dash-bar-row">
        <div className="dash-bar-top">
          <span>Expense</span>
          <b className="num">{formatAmount(expense)}</b>
        </div>
        <ProgressBar label="Expense" value={expense} max={max} color={toneColors.expense.fill} />
      </div>
      {pending > 0 && (
        <p className="dash-note">
          {pending} {pending === 1 ? 'amount waits' : 'amounts wait'} for an exchange rate.
        </p>
      )}
      <div className="dash-foot">
        <span className="muted">Net</span>
        <Amount value={report.net} signed tone="auto" className="dash-foot-amount" />
      </div>
    </>
  )
}

// ---- Spending by Category ----

export function SpendingByCategory({ report }: { report: Load<MonthlyReport> }) {
  return (
    <UiComponent title="Spending by Category" className="dash-spend a-spend">
      <NotReady load={report} what="the spending" />
      {report.status === 'ready' && <SpendingBody report={report.data} />}
    </UiComponent>
  )
}

function SpendingBody({ report }: { report: MonthlyReport }) {
  const slices = topLevelSlices(report.expense, 7)
  if (slices.length === 0) return <Empty>No Expenses this month yet.</Empty>
  const segments = slices.map((s, i) => ({ ...s, color: seriesColor(i) }))
  return (
    <div className="dash-donut-wrap">
      <Donut
        ariaLabel="Spending by Category this month"
        segments={segments}
        centreLabel="CHF"
        centreValue={wholeFrancs(report.expense.total)}
      />
      <ChartLegend items={segments.map(s => ({ label: s.label, color: s.color, value: wholeFrancs(s.value) }))} />
    </div>
  )
}

// ---- Accounts ----

export function Accounts({ summary }: { summary: Load<AccountsSummary> }) {
  return (
    <UiComponent
      title="Accounts"
      actions={
        <Link className="button-link" to="/accounts">
          All Accounts
        </Link>
      }
      className="dash-accounts a-accounts"
    >
      <NotReady load={summary} what="the Accounts" />
      {summary.status === 'ready' && <AccountsBody summary={summary.data} />}
    </UiComponent>
  )
}

function AccountsBody({ summary }: { summary: AccountsSummary }) {
  if (summary.entries.length === 0)
    return (
      <Empty>
        No Accounts here yet. <Link to="/accounts">Add one on the Accounts page</Link>.
      </Empty>
    )
  return (
    <>
      <ul className="dash-accts">
        {summary.entries.map(entry => (
          <AccountItem key={entry.kind === 'account' ? entry.account.id : entry.multi.id} entry={entry} />
        ))}
      </ul>
      <div className="dash-foot">
        <span className="muted">Total</span>
        <Amount value={summary.totalChf} size="m" />
      </div>
      {summary.notInTotal > 0 && (
        <p className="dash-note dash-note-right">
          Without {summary.notInTotal} in other currencies
        </p>
      )}
    </>
  )
}

/** "1'234.50", or "EUR 1'234.50" outside CHF. */
const balance = (value: number, currency: string) =>
  currency === 'CHF' ? formatAmount(value) : `${currency} ${formatAmount(value)}`

function AccountItem({ entry }: { entry: AccountEntry }) {
  if (entry.kind === 'account') {
    const a = entry.account
    return (
      <li>
        <div className="dash-acct">
          <span className="dash-acct-badge" aria-hidden="true">
            {accountBadge(a.name)}
          </span>
          <div className="dash-acct-name">
            {a.name}
            <small>{a.owner.name}</small>
          </div>
          <div className="dash-acct-bal num">{balance(a.balance, a.currency)}</div>
        </div>
      </li>
    )
  }
  const m = entry.multi
  return (
    <li>
      <div className="dash-acct">
        <span className="dash-acct-badge multi" aria-hidden="true">
          {accountBadge(m.name)}
        </span>
        <div className="dash-acct-name">
          {m.name}
          <small>{m.owner.name} · Multi-currency</small>
        </div>
        <div className="dash-acct-bal num" title={m.totalBalance === null ? 'Waiting for an exchange rate' : undefined}>
          {m.totalBalance === null ? '–' : balance(m.totalBalance, m.mainCurrency)}
        </div>
      </div>
      <ul className="dash-acct-sub">
        {m.accounts.map(a => (
          <li key={a.id}>
            <span className="muted">{a.currency}</span>
            <span className="num">{formatAmount(a.balance)}</span>
          </li>
        ))}
      </ul>
    </li>
  )
}

// ---- Last 6 months ----

export function LastSixMonths({ labels, reports }: { labels: string[]; reports: Load<MonthlyReport[]> }) {
  if (reports.status !== 'ready')
    return (
      <UiComponent title="Last 6 months" className="dash-trend a-trend">
        <NotReady load={reports} what="the last 6 months" />
      </UiComponent>
    )
  const income = reports.data.map(r => r.income.total)
  const expense = reports.data.map(r => r.expense.total)
  const incomeTotal = income.reduce((s, v) => s + v, 0)
  const expenseTotal = expense.reduce((s, v) => s + v, 0)
  const empty = incomeTotal === 0 && expenseTotal === 0
  return (
    <section className="ui-component dash-trend a-trend">
      <div className="dash-trend-head">
        <h2>Last 6 months</h2>
        {!empty && (
          <>
            <div className="dash-stat">
              <div className="label">
                <span className="dot" style={{ background: toneColors.income.fill }} />
                Income
              </div>
              <Amount value={incomeTotal} currency={null} size="s" />
            </div>
            <div className="dash-stat">
              <div className="label">
                <span className="dot" style={{ background: toneColors.expense.fill }} />
                Expense
              </div>
              <Amount value={expenseTotal} currency={null} size="s" />
            </div>
            <div className="dash-stat">
              <div className="label">Net</div>
              <Amount value={incomeTotal - expenseTotal} currency={null} signed tone="auto" size="s" />
            </div>
          </>
        )}
      </div>
      {empty ? (
        <Empty>No Income or Expense in the last 6 months yet.</Empty>
      ) : (
        <TrendChart
          ariaLabel="Income and Expense, last 6 months"
          labels={labels}
          series={[
            { name: 'Income', values: income, tone: 'income' },
            { name: 'Expense', values: expense, tone: 'expense' },
          ]}
        />
      )}
    </section>
  )
}

// ---- Uncategorised ----

export function Uncategorised({ count, owner }: { count: Load<number>; owner: ReportOwner }) {
  return (
    <UiComponent title="Uncategorised" className="dash-uncat a-uncat">
      <NotReady load={count} what="the Uncategorised queue" />
      {count.status === 'ready' && (
        <div className="dash-uncat-row">
          <div>
            <div className={count.data > 0 ? 'dash-uncat-count num' : 'dash-uncat-count none num'}>{count.data}</div>
            <div className="dash-uncat-text muted">
              {count.data === 0 ? 'Every Transaction has a Category.' : count.data === 1 ? 'waits for a Category' : 'waiting for a Category'}
            </div>
          </div>
          {count.data > 0 && (
            <Link className="button-primary" to={`/uncategorised?owner=${owner}`}>
              Review
            </Link>
          )}
        </div>
      )}
    </UiComponent>
  )
}

// ---- Latest Tag ----

/** `tag` null = there are no Tags at all; its report loads separately. */
export function LatestTag({ tag }: { tag: Load<{ report: TagReport } | null> }) {
  const range = tag.status === 'ready' && tag.data ? tagRange(tag.data.report.tag) : null
  return (
    <UiComponent
      title="Latest Tag"
      actions={range && <span className="dash-tag-range muted">{range}</span>}
      className="dash-tag a-tag"
    >
      <NotReady load={tag} what="the Latest Tag" />
      {tag.status === 'ready' &&
        (tag.data === null ? (
          <Empty>
            No Tag yet. <Link to="/settings/tags">Create one</Link> for a holiday or an event to see what it cost.
          </Empty>
        ) : (
          <LatestTagBody report={tag.data.report} />
        ))}
    </UiComponent>
  )
}

function LatestTagBody({ report }: { report: TagReport }) {
  const slices = topLevelSlices(report.expense, 5)
  const segments = slices.map((s, i) => ({ ...s, color: seriesColor(i) }))
  return (
    <>
      <div className="dash-tag-head">
        <Link className="chip-tag dash-tag-name" to={`/reports/tag/${report.tag.id}?owner=${report.owner}`}>
          {report.tag.name}
        </Link>
        <Amount value={report.expense.total} size="m" />
      </div>
      {segments.length === 0 ? (
        <Empty>No Expenses with this Tag yet.</Empty>
      ) : (
        <>
          <StackedBar ariaLabel={`${report.tag.name} by Category`} segments={segments} />
          <ChartLegend
            variant="inline"
            className="dash-tag-legend"
            items={segments.map(s => ({ label: s.label, color: s.color, value: wholeFrancs(s.value) }))}
          />
        </>
      )}
    </>
  )
}
