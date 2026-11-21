import { useEffect, useState } from 'react'
import { Link, Outlet, useLocation } from 'react-router'
import { listUncategorised } from '../lib/api'
import { useAuth, useMe } from '../lib/auth'
import { Layout, type NavItem } from './Layout'

/** Initials for the avatar: "anna.muster" -> "AM", "martin" -> "M". */
function initials(userName: string): string {
  const parts = userName.split(/[^\p{L}\p{N}]+/u).filter(Boolean)
  return parts
    .slice(0, 2)
    .map(p => p[0].toUpperCase())
    .join('')
}

/** Size of the Uncategorised queue (everyone's visible Transactions), refreshed on every navigation. */
function useUncategorisedCount(): number | undefined {
  const { pathname } = useLocation()
  const [count, setCount] = useState<number>()

  useEffect(() => {
    const controller = new AbortController()
    listUncategorised(controller.signal)
      .then(items => setCount(items.length))
      .catch(() => {
        // The count is a hint; a failed request just leaves the last known value.
      })
    return () => controller.abort()
  }, [pathname])

  return count
}

/** Layout route for every logged-in screen: shell + Member menu around the matched child route. */
export function AppShell() {
  const me = useMe()
  const { logout } = useAuth()
  const uncategorised = useUncategorisedCount()

  /** Screens in the sidebar. Add an entry here and a <Route> in App.tsx for each new screen. */
  const nav: NavItem[] = [
    { label: 'Dashboard', href: '/' },
    { label: 'Transactions', href: '/transactions' },
    { label: 'Uncategorised', href: '/uncategorised', count: uncategorised },
    { label: 'Monthly overview', href: '/overview' },
    { label: 'Tag report', href: '/reports/tag' },
    { label: 'Accounts', href: '/accounts' },
    { label: 'Settings', href: '/settings' },
  ]

  return (
    <Layout
      nav={nav}
      user={
        <>
          <Link to="/account/password" className="shell-user-link" title="Change password" aria-label={`${me.userName}, change password`}>
            <span className="shell-user-name">{me.userName}</span>
            <span className="avatar" aria-hidden="true">
              {initials(me.userName)}
            </span>
          </Link>
          <button type="button" className="shell-logout" onClick={() => void logout()}>
            Log out
          </button>
        </>
      }
    >
      <Outlet />
    </Layout>
  )
}
