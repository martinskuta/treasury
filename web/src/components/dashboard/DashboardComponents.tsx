/*
 * The Dashboard's UI components (pages/DashboardPage.tsx), each taking its already-loaded data as a Load<T>.
 * Layout and look: styles/dashboard.css, the cream-v3 prototype and docs/design-guidelines.md ("Dashboard").
 */
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import type { RecentMonthsReport, ReportOwner, TagReport } from '../../lib/api'
import {
  accountBadge,
  lastImportRows,
  shortMonthLabel,
  tagRange,
  topLevelSlices,
  type AccountEntry,
  type AccountsSummary,
  type LastImportRow,
  type Slice,
} from '../../lib/dashboard'
import { formatAmount, formatDate } from '../../lib/format'
import { ownerHref } from '../../lib/reportParams'
import { Amount } from '../Amount'
import { Balance } from '../Balance'
import {
  ChartLegend,
  StackedBar,
  TrendChart,
  seriesColor,
  toneColors,
  type LegendItem,
} from '../charts'
import { Frankie } from '../frankie/Frankie'
import { ReportOwnerToggle } from '../ReportViews'
import { UiComponent } from '../UiComponent'
import type { Load } from './useLoad'

/** Whole francs for legends: 1842.6 -> "1'843". */
const wholeFrancs = (value: number) => formatAmount(value, { whole: true })

/** Category slices with their series colours, and the matching legend items in whole francs. */
function colouredSlices(slices: Slice[]) {
  const segments = slices.map((s, i) => ({ ...s, color: seriesColor(i) }))
  const legend: LegendItem[] = segments.map(s => ({ label: s.label, color: s.color, value: wholeFrancs(s.value) }))
  return { segments, legend }
}

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
  netLabel,
  owner,
  onOwnerChange,
  net,
}: {
  name: string
  subtitle: string
  netLabel: string
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
          <span>{netLabel}</span>
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
      {summary.withoutRate > 0 && (
        <p className="dash-note dash-note-right">
          Not in the total: {summary.withoutRate} without an exchange rate yet
        </p>
      )}
    </>
  )
}

/** The currency code is shown only outside CHF: "1'234.50", "EUR 1'234.50". */
const shownCurrency = (currency: string) => (currency === 'CHF' ? null : currency)

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
          <Balance value={a.balance} currency={shownCurrency(a.currency)} className="dash-acct-bal" />
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
        <Balance
          value={m.totalBalance}
          currency={shownCurrency(m.mainCurrency)}
          className="dash-acct-bal"
          title={m.totalBalance === null ? 'Waiting for an exchange rate' : undefined}
        />
      </div>
      <ul className="dash-acct-sub">
        {m.accounts.map(a => (
          <li key={a.id}>
            <span className="muted">{a.currency}</span>
            <Balance value={a.balance} currency={null} />
          </li>
        ))}
      </ul>
    </li>
  )
}

// ---- Last import ----

/** One row per entry of the Accounts UI component: how far its data reaches and when it was last imported. */
export function LastImport({ summary }: { summary: Load<AccountsSummary> }) {
  return (
    <UiComponent title="Last import" className="a-import">
      <NotReady load={summary} what="the last imports" />
      {summary.status === 'ready' &&
        (summary.data.entries.length === 0 ? (
          <Empty>No Accounts here yet, so nothing to import.</Empty>
        ) : (
          <ul className="dash-imports">
            {lastImportRows(summary.data.entries).map(row => (
              <LastImportItem key={row.id} row={row} />
            ))}
          </ul>
        ))}
    </UiComponent>
  )
}

function LastImportItem({ row }: { row: LastImportRow }) {
  const reachClass = row.lagging ? 'dash-import-reach lagging' : 'dash-import-reach'
  const imported = row.lastImportedAt ? `imported ${formatDate(row.lastImportedAt)}` : null
  return (
    <li className="dash-import">
      <span className="dash-import-name">{row.name}</span>
      <div className="dash-import-dates">
        {row.reach === null ? (
          // No Transaction and no closing balance: the import line is all there is to say, and it needs attention.
          <span className={reachClass}>{imported ?? 'Never imported'}</span>
        ) : (
          <>
            <span className={reachClass}>
              through {formatDate(row.reach)}
              {row.lagging && <span className="visually-hidden"> (behind)</span>}
            </span>
            {/* An Account kept by hand has a reach and no Source file: no import line, so it doesn't look broken. */}
            {imported && <small>{imported}</small>}
          </>
        )}
      </div>
    </li>
  )
}

// ---- Last 6 months ----

/** The six months ending at the Owner filter's end month; the chart puts the last label, the end month, in ink. */
export function LastSixMonths({ report }: { report: Load<RecentMonthsReport> }) {
  if (report.status !== 'ready')
    return (
      <UiComponent title="Last 6 months" className="dash-trend a-trend">
        <NotReady load={report} what="the last 6 months" />
      </UiComponent>
    )
  const { months } = report.data
  const income = months.map(m => m.income)
  const expense = months.map(m => m.expense)
  const incomeTotal = income.reduce((s, v) => s + v, 0)
  const expenseTotal = expense.reduce((s, v) => s + v, 0)
  const empty = incomeTotal === 0 && expenseTotal === 0
  // Amounts without a CHF rate yet are in no total, so the totals alone would hide them.
  const pending = months.reduce((s, m) => s + m.pendingConversion, 0)
  return (
    <UiComponent
      title="Last 6 months"
      actions={empty ? undefined : <TrendStats income={incomeTotal} expense={expenseTotal} />}
      className="dash-trend a-trend"
    >
      {empty ? (
        pending === 0 && <Empty>No Income or Expense in the last 6 months yet.</Empty>
      ) : (
        <TrendChart
          ariaLabel="Income and Expense, last 6 months"
          labels={months.map(m => shortMonthLabel(m.month))}
          series={[
            { name: 'Income', values: income, tone: 'income' },
            { name: 'Expense', values: expense, tone: 'expense' },
          ]}
        />
      )}
      {pending > 0 && (
        <p className="dash-note">
          {pending} {pending === 1 ? 'amount waits' : 'amounts wait'} for an exchange rate.
        </p>
      )}
    </UiComponent>
  )
}

/** The six months' Income, Expense and Net at the right of the title row. */
function TrendStats({ income, expense }: { income: number; expense: number }) {
  return (
    <div className="dash-trend-stats">
      <div className="dash-stat">
        <div className="label">
          <span className="dot" style={{ background: toneColors.income.fill }} />
          Income
        </div>
        <Amount value={income} currency={null} size="s" />
      </div>
      <div className="dash-stat">
        <div className="label">
          <span className="dot" style={{ background: toneColors.expense.fill }} />
          Expense
        </div>
        <Amount value={expense} currency={null} size="s" />
      </div>
      <div className="dash-stat">
        <div className="label">Net</div>
        <Amount value={income - expense} currency={null} signed tone="auto" size="s" />
      </div>
    </div>
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
            <Link className="button-primary" to={ownerHref('/uncategorised', owner)}>
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
export function LatestTag({ tag }: { tag: Load<TagReport | null> }) {
  const range = tag.status === 'ready' && tag.data ? tagRange(tag.data.tag) : null
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
          <LatestTagBody report={tag.data} />
        ))}
    </UiComponent>
  )
}

function LatestTagBody({ report }: { report: TagReport }) {
  const { segments, legend } = colouredSlices(topLevelSlices(report.expense, 5))
  return (
    <>
      <div className="dash-tag-head">
        <Link className="chip-tag dash-tag-name" to={ownerHref(`/reports/tag/${report.tag.id}`, report.owner)}>
          {report.tag.name}
        </Link>
        <Amount value={report.expense.total} size="m" />
      </div>
      {segments.length === 0 ? (
        <Empty>No Expenses with this Tag yet.</Empty>
      ) : (
        <>
          <StackedBar ariaLabel={`${report.tag.name} by Category`} segments={segments} />
          <ChartLegend variant="inline" className="dash-tag-legend" items={legend} />
        </>
      )}
    </>
  )
}
