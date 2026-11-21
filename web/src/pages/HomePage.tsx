import { SystemStatus } from '../components/SystemStatus'

/** Placeholder at `/` until the Dashboard (#29) replaces it; the same content lives in Settings → System. */
export function HomePage() {
  return (
    <>
      <h1>Treasury</h1>
      <div className="ui-grid system-grid">
        <SystemStatus />
      </div>
    </>
  )
}
