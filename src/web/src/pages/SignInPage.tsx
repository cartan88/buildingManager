import { useState, type FormEvent } from 'react'
import { api, logoUrl, type Branding } from '../api'
import { ErrorBanner, Field, useSubmit } from '../ui'

/**
 * Shown instead of the app until you sign in. On the very first visit (no account yet) it creates
 * the owner account instead.
 */
export default function SignInPage({ setup, branding, onSignedIn }: { setup: boolean; branding?: Branding; onSignedIn: () => void }) {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const { error, setError, saving, run } = useSubmit()
  const logo = branding && logoUrl(branding)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (setup && password !== confirm) { setError("The two passwords don't match."); return }
    if (await run(() => api.post(setup ? '/auth/setup' : '/auth/login', { username, password }))) onSignedIn()
  }

  return (
    <div className="signin">
      <form className="signin-card" onSubmit={submit}>
        {logo && <img src={logo} alt="" className="signin-logo" />}
        <h1>{branding?.name ?? 'Building Manager'}</h1>
        {setup
          ? <p className="muted">Create your sign-in. You'll need it every time you open the app.</p>
          : <p className="muted">Sign in to continue.</p>}

        <Field label="Username">
          <input value={username} onChange={e => setUsername(e.target.value)} autoComplete="username" maxLength={100} required autoFocus />
        </Field>
        <Field label="Password" hint={setup ? 'At least 8 characters' : undefined}>
          <input type="password" value={password} onChange={e => setPassword(e.target.value)}
            autoComplete={setup ? 'new-password' : 'current-password'} minLength={setup ? 8 : undefined} required />
        </Field>
        {setup && (
          <Field label="Confirm password">
            <input type="password" value={confirm} onChange={e => setConfirm(e.target.value)} autoComplete="new-password" required />
          </Field>
        )}

        <button type="submit" disabled={saving}>{setup ? 'Create account' : 'Sign in'}</button>
        <ErrorBanner message={error} />
        {!setup && <p className="muted small">Forgot your password? See "Forgot the password" in the README for how to reset it on this PC.</p>}
      </form>
    </div>
  )
}
