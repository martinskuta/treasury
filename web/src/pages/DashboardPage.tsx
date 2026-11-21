import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router'
import {
  Accounts,
  LastSixMonths,
  LatestTag,
  SpendingByCategory,
  ThisMonth,
  Uncategorised,
  WelcomeStrip,
} from '../components/dashboard/DashboardComponents'
import { useLoad, type Load } from '../components/dashboard/useLoad'
import {
  getMonthlyReport,
  getTagReport,
  listAccounts,
  listMultiCurrencyAccounts,
  listTags,
  listUncategorised,
  type ReportOwner,
  type TagReport,
} from '../lib/api'
import { useMe } from '../lib/auth'
import {
  accountsSummary,
  greetingName,
  isoDate,
  lastMonths,
  latestTag,
  monthSoFar,
  shortMonthLabel,
} from '../lib/dashboard'
import { reportOwnerFromParams } from '../lib/reportParams'
import '../styles/dashboard.css'

/**
 * The Dashboard at `/`: welcome strip with Frankie and this month's net, then the bento grid of UI components.
 * Everything follows the Owner filter (?owner=me|family, as on the reports). Data comes from the existing report,
 * Accounts, Tags and Uncategorised endpoints; each UI component loads and fails on its own.
 */
export function DashboardPage() {
  const me = useMe()
  const [params, setParams] = useSearchParams()
  const owner = reportOwnerFromParams(params)

  // Fixed for the life of the page so the month keys don't change under a render.
  const [today] = useState(() => new Date())
  const months = useMemo(() => lastMonths(today, 6), [today])
  const thisMonth = months[months.length - 1]

  const reports = useLoad(`reports|${thisMonth}|${owner}`, signal =>
    Promise.all(months.map(month => getMonthlyReport(month, owner, signal))),
  )
  const current = mapLoad(reports, r => r[r.length - 1])

  const accounts = useLoad('accounts', signal =>
    Promise.all([listAccounts(signal), listMultiCurrencyAccounts(signal)]),
  )
  const summary = mapLoad(accounts, ([plain, multi]) => accountsSummary(plain, multi, owner, me))

  const uncategorised = useLoad(`uncategorised|${owner}`, signal => listUncategorised(signal, owner))
  const uncategorisedCount = mapLoad(uncategorised, items => items.length)

  const tags = useLoad('tags', listTags)
  const tag = tags.status === 'ready' ? latestTag(tags.data, isoDate(today)) : null
  const tagReport = useLoad(tag ? `tag|${tag.id}|${owner}` : null, signal =>
    getTagReport(tag!.id, owner, 'default', signal),
  )
  const latest: Load<TagReport | null> =
    tags.status !== 'ready' ? tags : tag === null ? { status: 'ready', data: null } : tagReport

  const setOwner = (next: ReportOwner) => {
    const updated = new URLSearchParams(params)
    updated.set('owner', next)
    setParams(updated, { replace: true })
  }

  return (
    <div className="dash-bento">
      <WelcomeStrip
        name={greetingName(me.userName)}
        subtitle={monthSoFar(today)}
        owner={owner}
        onOwnerChange={setOwner}
        net={mapLoad(current, r => r.net)}
      />
      <ThisMonth report={current} />
      <SpendingByCategory report={current} />
      <Accounts summary={summary} />
      <LastSixMonths labels={months.map(shortMonthLabel)} reports={reports} />
      <LatestTag tag={latest} />
      <Uncategorised count={uncategorisedCount} owner={owner} />
    </div>
  )
}

function mapLoad<T, U>(load: Load<T>, map: (data: T) => U): Load<U> {
  return load.status === 'ready' ? { status: 'ready', data: map(load.data) } : load
}
