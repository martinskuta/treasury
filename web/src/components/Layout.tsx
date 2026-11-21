import type { ReactNode } from 'react'
import { Link, NavLink } from 'react-router'

export type NavItem = {
  label: string
  href: string
  /** Shown at the right of the link in Expense red when above 0 (e.g. the Uncategorised count). */
  count?: number
}

type Props = {
  nav: NavItem[]
  /** Signed-in Member and log out, at the bottom of the sidebar (right end of the top bar on phones). */
  user?: ReactNode
  children: ReactNode
}

/**
 * App shell: fixed left sidebar (brand, nav with active state, Member at the bottom) and the main content.
 * Below 900px the sidebar becomes a top bar with a horizontally scrolling nav (styles/shell.css).
 */
export function Layout({ nav, user, children }: Props) {
  return (
    <div className="shell">
      <aside className="shell-side">
        <Link to="/" className="brand">
          <i className="brand-mark" aria-hidden="true">
            T
          </i>
          Treasury
        </Link>
        <nav className="shell-nav" aria-label="Main">
          <ul>
            {nav.map(item => (
              <li key={item.href}>
                <NavLink to={item.href} end={item.href === '/'}>
                  {item.label}
                  {item.count !== undefined && item.count > 0 && (
                    <span className="nav-count">{item.count}</span>
                  )}
                </NavLink>
              </li>
            ))}
          </ul>
        </nav>
        {user && <div className="shell-user">{user}</div>}
      </aside>
      <main className="shell-main">{children}</main>
    </div>
  )
}
