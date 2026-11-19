import { useEffect, useState } from 'react'
import { getHealth, type Health } from '../lib/api'
import { formatAmount, formatDate } from '../lib/format'

type HealthState = { kind: 'loading' } | { kind: 'ok'; health: Health } | { kind: 'error'; message: string }

export function HomePage() {
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
    <>
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
    </>
  )
}
