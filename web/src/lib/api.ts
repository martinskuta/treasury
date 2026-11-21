// All API calls use relative /api/... URLs: in development Vite proxies them to the API (see vite.config.ts),
// in production the API serves this app itself, so it is always the same origin and the auth cookie is sent.

/** A non-2xx API response. `fieldErrors` holds ASP.NET ValidationProblem errors (400), keyed by camelCase field. */
export class ApiError extends Error {
  readonly status: number
  readonly fieldErrors: Record<string, string[]>

  constructor(status: number, fieldErrors: Record<string, string[]> = {}, title?: string) {
    super(title ?? `HTTP ${status}`)
    this.status = status
    this.fieldErrors = fieldErrors
  }
}

let unauthorizedHandler: (() => void) | undefined

/** AuthProvider registers this: any 401 from the API means the session is gone, so the app shows the login screen. */
export function onUnauthorized(handler: (() => void) | undefined) {
  unauthorizedHandler = handler
}

type RequestOptions = { method?: string; body?: unknown; signal?: AbortSignal }

/**
 * Calls the API with a JSON body and returns the parsed JSON response (undefined for 204).
 * Throws ApiError on any non-2xx status; a 401 also notifies the unauthorized handler.
 * Every feature's fetch wrapper below should go through this.
 */
export async function apiFetch<T>(path: string, { method = 'GET', body, signal }: RequestOptions = {}): Promise<T> {
  const res = await fetch(path, {
    method,
    signal,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!res.ok) {
    if (res.status === 401) unauthorizedHandler?.()
    const problem = await readProblem(res)
    throw new ApiError(res.status, problem.errors, problem.title)
  }
  if (res.status === 204) return undefined as T
  return (await res.json()) as T
}

async function readProblem(res: Response): Promise<{ title?: string; errors?: Record<string, string[]> }> {
  try {
    return (await res.json()) as { title?: string; errors?: Record<string, string[]> }
  } catch {
    return {}
  }
}

// ---- Health ----

export type HealthCheck = { name: string; status: string }
export type Health = { status: string; checks: HealthCheck[] }

export async function getHealth(signal?: AbortSignal): Promise<Health> {
  const res = await fetch('/api/health', { signal })
  // 503 still carries the JSON report (status "Unhealthy").
  if (!res.ok && res.status !== 503) throw new Error(`HTTP ${res.status}`)
  return (await res.json()) as Health
}

// ---- Auth ----

/** The logged-in Member. */
export type Me = { id: string; userName: string; roles: string[] }

export const isAdmin = (me: Me) => me.roles.includes('Admin')

export const login = (userName: string, password: string) =>
  apiFetch<Me>('/api/auth/login', { method: 'POST', body: { userName, password } })

export const logout = () => apiFetch<void>('/api/auth/logout', { method: 'POST' })

/** The current Member, or null when signed out. */
export async function getMe(signal?: AbortSignal): Promise<Me | null> {
  const res = await fetch('/api/auth/me', { signal })
  if (res.status === 401) return null
  if (!res.ok) throw new ApiError(res.status)
  return (await res.json()) as Me
}

export const changePassword = (currentPassword: string, newPassword: string) =>
  apiFetch<void>('/api/auth/password', { method: 'POST', body: { currentPassword, newPassword } })

// ---- Owners ----

/** Who something belongs to: a Member or the Family. Requests send `{ kind: 'family' }` or `{ kind: 'member', memberId }`. */
export type OwnerRef = { kind: 'family' } | { kind: 'member'; memberId: string }

/** An Owner as the API returns it; `name` is the Member's user name or "Family". */
export type Owner = { kind: 'family' | 'member'; memberId: string | null; name: string }

// ---- Integrations ----

export type Integration = {
  key: string
  name: string
  canProvideIdentifier: boolean
  /** Used only through a Multi-currency account, never by a plain Account. */
  holdsSeveralCurrencies: boolean
}

export const listIntegrations = (signal?: AbortSignal) => apiFetch<Integration[]>('/api/integrations', { signal })

// ---- Accounts ----

export const currencies = ['CHF', 'CZK', 'EUR', 'USD'] as const
export type Currency = (typeof currencies)[number]

export type Account = {
  id: string
  name: string
  /** Name of its Ingest folder: ingest/<slug>/ */
  slug: string
  owner: Owner
  integrationKey: string
  currency: Currency
  openingBalance: number
  /** ISO date, e.g. "2026-01-01" */
  openingDate: string
  identifier: string | null
  balance: number
  /** Set when the most recent statement's closing balance differs from the calculated balance on that date. */
  balanceWarning: BalanceWarning | null
  /** The Multi-currency account it belongs to; such an Account has no Ingest folder of its own. */
  multiCurrencyAccountId: string | null
}

/** Computed on every read: it disappears once the balances match again. */
export type BalanceWarning = {
  /** ISO date of the statement's closing balance */
  date: string
  statementBalance: number
  calculatedBalance: number
  sourceFile: { id: string; fileName: string }
}

export type NewAccount = {
  name: string
  owner: OwnerRef
  integrationKey: string
  currency: Currency
  openingBalance: number | null
  openingDate: string | null
  identifier: string | null
}

/** The Accounts the current Member can see: their own and the Family's. */
export const listAccounts = (signal?: AbortSignal) => apiFetch<Account[]>('/api/accounts', { signal })

export const createAccount = (account: NewAccount) =>
  apiFetch<Account>('/api/accounts', { method: 'POST', body: account })

export const renameAccount = (id: string, name: string) =>
  apiFetch<Account>(`/api/accounts/${id}`, { method: 'PATCH', body: { name } })

// ---- Multi-currency accounts ----

/** A group of one Owner's Accounts, one per currency, that one provider holds together (e.g. Revolut). */
export type MultiCurrencyAccount = {
  id: string
  name: string
  /** Name of its Ingest folder: ingest/<slug>/ */
  slug: string
  owner: Owner
  integrationKey: string
  mainCurrency: Currency
  /** The main-currency Account first. */
  accounts: Account[]
  /** Sum of its Accounts' balances in the main currency at today's rates; null while a rate is missing. */
  totalBalance: number | null
}

export type NewMultiCurrencyAccount = {
  name: string
  owner: OwnerRef
  integrationKey: string
  mainCurrency: Currency
  /** Of the main-currency Account, created with it. */
  openingBalance: number | null
  openingDate: string | null
}

export const listMultiCurrencyAccounts = (signal?: AbortSignal) =>
  apiFetch<MultiCurrencyAccount[]>('/api/multi-currency-accounts', { signal })

export const createMultiCurrencyAccount = (group: NewMultiCurrencyAccount) =>
  apiFetch<MultiCurrencyAccount>('/api/multi-currency-accounts', { method: 'POST', body: group })

export const renameMultiCurrencyAccount = (id: string, name: string) =>
  apiFetch<MultiCurrencyAccount>(`/api/multi-currency-accounts/${id}`, { method: 'PATCH', body: { name } })

/** Every Owner something can be assigned to: the Family first, then each Member. */
export const listOwners = (signal?: AbortSignal) => apiFetch<Owner[]>('/api/owners', { signal })

// ---- Transactions ----

export type Transaction = {
  id: string
  /** null (with accountHidden) when the Transaction sits in another Member's personal Account (US37). */
  accountId: string | null
  accountName: string | null
  accountHidden: boolean
  /** ISO date, e.g. "2026-11-20" */
  date: string
  /** In the Account's currency; positive = Income, negative = Expense. */
  amount: number
  currency: Currency
  /** The amount in CHF at the rate of its date (= amount for CHF); null until a rate is available. */
  chfAmount: number | null
  /** A Transfer (a leg of money moved between one Owner's Accounts) is neither Income nor Expense. */
  type: TransactionType
  /** The original description (as entered, or as the bank wrote it); never replaced by the Display name. */
  description: string
  /** Friendly name shown instead of the description, when set. */
  displayName: string | null
  counterparty: string | null
  reference: string | null
  /** Foreign-currency purchase: what was paid in `originalCurrency` (any ISO code). */
  originalAmount: number | null
  originalCurrency: string | null
  note: string | null
  owner: Owner
  /** Imported Transactions keep their date, amount, description and original amount read-only. */
  origin: 'manual' | 'imported'
  /** null = Uncategorised. */
  category: CategoryRef | null
  /** The Tags on this Transaction, by name, each with the sources that put it there. */
  tags: TagRef[]
  /** Classification fields a Member set by hand (Rules leave them alone). */
  manuallySet: ('owner' | 'category' | 'tags' | 'displayName')[]
  /** The Source file an imported Transaction came from. */
  sourceFile: { id: string; fileName: string } | null
  /** The other leg of a Transfer or Contribution; it may sit in an Account the viewer can't see. */
  link: TransactionLink | null
}

export type TransactionType = 'income' | 'expense' | 'transfer'

/** The other leg; accountName is null (accountHidden) when it sits in an Account the viewer can't see. */
export type TransactionLink = {
  kind: 'transfer' | 'contribution'
  transactionId: string
  accountName: string | null
  accountHidden: boolean
}

export type NewTransaction = {
  accountId: string
  date: string | null
  amount: number | null
  description: string
  note: string | null
  /** Omit to use the Account's Owner. */
  owner?: OwnerRef
  /** Omit to leave it Uncategorised. */
  categoryId?: string
  /** Tags added by hand (date-range Tags are added automatically). */
  tagIds?: string[]
}

/**
 * Partial update: leave a field out to keep it; `note: ''` / `displayName: ''` clear them;
 * `uncategorise: true` removes the Category.
 */
export type TransactionChanges = {
  date?: string
  amount?: number
  description?: string
  displayName?: string
  note?: string
  owner?: OwnerRef
  categoryId?: string
  uncategorise?: true
  /** Add Tags by hand. */
  addTagIds?: string[]
  /** Remove Tags by hand; the removal sticks (the Tag's date range won't add it back). */
  removeTagIds?: string[]
}

export type TransactionSort = 'date-desc' | 'date-asc' | 'amount-asc' | 'amount-desc'

/** Filters for the Transactions list; all optional and combined with AND (never beyond what the Member can see). */
export type TransactionFilter = {
  accountId?: string
  /** "me", "family" or a Member id: the Transaction's Owner. */
  owner?: string
  /** This Category or any of its subcategories. */
  categoryId?: string
  /** Only Uncategorised Income and Expense. */
  uncategorised?: boolean
  tagId?: string
  /** Inclusive ISO dates. */
  from?: string
  to?: string
  /** Text in the description, Display name, counterparty, reference or note. */
  q?: string
  sort?: TransactionSort
  offset?: number
  limit?: number
}

/** One page of a list plus the number of items across all pages. */
export type Page<T> = { items: T[]; total: number }

/** The Transactions the current Member can see matching the filter, one page (newest first by default). */
export async function listTransactions(signal?: AbortSignal, filter: TransactionFilter = {}): Promise<Page<Transaction>> {
  const query = new URLSearchParams()
  for (const [key, value] of Object.entries(filter)) {
    if (value !== undefined && value !== '' && value !== false) query.set(key, String(value))
  }
  const qs = query.toString()
  const res = await fetch(`/api/transactions${qs ? `?${qs}` : ''}`, { signal })
  if (!res.ok) {
    if (res.status === 401) unauthorizedHandler?.()
    const problem = await readProblem(res)
    throw new ApiError(res.status, problem.errors, problem.title)
  }
  const items = (await res.json()) as Transaction[]
  return { items, total: Number(res.headers.get('X-Total-Count') ?? items.length) }
}

/**
 * The same Owner / Category / Tag change on several Transactions, all or nothing. A Transaction that can't take it
 * is reported in ApiError.fieldErrors under `transactions[<id>]` (see bulkErrorKey) and nothing is changed.
 */
export type BulkEdit = {
  transactionIds: string[]
  owner?: OwnerRef
  categoryId?: string
  uncategorise?: true
  addTagIds?: string[]
  removeTagIds?: string[]
}

export const bulkEditTransactions = (edit: BulkEdit) =>
  apiFetch<Transaction[]>('/api/transactions/bulk-edit', { method: 'POST', body: edit })

/** fieldErrors key of a Transaction refused by a bulk edit. */
export const bulkErrorKey = (transactionId: string) => `transactions[${transactionId}]`

/**
 * The Uncategorised queue: visible Income and Expense without a Category, newest first; `owner` = "me", "family" or
 * a Member id (the Transaction's Owner), omitted = everyone's.
 */
export const listUncategorised = (signal?: AbortSignal, owner?: string) =>
  apiFetch<Transaction[]>(
    `/api/transactions/uncategorised${owner ? `?owner=${encodeURIComponent(owner)}` : ''}`,
    { signal },
  )

export const createTransaction = (transaction: NewTransaction) =>
  apiFetch<Transaction>('/api/transactions', { method: 'POST', body: transaction })

export const updateTransaction = (id: string, changes: TransactionChanges) =>
  apiFetch<Transaction>(`/api/transactions/${id}`, { method: 'PATCH', body: changes })

export const deleteTransaction = (id: string) => apiFetch<void>(`/api/transactions/${id}`, { method: 'DELETE' })

/** Links two Transactions as a pair: a Transfer (same Owner) or a Contribution (different Owners). */
export const linkTransactions = (id: string, otherId: string) =>
  apiFetch<Transaction>(`/api/transactions/${id}/link`, { method: 'POST', body: { transactionId: otherId } })

/** Breaks a pair: both legs become Income or Expense again. */
export const unlinkTransaction = (id: string) =>
  apiFetch<Transaction>(`/api/transactions/${id}/link`, { method: 'DELETE' })

// ---- Imports and Source files ----

/** A statement file imported into an Account and kept. */
export type SourceFile = {
  id: string
  accountId: string
  fileName: string
  size: number
  importedAt: string
  transactionCount: number
}

export type ImportResult = {
  sourceFile: SourceFile
  importedCount: number
  skippedCount: number
  warnings: string[]
}

/**
 * Uploads a statement file into an Account; it is read by the Account's Integration.
 * A file that can't be imported throws ApiError 400 with `fieldErrors.file` saying why.
 */
export async function importStatement(accountId: string, file: File): Promise<ImportResult> {
  const form = new FormData()
  form.append('file', file)
  const res = await fetch(`/api/accounts/${accountId}/imports`, { method: 'POST', body: form })
  if (!res.ok) {
    if (res.status === 401) unauthorizedHandler?.()
    const problem = await readProblem(res)
    throw new ApiError(res.status, problem.errors, problem.title)
  }
  return (await res.json()) as ImportResult
}

/** The Source files of an Account, newest first. */
export const listSourceFiles = (accountId: string, signal?: AbortSignal) =>
  apiFetch<SourceFile[]>(`/api/accounts/${accountId}/source-files`, { signal })

/** Link that downloads the original file. */
export const sourceFileContentUrl = (id: string) => `/api/source-files/${id}/content`

/** Undo an import: deletes the Source file and every Transaction imported from it. */
export const deleteSourceFile = (id: string) => apiFetch<void>(`/api/source-files/${id}`, { method: 'DELETE' })

// ---- Categories ----

/** A Category as Transactions carry it; `path` is e.g. "Food › Eating out". */
export type CategoryRef = { id: string; name: string; path: string }

/** A node of the shared Category tree. Built-in (Contribution) Categories can't be changed. */
export type Category = CategoryRef & { parentId: string | null; isBuiltIn: boolean; builtInKey: string | null }

/** The whole tree as a flat list in display order: each Category directly followed by its subtree. */
export const listCategories = (signal?: AbortSignal) => apiFetch<Category[]>('/api/categories', { signal })

export const createCategory = (name: string, parentId: string | null) =>
  apiFetch<Category>('/api/categories', { method: 'POST', body: { name, parentId } })

export const renameCategory = (id: string, name: string) =>
  apiFetch<Category>(`/api/categories/${id}`, { method: 'PATCH', body: { name } })

/** parentId null = move to the top level. */
export const moveCategory = (id: string, parentId: string | null) =>
  apiFetch<Category>(`/api/categories/${id}/move`, { method: 'POST', body: { parentId } })

/** What happens to the Transactions that use a removed Category: moved to another one, or Uncategorised. */
export type RemovedCategoryTransactions = { moveTo: string } | { uncategorise: true }

export const removeCategory = (id: string, transactions: RemovedCategoryTransactions) => {
  const query = 'moveTo' in transactions ? `moveTo=${encodeURIComponent(transactions.moveTo)}` : 'uncategorise=true'
  return apiFetch<void>(`/api/categories/${id}?${query}`, { method: 'DELETE' })
}

// ---- Tags ----

/** Why a Tag is on a Transaction: added by hand, by the Tag's date range, or by a Rule. */
export type TagSource = 'manual' | 'dateRange' | 'rule'

/** A Tag as Transactions carry it. */
export type TagRef = { id: string; name: string; sources: TagSource[] }

/**
 * A shared Tag. With a date range (inclusive ISO dates) it is applied automatically to every Transaction in the
 * range; `owners` limits that to Transactions of those Owners (empty = every Owner).
 */
export type Tag = { id: string; name: string; dateFrom: string | null; dateTo: string | null; owners: Owner[] }

export type TagInput = { name: string; dateFrom: string | null; dateTo: string | null; owners: OwnerRef[] }

export const listTags = (signal?: AbortSignal) => apiFetch<Tag[]>('/api/tags', { signal })

export const createTag = (tag: TagInput) => apiFetch<Tag>('/api/tags', { method: 'POST', body: tag })

/** Replaces name, date range and Owner limit. */
export const updateTag = (id: string, tag: TagInput) => apiFetch<Tag>(`/api/tags/${id}`, { method: 'PUT', body: tag })

/** Takes the Tag off every Transaction. */
export const deleteTag = (id: string) => apiFetch<void>(`/api/tags/${id}`, { method: 'DELETE' })

// ---- Rules (/api/rules) ----

export type RuleField = 'description' | 'counterparty'
export type RuleMatch = 'contains' | 'equals'

/**
 * A shared Rule, in priority order (0 = first). `owner` null = the Rule doesn't set an Owner. `active` is false while
 * its author (the Member who last saved it) is deactivated: it doesn't run until someone saves it again.
 */
export type Rule = {
  id: string
  priority: number
  field: RuleField
  match: RuleMatch
  text: string
  category: CategoryRef | null
  tags: { id: string; name: string }[]
  owner: Owner | null
  author: { id: string; name: string }
  active: boolean
}

/** Create/replace body; also the shape of a suggested draft. At least one of categoryId, tagIds, owner. */
export type RuleInput = {
  field: RuleField
  match: RuleMatch
  text: string
  categoryId: string | null
  tagIds: string[]
  owner: OwnerRef | null
}

export type RuleRunResult = { checked: number; categorised: number }

export const listRules = (signal?: AbortSignal) => apiFetch<Rule[]>('/api/rules', { signal })

export const createRule = (rule: RuleInput) => apiFetch<Rule>('/api/rules', { method: 'POST', body: rule })

/** Replaces condition and actions; the saving Member becomes the author. */
export const updateRule = (id: string, rule: RuleInput) => apiFetch<Rule>(`/api/rules/${id}`, { method: 'PUT', body: rule })

export const deleteRule = (id: string) => apiFetch<void>(`/api/rules/${id}`, { method: 'DELETE' })

/** The new priority order: every Rule id once, first = runs first. */
export const reorderRules = (ruleIds: string[]) =>
  apiFetch<Rule[]>('/api/rules/order', { method: 'PUT', body: { ruleIds } })

/** Runs the Rules over the caller's visible Uncategorised Transactions. */
export const applyRules = () => apiFetch<RuleRunResult>('/api/rules/apply', { method: 'POST' })

/** A pre-filled Rule built from a Transaction (e.g. right after correcting its Category). Not saved. */
export const suggestRule = (transactionId: string, signal?: AbortSignal) =>
  apiFetch<RuleInput>(`/api/rules/suggestion?transactionId=${encodeURIComponent(transactionId)}`, { signal })

// ---- Members (Admin only: /api/admin/members) ----

/** A Member as the Admin sees them in member management. */
export type MemberSummary = { id: string; userName: string; isAdmin: boolean; isActive: boolean }

/**
 * One of a Member's Accounts, just enough to choose who gets it on deactivation. A Multi-currency account is
 * listed (and handed over) as a whole instead of its Accounts; its currency is the main currency.
 */
export type HandoverAccount = { id: string; name: string; currency: Currency; isMultiCurrencyAccount: boolean }

export type Handover =
  | { accountId: string; owner: OwnerRef }
  | { multiCurrencyAccountId: string; owner: OwnerRef }

export const listMembers = (signal?: AbortSignal) => apiFetch<MemberSummary[]>('/api/admin/members', { signal })

export const addMember = (userName: string, password: string) =>
  apiFetch<MemberSummary>('/api/admin/members', { method: 'POST', body: { userName, password } })

export const resetMemberPassword = (id: string, newPassword: string) =>
  apiFetch<void>(`/api/admin/members/${id}/password`, { method: 'POST', body: { newPassword } })

export const setMemberAdmin = (id: string, isAdmin: boolean) =>
  apiFetch<MemberSummary>(`/api/admin/members/${id}/admin`, { method: 'PUT', body: { isAdmin } })

export const listMemberAccountsForHandover = (id: string, signal?: AbortSignal) =>
  apiFetch<HandoverAccount[]>(`/api/admin/members/${id}/accounts`, { signal })

/** Accounts not listed in `handover` stay with the deactivated Member. */
export const deactivateMember = (id: string, handover: Handover[]) =>
  apiFetch<void>(`/api/admin/members/${id}/deactivate`, { method: 'POST', body: { handover } })

// ---- Reports (/api/reports) ----

/** Whose figures a report shows: the viewer's own Transactions, or Family-owned ones only. */
export type ReportOwner = 'me' | 'family'

/** A Category's CHF amount: `total` includes every Category below it, `direct` only what is assigned to it. */
export type CategoryTotal = {
  id: string
  name: string
  parentId: string | null
  path: string
  builtInKey: string | null
  total: number
  direct: number
}

/**
 * One side (Income or Expense) of a report, as positive CHF amounts. `categories` lists, in tree order, every
 * Category whose subtree holds amounts (drill in by `parentId`). Amounts without a CHF rate yet are in no total;
 * `pendingConversion` counts them.
 */
export type ReportSection = {
  total: number
  pendingConversion: number
  uncategorised: number
  contributions: number
  categories: CategoryTotal[]
}

export type MonthlyReport = {
  month: string // yyyy-MM
  owner: ReportOwner
  currency: 'CHF'
  income: ReportSection
  expense: ReportSection
  net: number
}

export const getMonthlyReport = (month: string, owner: ReportOwner, signal?: AbortSignal) =>
  apiFetch<MonthlyReport>(`/api/reports/monthly?month=${encodeURIComponent(month)}&owner=${owner}`, { signal })

/** `from`/`to` = the period counted (inclusive; null = open-ended, both null = all time). */
export type TagReport = {
  tag: { id: string; name: string; dateFrom: string | null; dateTo: string | null }
  owner: ReportOwner
  from: string | null
  to: string | null
  currency: 'CHF'
  income: ReportSection
  expense: ReportSection
  net: number
}

/**
 * The period of a Tag report: 'default' = the Tag's date range (all time when it has none), 'all' = all time,
 * or explicit bounds (yyyy-MM-dd, either may be empty).
 */
export type TagReportPeriod = 'default' | 'all' | { from: string; to: string }

export function getTagReport(tagId: string, owner: ReportOwner, period: TagReportPeriod, signal?: AbortSignal) {
  const query = new URLSearchParams({ owner })
  if (period === 'all') query.set('allTime', 'true')
  else if (period !== 'default') {
    if (period.from) query.set('from', period.from)
    if (period.to) query.set('to', period.to)
  }
  return apiFetch<TagReport>(`/api/reports/tag/${tagId}?${query}`, { signal })
}

/** Link to the Tag report screen (used by the Tags list and Transaction Tag chips). */
export const tagReportPath = (tagId: string) => `/reports/tag/${tagId}`