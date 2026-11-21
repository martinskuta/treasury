import { useState, type FormEvent } from 'react'
import { FormField } from '../components/FormField'
import { changeMyName, changePassword } from '../lib/api'
import { useAuth, useMe } from '../lib/auth'
import { toErrors, type Errors } from '../lib/errors'
import '../styles/settings.css'

/** Your account: the logged-in Member's own name and password. */
export function AccountPage() {
  const me = useMe()
  return (
    <>
      <h1 className="page-title">Your account</h1>
      <p className="settings-intro">You log in as {me.userName}.</p>
      <NameForm />
      <PasswordForm />
    </>
  )
}

function NameForm() {
  const me = useMe()
  const { updateMe } = useAuth()
  const [firstName, setFirstName] = useState(me.firstName)
  const [lastName, setLastName] = useState(me.lastName ?? '')
  const [errors, setErrors] = useState<Errors>({})
  const [done, setDone] = useState(false)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setDone(false)
    setBusy(true)
    setErrors({})
    try {
      const updated = await changeMyName(firstName, lastName)
      updateMe(updated)
      setFirstName(updated.firstName)
      setLastName(updated.lastName ?? '')
      setDone(true)
    } catch (err) {
      setErrors(toErrors(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="settings-section" aria-labelledby="account-name">
      <h2 id="account-name" className="section-title">
        Name
      </h2>
      <form className="ui-component form settings-narrow" onSubmit={submit}>
        <FormField
          label="First name"
          name="firstName"
          autoComplete="given-name"
          required
          value={firstName}
          errors={errors.firstName}
          onChange={e => setFirstName(e.target.value)}
        />
        <FormField
          label="Last name (optional)"
          name="lastName"
          autoComplete="family-name"
          value={lastName}
          errors={errors.lastName}
          onChange={e => setLastName(e.target.value)}
        />
        {errors.form && (
          <p className="form-error" role="alert">
            {errors.form[0]}
          </p>
        )}
        {done && (
          <p className="status status-good" role="status">
            Your name has been saved.
          </p>
        )}
        <button type="submit" className="button-primary" disabled={busy}>
          Save name
        </button>
      </form>
    </section>
  )
}

function PasswordForm() {
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
    <section className="settings-section" aria-labelledby="account-password">
      <h2 id="account-password" className="section-title">
        Password
      </h2>
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
    </section>
  )
}
