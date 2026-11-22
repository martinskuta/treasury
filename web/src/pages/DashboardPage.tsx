import { useState } from 'react'
import { useSearchParams } from 'react-router'
import {
  Accounts,
  LastSixMonths,
  LastImport,
  LatestTag,
  Uncategorised,
  WelcomeStrip,
} from '../components/dashboard/DashboardComponents'
import { useLoad, type Load } from '../components/dashboard/useLoad'
import {
  getRecentMonthsReport,
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
  isoDate,
  latestTag,
  monthAndYear,
  monthName,
} from '../lib/dashboard'
import { reportOwnerFromParams } from '../lib/reportParams'
import '../styles/dashboard.css'

/**
 * The Dashboard at `/`: welcome strip with Frankie and the end month's net, then the bento grid of UI components.
 * It follows the data, not the calendar: the recent months report names the Owner filter's end month (the month of its
 * latest Transaction, never later than the last complete month) and the six months ending at it. Everything follows
 * the Owner filter (?owner=me|family, as on the reports); each UI component loads and fails on its own.
 */
export function DashboardPage() {
  const me = useMe()
  const [params, setParams] = useSearchParams()
  const owner = reportOwnerFromParams(params)

  // Fixed for the life of the page so the Latest Tag doesn't change under a render.
  const [today] = useState(() => new Date())

  const recent = useLoad(`recent-months|${owner}`, signal => getRecentMonthsReport(owner, signal))
  const endMonth = recent.status === 'ready' ? recent.data.endMonth : null
  const endMonthNet = mapLoad(recent, r => r.months.find(m => m.month === r.endMonth)?.net ?? 0)

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
        name={me.firstName}
        subtitle={
          endMonth
            ? monthAndYear(endMonth)
            : recent.status === 'failed'
              ? 'Couldn’t load the latest month.'
              : 'Loading the latest month…'
        }
        netLabel={endMonth ? `Net in ${monthName(endMonth)}` : 'Net'}
        owner={owner}
        onOwnerChange={setOwner}
        net={endMonthNet}
      />
      <Accounts summary={summary} />
      <LastImport summary={summary} />
      <Uncategorised count={uncategorisedCount} owner={owner} />
      <LastSixMonths report={recent} />
      <LatestTag tag={latest} />
    </div>
  )
}

function mapLoad<T, U>(load: Load<T>, map: (data: T) => U): Load<U> {
  return load.status === 'ready' ? { status: 'ready', data: map(load.data) } : load
}
