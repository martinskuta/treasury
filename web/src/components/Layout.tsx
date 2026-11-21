import { useEffect, useRef, type ReactNode } from 'react'
import { Link, NavLink, useLocation } from 'react-router'

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
  const navRef = useRef<HTMLElement>(null)
  const { pathname } = useLocation()

  // Phone top bar: keep the active link in view in the sideways-scrolling nav (no-op on the desktop sidebar).
  useEffect(() => {
    const centreActive = () => {
      const navElement = navRef.current
      const active = navElement?.querySelector<HTMLElement>('a.active')
      if (!navElement || !active || navElement.scrollWidth <= navElement.clientWidth) return
      const left = active.getBoundingClientRect().left - navElement.getBoundingClientRect().left + navElement.scrollLeft
      navElement.scrollLeft = left - (navElement.clientWidth - active.offsetWidth) / 2
    }
    centreActive()
    // Link widths change once Inter has loaded.
    void document.fonts.ready.then(centreActive)
  }, [pathname])

  return (
    <div className="shell">
      <aside className="shell-side">
        <Link to="/" className="brand">
          <i className="brand-mark" aria-hidden="true">
            T
          </i>
          Treasury
        </Link>
        <nav ref={navRef} className="shell-nav" aria-label="Main">
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
