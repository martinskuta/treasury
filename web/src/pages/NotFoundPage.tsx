import { Link } from 'react-router'
import '../styles/not-found.css'

export function NotFoundPage() {
  return (
    <section className="ui-component not-found">
      <p className="not-found-code" aria-hidden="true">
        404
      </p>
      <h1 className="page-title">Page not found</h1>
      <p className="not-found-text">There’s nothing at this address.</p>
      <Link className="button-primary" to="/">
        Back to the Dashboard
      </Link>
    </section>
  )
}
