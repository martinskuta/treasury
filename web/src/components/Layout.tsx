import { useState, type ReactNode } from 'react'
import { NavLink } from 'react-router'

export type NavItem = { label: string; href: string }

type Props = {
  nav: NavItem[]
  /** Shown at the right end of the header (the Member menu). */
  headerEnd?: ReactNode
  children: ReactNode
}

/**
 * App shell: header + navigation + main content.
 * Phone width: navigation collapses behind a menu button. Desktop (>= 768px): navigation is a sidebar.
 */
export function Layout({ nav, headerEnd, children }: Props) {
  const [menuOpen, setMenuOpen] = useState(false)

  return (
    <div className="shell">
      <header className="shell-header">
        <button
          type="button"
          className="menu-button"
          aria-label="Menu"
          aria-expanded={menuOpen}
          aria-controls="shell-nav"
          onClick={() => setMenuOpen(open => !open)}
        >
          ☰
        </button>
        <span className="brand">Treasury</span>
        {headerEnd && <div className="shell-header-end">{headerEnd}</div>}
      </header>
      <nav id="shell-nav" className={`shell-nav${menuOpen ? ' open' : ''}`}>
        <ul>
          {nav.map(item => (
            <li key={item.href}>
              <NavLink to={item.href} end={item.href === '/'} onClick={() => setMenuOpen(false)}>
                {item.label}
              </NavLink>
            </li>
          ))}
        </ul>
      </nav>
      <main className="shell-main">{children}</main>
    </div>
  )
}
