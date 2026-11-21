import { createContext, useContext } from 'react'
import type { Me } from './api'

export type AuthState =
  | { status: 'loading' }
  | { status: 'signedOut' }
  | { status: 'signedIn'; me: Me }

export type Auth = AuthState & {
  login: (userName: string, password: string) => Promise<void>
  logout: () => Promise<void>
  /** Replace the signed-in Member after they changed their own details (e.g. their name). */
  updateMe: (me: Me) => void
}

export const AuthContext = createContext<Auth | null>(null)

/** The session (from AuthProvider). */
export function useAuth(): Auth {
  const auth = useContext(AuthContext)
  if (!auth) throw new Error('useAuth must be used inside <AuthProvider>')
  return auth
}

/** The logged-in Member; only call this inside routes guarded by RequireAuth. */
export function useMe(): Me {
  const auth = useAuth()
  if (auth.status !== 'signedIn') throw new Error('useMe used outside RequireAuth')
  return auth.me
}
