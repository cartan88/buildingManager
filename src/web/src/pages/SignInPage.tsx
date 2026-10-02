import { useState, type FormEvent } from 'react'
import { api, logoUrl, type Branding } from '../api'
import { ErrorBanner, Field, useSubmit } from '../ui'

type Mode = 'signin' | 'setup' | 'forgot' | 'reset'

/** Opened from the link in a reset email: /reset-password?user=...&code=... */
function linkFromEmail() {
  if (window.location.pathname !== '/reset-password') return null
  const q = new URLSearchParams(window.location.search)
  return { username: q.get('user') ?? '', code: q.get('code') ?? '' }
}

/**
 * Shown instead of the app until you sign in. On the very first visit (no account yet) it creates the owner
 * account instead. "Forgot password?" emails a one-time code when email is set up in Settings.
 */
export default function SignInPage({ setup, emailReset, branding, onSignedIn }: {
  setup: boolean; emailReset: boolean; branding?: Branding; onSignedIn: () => void
}) {
  const fromEmail = setup ? null : linkFromEmail()
  const [mode, setMode] = useState<Mode>(setup ? 'setup' : fromEmail ? 'reset' : 'signin')
  const [username, setUsername] = useState(fromEmail?.username ?? '')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [code, setCode] = useState(fromEmail?.code ?? '')
  const [notice, setNotice] = useState<string>()
  const { error, setError, saving, run } = useSubmit()
  const logo = branding && logoUrl(branding)

  const go = (next: Mode) => { setMode(next); setError(undefined); setNotice(undefined); setPassword(''); setConfirm('') }
  const signedIn = () => { window.history.replaceState(null, '', '/'); onSignedIn() }

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if ((mode === 'setup' || mode === 'reset') && password !== confirm) { setError("The two passwords don't match."); return }
    if (mode === 'forgot') {
      let message = ''
      if (await run(async () => { message = (await api.post<{ message: string }>('/auth/forgot', { username })).message })) {
        go('reset')
        setNotice(message)
      }
      return
    }
    const ok = await run(() => mode === 'reset'
      ? api.post('/auth/reset', { username, code, newPassword: password })
      : api.post(mode === 'setup' ? '/auth/setup' : '/auth/login', { username, password }))
    if (ok) signedIn()
  }

  const intro = {
    setup: "Create your sign-in. You'll need it every time you open the app.",
    signin: 'Sign in to continue.',
    forgot: "Enter your username and we'll email you a code to reset your password.",
    reset: 'Enter the code from the email and choose a new password.',
  }[mode]
  const newPassword = mode === 'setup' || mode === 'reset'

  return (
    <div className="signin">
      <form className="signin-card" onSubmit={submit}>
        {logo && <img src={logo} alt="" className="signin-logo" />}
        <h1>{branding?.name ?? 'Building Manager'}</h1>
        <p className="muted">{intro}</p>
        {notice && <div className="notice">{notice}</div>}

        <Field label="Username">
          <input value={username} onChange={e => setUsername(e.target.value)} autoComplete="username" maxLength={100} required autoFocus={!fromEmail} />
        </Field>
        {mode === 'reset' && (
          <Field label="Reset code" hint="From the email, e.g. K7QF-9M2X">
            <input value={code} onChange={e => setCode(e.target.value)} autoComplete="one-time-code" required autoFocus={!!fromEmail && !code} />
          </Field>
        )}
        {mode !== 'forgot' && (
          <Field label={mode === 'reset' ? 'New password' : 'Password'} hint={newPassword ? 'At least 8 characters' : undefined}>
            <input type="password" value={password} onChange={e => setPassword(e.target.value)}
              autoComplete={newPassword ? 'new-password' : 'current-password'} minLength={newPassword ? 8 : undefined} required
              autoFocus={!!fromEmail && !!code} />
          </Field>
        )}
        {newPassword && (
          <Field label="Confirm password">
            <input type="password" value={confirm} onChange={e => setConfirm(e.target.value)} autoComplete="new-password" required />
          </Field>
        )}

        <button type="submit" disabled={saving}>
          {{ setup: 'Create account', signin: 'Sign in', forgot: 'Email me a code', reset: 'Set new password' }[mode]}
        </button>
        <ErrorBanner message={error} />

        {mode === 'signin' && (emailReset
          ? <button type="button" className="link signin-switch" onClick={() => go('forgot')}>Forgot password?</button>
          : <p className="muted small">Forgot your password? See "Forgot the password" in the README for how to reset it on this PC.</p>)}
        {mode === 'forgot' && <button type="button" className="link signin-switch" onClick={() => go('reset')}>I already have a code</button>}
        {(mode === 'forgot' || mode === 'reset') && (
          <button type="button" className="link signin-switch" onClick={() => { window.history.replaceState(null, '', '/'); go('signin') }}>Back to sign in</button>
        )}
      </form>
    </div>
  )
}
