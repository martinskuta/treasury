import { Link, Outlet } from 'react-router'
import { useAuth, useMe } from '../lib/auth'
import { Layout, type NavItem } from './Layout'

/** Screens in the sidebar. Add an entry here and a <Route> in App.tsx for each new screen. */
const nav: NavItem[] = [
  { label: 'Home', href: '/' },
  { label: 'Transactions', href: '/transactions' },
  { label: 'Accounts', href: '/accounts' },
]

/** Layout route for every logged-in screen: shell + Member menu around the matched child route. */
export function AppShell() {
  const me = useMe()
  const { logout } = useAuth()

  return (
    <Layout
      nav={nav}
      headerEnd={
        <>
          <Link to="/account/password" className="header-user" title="Change password">
            {me.userName}
          </Link>
          <button type="button" className="button-secondary" onClick={() => void logout()}>
            Log out
          </button>
        </>
      }
    >
      <Outlet />
    </Layout>
  )
}
