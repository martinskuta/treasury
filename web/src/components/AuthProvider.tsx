import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import * as api from '../lib/api'
import { AuthContext, type Auth, type AuthState } from '../lib/auth'

/** Loads the session once (GET /api/auth/me) and drops it whenever any API call returns 401. */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ status: 'loading' })

  useEffect(() => {
    const controller = new AbortController()
    api
      .getMe(controller.signal)
      .then(me => setState(me ? { status: 'signedIn', me } : { status: 'signedOut' }))
      .catch(() => {
        if (!controller.signal.aborted) setState({ status: 'signedOut' })
      })
    api.onUnauthorized(() => setState({ status: 'signedOut' }))
    return () => {
      controller.abort()
      api.onUnauthorized(undefined)
    }
  }, [])

  const login = useCallback(async (userName: string, password: string) => {
    const me = await api.login(userName, password)
    setState({ status: 'signedIn', me })
  }, [])

  const logout = useCallback(async () => {
    try {
      await api.logout()
    } finally {
      setState({ status: 'signedOut' })
    }
  }, [])

  const value = useMemo<Auth>(() => ({ ...state, login, logout }), [state, login, logout])
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
