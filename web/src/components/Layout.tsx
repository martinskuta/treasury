import { useState, type ReactNode } from 'react'

export type NavItem = { label: string; href: string }

type Props = {
  nav: NavItem[]
  children: ReactNode
}

/**
 * App shell: header + navigation + main content.
 * Phone width: navigation collapses behind a menu button. Desktop (>= 768px): navigation is a sidebar.
 */
export function Layout({ nav, children }: Props) {
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
      </header>
      <nav id="shell-nav" className={`shell-nav${menuOpen ? ' open' : ''}`}>
        <ul>
          {nav.map(item => (
            <li key={item.href}>
              <a href={item.href} onClick={() => setMenuOpen(false)}>
                {item.label}
              </a>
            </li>
          ))}
        </ul>
      </nav>
      <main className="shell-main">{children}</main>
    </div>
  )
}
