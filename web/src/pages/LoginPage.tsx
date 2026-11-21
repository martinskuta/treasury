import { useState, type FormEvent } from 'react'
import { Navigate, useLocation } from 'react-router'
import { FormField } from '../components/FormField'
import { ApiError } from '../lib/api'
import { useAuth } from '../lib/auth'

export function LoginPage() {
  const auth = useAuth()
  const location = useLocation()
  const from = (location.state as { from?: string } | null)?.from ?? '/'
  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)

  if (auth.status === 'signedIn') return <Navigate to={from} replace />

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(undefined)
    try {
      await auth.login(userName, password)
    } catch (err) {
      setError(err instanceof ApiError && err.status === 401 ? 'Wrong username or password.' : 'Login failed, try again.')
      setBusy(false)
    }
  }

  return (
    <main className="login">
      <form className="ui-component login-card" onSubmit={submit}>
        <h1>Treasury</h1>
        <FormField
          label="Username"
          name="userName"
          autoComplete="username"
          autoCapitalize="none"
          required
          value={userName}
          onChange={e => setUserName(e.target.value)}
        />
        <FormField
          label="Password"
          name="password"
          type="password"
          autoComplete="current-password"
          required
          value={password}
          onChange={e => setPassword(e.target.value)}
        />
        {error && (
          <p className="form-error" role="alert">
            {error}
          </p>
        )}
        <button type="submit" className="button-primary" disabled={busy}>
          Log in
        </button>
      </form>
    </main>
  )
}
