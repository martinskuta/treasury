import { Navigate, Outlet, useLocation } from 'react-router'
import { useAuth } from '../lib/auth'

/** Route guard: signed-out visitors go to /login, which sends them back here after logging in. */
export function RequireAuth() {
  const auth = useAuth()
  const location = useLocation()

  if (auth.status === 'loading') return <p className="page-loading">Loading…</p>
  if (auth.status === 'signedOut') {
    const from = location.pathname + location.search
    return <Navigate to="/login" replace state={from === '/' ? undefined : { from }} />
  }
  return <Outlet />
}
