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
