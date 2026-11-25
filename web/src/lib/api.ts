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
export type Me = { id: string; userName: string; firstName: string; lastName: string | null; roles: string[] }

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

/** Change the logged-in Member's own name; returns the updated Member. */
export const changeMyName = (firstName: string, lastName: string) =>
  apiFetch<Me>('/api/auth/name', { method: 'PUT', body: { firstName, lastName } })

export const changePassword = (currentPassword: string, newPassword: string) =>
  apiFetch<void>('/api/auth/password', { method: 'POST', body: { currentPassword, newPassword } })

// ---- Owners ----

/** Who something belongs to: a Member or the Family. Requests send `{ kind: 'family' }` or `{ kind: 'member', memberId }`. */
export type OwnerRef = { kind: 'family' } | { kind: 'member'; memberId: string }

/** An Owner as the API returns it; `name` is the Member's label ("Martin Š. (martin)") or "Family". */
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

/** An ISO 4217 code; Accounts can be in any of `listCurrencies()` (every currency with an exchange rate). */
export type Currency = string

/** The currencies an Account can be in, alphabetical (served by the API so the list lives in one place). */
export const listCurrencies = (signal?: AbortSignal) => apiFetch<Currency[]>('/api/currencies', { signal })

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
  /** IBANs / last card digits its statements are checked against (empty for an Account in a Multi-currency account). */
  identifiers: string[]
  balance: number
  /** Set when the most recent statement's closing balance differs from the calculated balance on that date. */
  balanceWarning: BalanceWarning | null
  /** The Multi-currency account it belongs to; such an Account has no Ingest folder of its own. */
  multiCurrencyAccountId: string | null
  /** The balance in CHF at today's exchange rate (unchanged for CHF); null while the rate is missing. */
  balanceChf: number | null
  /** ISO date its data reaches: the later of its latest Transaction and its statements' latest closing balance; null when it has neither. */
  reach: string | null
  /** ISO date-time its most recent Source file was imported (its Multi-currency account's, for an Account in one); null when never imported. */
  lastImportedAt: string | null
  /** The reach is before the last day of the last complete calendar month, or missing. */
  lagging: boolean
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
  /** Only when the Integration can provide one. */
  identifiers: string[]
}

/** The Accounts the current Member can see: their own and the Family's. */
export const listAccounts = (signal?: AbortSignal) => apiFetch<Account[]>('/api/accounts', { signal })

export const createAccount = (account: NewAccount) =>
  apiFetch<Account>('/api/accounts', { method: 'POST', body: account })

/** Renames an Account and, when `identifiers` is given, replaces its Identifiers. */
export const updateAccount = (id: string, changes: { name: string; identifiers?: string[] }) =>
  apiFetch<Account>(`/api/accounts/${id}`, { method: 'PATCH', body: changes })

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
  /** IBANs its statements are checked against; its Accounts have none of their own. */
  identifiers: string[]
  /** The main-currency Account first. */
  accounts: Account[]
  /** Sum of its Accounts' balances in the main currency at today's rates; null while a rate is missing. */
  totalBalance: number | null
  /** The same total in CHF at today's rates; null while a rate is missing. */
  totalBalanceChf: number | null
  /** ISO date its data reaches: the latest reach among its Accounts; null when none has one. */
  reach: string | null
  /** ISO date-time its most recent Source file was imported; null when never imported. */
  lastImportedAt: string | null
  /** The reach is before the last day of the last complete calendar month, or missing. */
  lagging: boolean
}

export type NewMultiCurrencyAccount = {
  name: string
  owner: OwnerRef
  integrationKey: string
  mainCurrency: Currency
  /** Of the main-currency Account, created with it. */
  openingBalance: number | null
  openingDate: string | null
  /** Only when the Integration can provide one. */
  identifiers: string[]
}

export const listMultiCurrencyAccounts = (signal?: AbortSignal) =>
  apiFetch<MultiCurrencyAccount[]>('/api/multi-currency-accounts', { signal })

export const createMultiCurrencyAccount = (multiCurrencyAccount: NewMultiCurrencyAccount) =>
  apiFetch<MultiCurrencyAccount>('/api/multi-currency-accounts', { method: 'POST', body: multiCurrencyAccount })

/** Renames a Multi-currency account and, when `identifiers` is given, replaces its Identifiers. */
export const updateMultiCurrencyAccount = (id: string, changes: { name: string; identifiers?: string[] }) =>
  apiFetch<MultiCurrencyAccount>(`/api/multi-currency-accounts/${id}`, { method: 'PATCH', body: changes })

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
  /** A Transfer (money moved between one Owner's Accounts) or Reimbursement (one Owner paying another back) is neither Income nor Expense. */
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
  manuallySet: ('owner' | 'category' | 'tags' | 'displayName' | 'linkKind')[]
  /** The Source file an imported Transaction came from. */
  sourceFile: { id: string; fileName: string } | null
  /** The other leg of a Transfer, Contribution or Reimbursement; it may sit in an Account the viewer can't see. */
  link: TransactionLink | null
  /**
   * For a Transaction on another Owner's Account: how far it is paid back (Amount owed). null for any other
   * Transaction, one before the Family's start date, or one without a CHF rate yet.
   */
  paidBack: PaidBack | null
}

export type TransactionType = 'income' | 'expense' | 'transfer' | 'reimbursement'

/** `outstanding` = the CHF amount still open. */
export type PaidBack = { status: 'paidBack' | 'partlyPaidBack' | 'outstanding'; outstanding: number }

/** The kinds a pair with different Owners can be switched between. */
export type SwitchableLinkKind = 'contribution' | 'reimbursement'

/** The other leg; accountName is null (accountHidden) when it sits in an Account the viewer can't see. */
export type TransactionLink = {
  kind: 'transfer' | SwitchableLinkKind
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
  /** Only Transactions on another Owner's Account that are outstanding or partly paid back. */
  notPaidBack?: boolean
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

/**
 * Links two Transactions as a pair: a Transfer (same Owner); with different Owners a Reimbursement (the Family pays
 * a Member) or a Contribution.
 */
export const linkTransactions = (id: string, otherId: string) =>
  apiFetch<Transaction>(`/api/transactions/${id}/link`, { method: 'POST', body: { transactionId: otherId } })

/** Breaks a pair: both legs become Income or Expense again. */
export const unlinkTransaction = (id: string) =>
  apiFetch<Transaction>(`/api/transactions/${id}/link`, { method: 'DELETE' })

/** Makes a pair with different Owners a Contribution or a Reimbursement; the choice sticks. */
export const switchLinkKind = (id: string, kind: SwitchableLinkKind) =>
  apiFetch<Transaction>(`/api/transactions/${id}/link/kind`, { method: 'PUT', body: { kind } })

// ---- Amounts owed ----

/** `debtor` owes `creditor` `amount` CHF (0 = even). `pendingConversion` = Transactions between them without a CHF rate yet. */
export type AmountOwed = { debtor: Owner; creditor: Owner; amount: number; pendingConversion: number }

/**
 * What Owners owe each other, as far as the Member may see: every pair with the Family, and pairs of two Members
 * only when the Member is one of them. `startDate` = the Family's start date (null = every Transaction counts).
 */
export type AmountsOwed = { currency: 'CHF'; startDate: string | null; amounts: AmountOwed[] }

export const getAmountsOwed = (signal?: AbortSignal) => apiFetch<AmountsOwed>('/api/amounts-owed', { signal })

// ---- Family settings ----

/** `amountOwedStartDate`: only Transactions from this ISO date on count towards Amounts owed; null = all. */
export type FamilySettings = { amountOwedStartDate: string | null }

export const getFamilySettings = (signal?: AbortSignal) => apiFetch<FamilySettings>('/api/settings', { signal })

export const saveFamilySettings = (settings: FamilySettings) =>
  apiFetch<FamilySettings>('/api/settings', { method: 'PUT', body: settings })

// ---- Imports and Source files ----

/**
 * A statement file imported and kept: into one Account, or into a Multi-currency account (then its Transactions sit
 * in its Accounts of their currencies). Exactly one of the two ids is set.
 */
export type SourceFile = {
  id: string
  accountId: string | null
  multiCurrencyAccountId: string | null
  fileName: string
  size: number
  importedAt: string
  transactionCount: number
}

/** An Account an import into a Multi-currency account created for a currency it had no Account for yet. */
export type CreatedAccount = { id: string; name: string; currency: Currency }

export type ImportResult = {
  sourceFile: SourceFile
  importedCount: number
  skippedCount: number
  warnings: string[]
  createdAccounts: CreatedAccount[]
}

/** What statements are imported into: a plain Account or a Multi-currency account. */
export type ImportTarget = { kind: 'account' | 'multiCurrencyAccount'; id: string }

const importTargetPath = (target: ImportTarget) =>
  target.kind === 'account' ? `/api/accounts/${target.id}` : `/api/multi-currency-accounts/${target.id}`

/**
 * Uploads a statement file into an Account or a Multi-currency account; it is read by its Integration. Into a
 * Multi-currency account, each row goes to the Account of its currency (created when missing).
 * A file that can't be imported throws ApiError 400 with `fieldErrors.file` saying why.
 */
export async function importStatement(target: ImportTarget, file: File): Promise<ImportResult> {
  const form = new FormData()
  form.append('file', file)
  const res = await fetch(`${importTargetPath(target)}/imports`, { method: 'POST', body: form })
  if (!res.ok) {
    if (res.status === 401) unauthorizedHandler?.()
    const problem = await readProblem(res)
    throw new ApiError(res.status, problem.errors, problem.title)
  }
  return (await res.json()) as ImportResult
}

/** The Source files imported into an Account or a Multi-currency account, newest first. */
export const listSourceFiles = (target: ImportTarget, signal?: AbortSignal) =>
  apiFetch<SourceFile[]>(`${importTargetPath(target)}/source-files`, { signal })

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
export type TagSource = 'manual' | 'dateRange' | 'rule' | 'integration'

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
export type MemberSummary = {
  id: string
  userName: string
  firstName: string
  lastName: string | null
  isAdmin: boolean
  isActive: boolean
}

/**
 * One of a Member's Accounts, just enough to choose who gets it on deactivation. A Multi-currency account is
 * listed (and handed over) as a whole instead of its Accounts; its currency is the main currency.
 */
export type HandoverAccount = { id: string; name: string; currency: Currency; isMultiCurrencyAccount: boolean }

export type Handover =
  | { accountId: string; owner: OwnerRef }
  | { multiCurrencyAccountId: string; owner: OwnerRef }

export const listMembers = (signal?: AbortSignal) => apiFetch<MemberSummary[]>('/api/admin/members', { signal })

export type NewMember = { userName: string; firstName: string; lastName: string; password: string }

export const addMember = (member: NewMember) =>
  apiFetch<MemberSummary>('/api/admin/members', { method: 'POST', body: member })

/** Rename any Member, deactivated ones included. */
export const setMemberName = (id: string, firstName: string, lastName: string) =>
  apiFetch<MemberSummary>(`/api/admin/members/${id}/name`, { method: 'PUT', body: { firstName, lastName } })

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

/**
 * One month's totals, counted like the Monthly overview: `income` and `expense` are positive CHF amounts,
 * `net` = income - expense. `pendingConversion` counts the amounts (both sides) without a CHF rate yet.
 */
export type MonthTotals = {
  month: string // yyyy-MM
  income: number
  expense: number
  net: number
  pendingConversion: number
}

/**
 * The six months ending at the Owner filter's end month, oldest first. `endMonth` = the month of the Owner filter's
 * latest Transaction, never later than the last complete calendar month (which it is without Transactions).
 */
export type RecentMonthsReport = {
  owner: ReportOwner
  currency: 'CHF'
  endMonth: string // yyyy-MM
  months: MonthTotals[]
}

export const getRecentMonthsReport = (owner: ReportOwner, signal?: AbortSignal) =>
  apiFetch<RecentMonthsReport>(`/api/reports/recent-months?owner=${owner}`, { signal })

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