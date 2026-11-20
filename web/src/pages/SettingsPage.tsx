import { Link } from 'react-router'

/** Settings: links to the Family-wide settings screens. */
export function SettingsPage() {
  return (
    <>
      <h1>Settings</h1>
      <section className="card">
        <ul className="settings-links">
          <li>
            <Link to="/settings/categories">Categories</Link>
          </li>
        </ul>
      </section>
    </>
  )
}
