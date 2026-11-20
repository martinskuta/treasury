import { Link } from 'react-router'
import { isAdmin } from '../lib/api'
import { useMe } from '../lib/auth'

/** Entry point for settings screens; Admin-only ones are listed only for Admins. */
export function SettingsPage() {
  const me = useMe()
  return (
    <>
      <h1>Settings</h1>
      <section className="card">
        <ul className="settings-links">
          <li>
            <Link to="/settings/categories">Categories</Link> — the Family’s Category tree
          </li>
          <li>
            <Link to="/settings/tags">Tags</Link> — shared Tags, including date-range Tags applied automatically
          </li>
          <li>
            <Link to="/account/password">Change your password</Link>
          </li>
          {isAdmin(me) && (
            <li>
              <Link to="/settings/members">Members</Link> — add Members, reset passwords, Admin role, deactivate
            </li>
          )}
        </ul>
      </section>
    </>
  )
}
