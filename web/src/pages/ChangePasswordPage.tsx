import { useState, type FormEvent } from 'react'
import { FormField } from '../components/FormField'
import { changePassword } from '../lib/api'
import { toErrors, type Errors } from '../lib/errors'
import '../styles/settings.css'

export function ChangePasswordPage() {
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [errors, setErrors] = useState<Errors>({})
  const [done, setDone] = useState(false)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setDone(false)
    if (newPassword !== confirm) {
      setErrors({ confirm: ['The new passwords don’t match.'] })
      return
    }
    setBusy(true)
    setErrors({})
    try {
      await changePassword(currentPassword, newPassword)
      setDone(true)
      setCurrentPassword('')
      setNewPassword('')
      setConfirm('')
    } catch (err) {
      setErrors(toErrors(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <h1 className="page-title">Change password</h1>
      <form className="ui-component form settings-narrow" onSubmit={submit}>
        <FormField
          label="Current password"
          name="currentPassword"
          type="password"
          autoComplete="current-password"
          required
          value={currentPassword}
          errors={errors.currentPassword}
          onChange={e => setCurrentPassword(e.target.value)}
        />
        <FormField
          label="New password"
          name="newPassword"
          type="password"
          autoComplete="new-password"
          required
          value={newPassword}
          errors={errors.newPassword}
          onChange={e => setNewPassword(e.target.value)}
        />
        <FormField
          label="Repeat new password"
          name="confirm"
          type="password"
          autoComplete="new-password"
          required
          value={confirm}
          errors={errors.confirm}
          onChange={e => setConfirm(e.target.value)}
        />
        {errors.form && (
          <p className="form-error" role="alert">
            {errors.form[0]}
          </p>
        )}
        {done && (
          <p className="status status-good" role="status">
            Your password has been changed.
          </p>
        )}
        <button type="submit" className="button-primary" disabled={busy}>
          Change password
        </button>
      </form>
    </>
  )
}
