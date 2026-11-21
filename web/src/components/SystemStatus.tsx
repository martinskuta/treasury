import { useEffect, useState } from 'react'
import { getHealth, type Health } from '../lib/api'
import { formatAmount, formatDate } from '../lib/format'
import { Amount } from './Amount'
import { UiComponent } from './UiComponent'

type HealthState = { kind: 'loading' } | { kind: 'ok'; health: Health } | { kind: 'error'; message: string }

/** API status and the formatting check (Settings → System): two UI components, laid out by the caller. */
export function SystemStatus() {
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
      <UiComponent title="API status">
        {state.kind === 'loading' && <p className="muted">Checking…</p>}
        {state.kind === 'error' && <p className="status status-bad">Unreachable ({state.message})</p>}
        {state.kind === 'ok' && (
          <>
            <p className={`status ${state.health.status === 'Healthy' ? 'status-good' : 'status-bad'}`}>
              {state.health.status}
            </p>
            <ul className="system-checks">
              {state.health.checks.map(c => (
                <li key={c.name}>
                  <span>{c.name}</span>
                  <span className={c.status === 'Healthy' ? 'inc' : 'exp'}>{c.status}</span>
                </li>
              ))}
            </ul>
          </>
        )}
      </UiComponent>
      <UiComponent title="Formatting">
        <p className="num">
          {formatAmount(1234.5)} CHF on {formatDate(today)}
        </p>
        <Amount value={1234.5} signed tone="auto" size="m" />
      </UiComponent>
    </>
  )
}
