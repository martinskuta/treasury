import { useEffect, useState, type FormEvent } from 'react'
import { getFamilySettings, saveFamilySettings } from '../lib/api'
import { firstError, toErrors } from '../lib/errors'
import { formatDate } from '../lib/format'
import { FormField } from './FormField'
import { UiComponent } from './UiComponent'

/**
 * The Family's start date for Amounts owed (Settings): only Transactions from that day on count. Any Member can set
 * or clear it. Useful when imported history reaches back before paybacks were tracked.
 */
export function AmountOwedStartDate() {
  // undefined = not loaded yet; null = no start date.
  const [saved, setSaved] = useState<string | null | undefined>(undefined)
  const [date, setDate] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    getFamilySettings(controller.signal).then(
      s => {
        setSaved(s.amountOwedStartDate)
        setDate(s.amountOwedStartDate ?? '')
      },
      () => {
        if (!controller.signal.aborted) setError('Couldn’t load the start date.')
      },
    )
    return () => controller.abort()
  }, [])

  async function save(next: string | null) {
    setBusy(true)
    setError(null)
    try {
      const s = await saveFamilySettings({ amountOwedStartDate: next })
      setSaved(s.amountOwedStartDate)
      setDate(s.amountOwedStartDate ?? '')
    } catch (err) {
      setError(firstError(toErrors(err)) ?? 'Couldn’t save the start date.')
    } finally {
      setBusy(false)
    }
  }

  function submit(e: FormEvent) {
    e.preventDefault()
    void save(date || null)
  }

  return (
    <UiComponent title="Amounts owed" className="owed-start">
      <p className="settings-intro owed-start-intro">
        When someone pays for the Family, or for another Member, from their own Account, Treasury keeps track of what is
        owed and of the Reimbursements that pay it back.{' '}
        {saved === undefined
          ? ''
          : saved === null
            ? 'Every Transaction counts.'
            : `Only Transactions from ${formatDate(saved)} on count.`}
      </p>
      <form className="owed-start-form" onSubmit={submit}>
        <FormField
          label="Count Transactions from"
          type="date"
          name="amountOwedStartDate"
          value={date}
          disabled={saved === undefined}
          onChange={e => setDate(e.target.value)}
        />
        <div className="form-actions">
          <button type="submit" className="button-primary" disabled={busy || saved === undefined || date === (saved ?? '')}>
            Save
          </button>
          {saved && (
            <button type="button" className="button-secondary" disabled={busy} onClick={() => void save(null)}>
              Count everything
            </button>
          )}
        </div>
      </form>
      {error && (
        <p className="form-error" role="alert">
          {error}
        </p>
      )}
    </UiComponent>
  )
}
