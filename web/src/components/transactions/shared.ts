import type { TransactionFilter, TransactionSort } from '../../lib/api'
import { toErrors, type Errors } from '../../lib/errors'

/** Form errors for Transaction changes; a 403 means the Member doesn't manage the Account. */
export const toTransactionErrors = (err: unknown): Errors =>
  toErrors(err, 'Only the Members who manage this Account can change that.')

/** Shown instead of the Account of a Transaction (or other leg) in another Member's personal Account (US37). */
export const HIDDEN_ACCOUNT = 'another Member’s Account'

/** "1'234.50" or "-12.5" -> number; null when empty; NaN when not a number. */
export const parseAmount = (text: string) => (text.trim() === '' ? null : Number(text.replace(/['\s]/g, '')))

export const today = () => {
  const d = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

export const PAGE_SIZE = 50

export const SORTS: { value: TransactionSort; label: string }[] = [
  { value: 'date-desc', label: 'Newest first' },
  { value: 'date-asc', label: 'Oldest first' },
  { value: 'amount-asc', label: 'Largest Expense first' },
  { value: 'amount-desc', label: 'Largest Income first' },
]

/** URL query keys of the filters (sort and page are kept apart: changing a filter goes back to page 1). */
export const FILTER_KEYS = ['q', 'account', 'owner', 'category', 'tag', 'from', 'to'] as const

/** The 1-based page number in the URL (?page=). */
export const pageFromParams = (params: URLSearchParams) =>
  Math.max(1, Number.parseInt(params.get('page') ?? '1', 10) || 1)

/**
 * The list request for the URL's query string: ?q=&account=&owner=family|<memberId>&category=<id>|uncategorised
 * &tag=&from=&to=&sort=&page=. Filters live in the URL so Back works and a filtered list can be bookmarked or shared.
 */
export function filterFromParams(params: URLSearchParams): TransactionFilter {
  const get = (key: string) => params.get(key) || undefined
  const category = get('category')
  const sort = SORTS.find(s => s.value === params.get('sort'))?.value
  return {
    q: get('q'),
    accountId: get('account'),
    owner: get('owner'),
    categoryId: category === 'uncategorised' ? undefined : category,
    uncategorised: category === 'uncategorised' || undefined,
    tagId: get('tag'),
    from: get('from'),
    to: get('to'),
    sort,
    offset: (pageFromParams(params) - 1) * PAGE_SIZE,
    limit: PAGE_SIZE,
  }
}
