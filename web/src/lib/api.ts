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

export type Integration = { key: string; name: string; canProvideIdentifier: boolean }

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

/** Every Owner something can be assigned to: the Family first, then each Member. */
export const listOwners = (signal?: AbortSignal) => apiFetch<Owner[]>('/api/owners', { signal })

// ---- Transactions ----

export type Transaction = {
  id: string
  accountId: string
  accountName: string
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

export type TransactionLink = { kind: 'transfer' | 'contribution'; transactionId: string }

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

/** Filters for the Transactions list. */
export type TransactionFilter = { tagId?: string }

/** The Transactions the current Member can see, newest first. */
export const listTransactions = (signal?: AbortSignal, filter: TransactionFilter = {}) => {
  const query = new URLSearchParams()
  if (filter.tagId) query.set('tagId', filter.tagId)
  const qs = query.toString()
  return apiFetch<Transaction[]>(`/api/transactions${qs ? `?${qs}` : ''}`, { signal })
}

/** The Uncategorised queue: visible Income and Expense without a Category, newest first. */
export const listUncategorised = (signal?: AbortSignal) =>
  apiFetch<Transaction[]>('/api/transactions/uncategorised', { signal })

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

// ---- Members (Admin only: /api/admin/members) ----

/** A Member as the Admin sees them in member management. */
export type MemberSummary = { id: string; userName: string; isAdmin: boolean; isActive: boolean }

/** One of a Member's Accounts, just enough to choose who gets it on deactivation. */
export type HandoverAccount = { id: string; name: string; currency: Currency }

export type Handover = { accountId: string; owner: OwnerRef }

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