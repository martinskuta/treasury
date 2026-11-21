import { useEffect, useState } from 'react'

/** Where one Dashboard data source is: still loading, failed, or ready. */
export type Load<T> = { status: 'loading' } | { status: 'failed' } | { status: 'ready'; data: T }

/**
 * Loads data for `key` (e.g. "reports|family") and reloads whenever the key changes; a null key loads nothing and
 * stays 'loading'. Results for an older key are never shown, so switching the Owner filter can't show stale figures.
 * `load` must depend only on what the key describes.
 */
export function useLoad<T>(key: string | null, load: (signal: AbortSignal) => Promise<T>): Load<T> {
  const [result, setResult] = useState<{ key: string; load: Load<T> } | null>(null)

  useEffect(() => {
    if (key === null) return
    const controller = new AbortController()
    load(controller.signal).then(
      data => setResult({ key, load: { status: 'ready', data } }),
      () => {
        if (!controller.signal.aborted) setResult({ key, load: { status: 'failed' } })
      },
    )
    return () => controller.abort()
    // `load` is a fresh closure every render; the key says when the data it fetches changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key])

  return result !== null && result.key === key ? result.load : { status: 'loading' }
}
