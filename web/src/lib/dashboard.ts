// Pure helpers behind the Dashboard (pages/DashboardPage.tsx): month names, the Latest Tag rule, Category slices for
// the stacked bar, and the Accounts of one Owner filter with their CHF total and their "Last import" rows.
import type { Account, Me, MultiCurrencyAccount, ReportOwner, ReportSection, Tag } from './api'

const shortMonthNames = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']
const monthNames = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
]

const pad = (n: number) => String(n).padStart(2, '0')

/** "2026-11" -> "Nov". */
export const shortMonthLabel = (month: string) => shortMonthNames[Number(month.slice(5, 7)) - 1]

/** "2026-10" -> "October". */
export const monthName = (month: string) => monthNames[Number(month.slice(5, 7)) - 1]

/** "2026-10" -> "October 2026". */
export const monthAndYear = (month: string) => `${monthName(month)} ${month.slice(0, 4)}`

/** ISO date as an ISO-comparable local yyyy-MM-dd. */
export const isoDate = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`

/**
 * The Latest Tag: the Tag whose date range started most recently; without such a Tag, the most recently created one.
 * Tags whose date range starts after `today` haven't started and are never chosen. Tag ids are UUIDv7, so they sort
 * by creation time.
 */
export function latestTag(tags: Tag[], today: string): Tag | null {
  const byCreation = (a: Tag, b: Tag) => (a.id < b.id ? 1 : a.id > b.id ? -1 : 0)
  const candidates = tags.filter(t => t.dateFrom === null || t.dateFrom <= today)
  const started = candidates
    .filter(t => t.dateFrom !== null)
    .sort((a, b) => (a.dateFrom! < b.dateFrom! ? 1 : a.dateFrom! > b.dateFrom! ? -1 : byCreation(a, b)))
  return started[0] ?? candidates.sort(byCreation)[0] ?? null
}

/** "02.08. – 16.08.2026"; the year is shown once when both ends share it. */
export function tagRange(tag: Pick<Tag, 'dateFrom' | 'dateTo'>): string | null {
  if (!tag.dateFrom || !tag.dateTo) return null
  const [fy, fm, fd] = tag.dateFrom.split('-')
  const [ty, tm, td] = tag.dateTo.split('-')
  return fy === ty ? `${fd}.${fm}. – ${td}.${tm}.${ty}` : `${fd}.${fm}.${fy} – ${td}.${tm}.${ty}`
}

export type Slice = { label: string; value: number }

/**
 * A report side split by top-level Category (plus Uncategorised), largest first; beyond `max` slices the rest is
 * folded into one "Other Categories" slice so the colours stay distinct.
 */
export function topLevelSlices(section: ReportSection, max: number): Slice[] {
  const slices: Slice[] = section.categories
    .filter(c => c.parentId === null && c.total > 0)
    .map(c => ({ label: c.name, value: c.total }))
  if (section.uncategorised > 0) slices.push({ label: 'Uncategorised', value: section.uncategorised })
  slices.sort((a, b) => b.value - a.value)
  if (slices.length <= max) return slices
  const rest = slices.slice(max - 1).reduce((sum, s) => sum + s.value, 0)
  return [...slices.slice(0, max - 1), { label: 'Other Categories', value: rest }]
}

/** One row of the Accounts UI component: a plain Account, or a Multi-currency account with its Accounts nested. */
export type AccountEntry =
  | { kind: 'account'; account: Account }
  | { kind: 'multi'; multi: MultiCurrencyAccount }

export type AccountsSummary = {
  entries: AccountEntry[]
  /** Sum of every entry's balance in CHF at today's exchange rates. */
  totalChf: number
  /** Entries left out of the total because an exchange rate is still missing. */
  withoutRate: number
}

const ownedBy = (owner: Account['owner'], filter: ReportOwner, me: Me) =>
  filter === 'family' ? owner.kind === 'family' : owner.kind === 'member' && owner.memberId === me.id

/** The Accounts and Multi-currency accounts of the Owner filter (Mine = the viewer's own, Family = the Family's). */
export function accountsSummary(
  accounts: Account[],
  multis: MultiCurrencyAccount[],
  filter: ReportOwner,
  me: Me,
): AccountsSummary {
  const entries: AccountEntry[] = [
    ...accounts
      .filter(a => a.multiCurrencyAccountId === null && ownedBy(a.owner, filter, me))
      .map(account => ({ kind: 'account' as const, account })),
    ...multis.filter(m => ownedBy(m.owner, filter, me)).map(multi => ({ kind: 'multi' as const, multi })),
  ].sort((a, b) => entryName(a).localeCompare(entryName(b)))

  let totalChf = 0
  let withoutRate = 0
  for (const entry of entries) {
    const chf = entry.kind === 'account' ? entry.account.balanceChf : entry.multi.totalBalanceChf
    if (chf === null) withoutRate++
    else totalChf += chf
  }
  return { entries, totalChf, withoutRate }
}

const entryName = (e: AccountEntry) => (e.kind === 'account' ? e.account.name : e.multi.name)

/** One row of the Last import UI component: how far an Account's data reaches and when it was last imported. */
export type LastImportRow = {
  id: string
  name: string
  /** ISO date; null when it has neither a Transaction nor a closing balance. */
  reach: string | null
  /** ISO date-time of its most recent Source file; null when never imported. */
  lastImportedAt: string | null
  lagging: boolean
}

/** The Last import rows of the Accounts UI component's entries, in the same order. */
export function lastImportRows(entries: AccountEntry[]): LastImportRow[] {
  return entries.map(entry => {
    const { id, name, reach, lastImportedAt, lagging } = entry.kind === 'account' ? entry.account : entry.multi
    return { id, name, reach, lastImportedAt, lagging }
  })
}

/** Short badge text for an Account icon: an all-caps first word ("TKB Privatkonto" -> "TKB"), else its initial. */
export function accountBadge(name: string): string {
  const first = name.trim().split(/\s+/)[0] ?? ''
  if (/^[A-Z0-9]{2,4}$/.test(first)) return first
  return (first[0] ?? '?').toUpperCase()
}
