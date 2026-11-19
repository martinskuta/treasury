import { useEffect, useState } from 'react'
import { Layout, type NavItem } from './components/Layout'
import { getHealth, type Health } from './lib/api'
import { formatAmount, formatDate } from './lib/format'

const nav: NavItem[] = [{ label: 'Home', href: '/' }]

type HealthState = { kind: 'loading' } | { kind: 'ok'; health: Health } | { kind: 'error'; message: string }

export default function App() {
  const [state, setState] = useState<HealthState>({ kind: 'loading' })
  const [today] = useState(() => new Date())

  useEffect(() => {
    const controller = new AbortController()
    getHealth(controller.signal)
      .then(health => setState({ kind: 'ok', health }))
      .catch((e: unknown) => {
        if (!controller.signal.aborted) setState({ kind: 'error', message: String(e) })
      })
    return () => controller.abort()
  }, [])

  return (
    <Layout nav={nav}>
      <h1>Treasury</h1>
      <section className="card">
        <h2>API status</h2>
        {state.kind === 'loading' && <p>Checking…</p>}
        {state.kind === 'error' && <p className="status status-bad">Unreachable ({state.message})</p>}
        {state.kind === 'ok' && (
          <>
            <p className={`status ${state.health.status === 'Healthy' ? 'status-good' : 'status-bad'}`}>
              {state.health.status}
            </p>
            <ul>
              {state.health.checks.map(c => (
                <li key={c.name}>
                  {c.name}: {c.status}
                </li>
              ))}
            </ul>
          </>
        )}
      </section>
      <section className="card">
        <h2>Formatting</h2>
        <p>
          {formatAmount(1234.5)} CHF on {formatDate(today)}
        </p>
      </section>
    </Layout>
  )
}
