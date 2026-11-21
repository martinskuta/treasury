import { Link } from 'react-router'
import { SystemStatus } from '../components/SystemStatus'
import { isAdmin } from '../lib/api'
import { useMe } from '../lib/auth'
import '../styles/settings.css'

function SettingsTile({ to, icon, title, children }: { to: string; icon: string; title: string; children: string }) {
  return (
    <Link className="settings-tile" to={to}>
      <span className="settings-tile-icon" aria-hidden="true">
        {icon}
      </span>
      <span className="settings-tile-text">
        <strong>{title}</strong>
        <small>{children}</small>
      </span>
      <span className="settings-tile-chevron" aria-hidden="true">
        ›
      </span>
    </Link>
  )
}

/** Entry point for settings screens (Admin-only ones are listed only for Admins), plus the System checks. */
export function SettingsPage() {
  const me = useMe()
  return (
    <div className="settings-page">
      <h1 className="page-title">Settings</h1>
      <nav className="settings-tiles" aria-label="Settings">
        <SettingsTile to="/settings/categories" icon="C" title="Categories">
          The Family’s Category tree
        </SettingsTile>
        <SettingsTile to="/settings/tags" icon="T" title="Tags">
          Shared Tags, including date-range Tags applied automatically
        </SettingsTile>
        <SettingsTile to="/settings/rules" icon="R" title="Rules">
          Categorise, tag and assign imported Transactions automatically
        </SettingsTile>
        <SettingsTile to="/account" icon="Y" title="Your account">
          Your name and the password you log in with
        </SettingsTile>
        {isAdmin(me) && (
          <SettingsTile to="/settings/members" icon="M" title="Members">
            Add Members, names, reset passwords, Admin role, deactivate
          </SettingsTile>
        )}
      </nav>
      <section className="settings-section" aria-labelledby="settings-system">
        <h2 id="settings-system" className="section-title">
          System
        </h2>
        <div className="ui-grid system-grid">
          <SystemStatus />
        </div>
      </section>
    </div>
  )
}
