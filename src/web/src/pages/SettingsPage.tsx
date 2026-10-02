import { useEffect, useState, type FormEvent } from 'react'
import { api, logoUrl, notifyBrandingChanged, uploadFile, type Branding, type BusinessProfile, type ExpenseCategory, type ExpenseCategoryKind } from '../api'
import { ErrorBanner, Field, PageHeader, Panel, useApi, useSubmit } from '../ui'

export default function SettingsPage() {
  const { data, error } = useApi<BusinessProfile>('/settings/business')
  const [f, setF] = useState<BusinessProfile>()
  const [saved, setSaved] = useState(false)
  const { error: saveError, saving, run } = useSubmit()

  useEffect(() => { if (data) setF(data) }, [data])
  if (!f) return <ErrorBanner message={error} />

  const set = (k: keyof BusinessProfile) => (e: { target: { value: string } }) => {
    setSaved(false)
    setF(s => s && { ...s, [k]: k === 'defaultDueDays' ? Number(e.target.value) : e.target.value })
  }

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (await run(() => api.put('/settings/business', f))) { setSaved(true); notifyBrandingChanged() }
  }

  return (
    <>
      <PageHeader title="Settings" />
      <LogoPanel />
      <form onSubmit={submit}>
        <Panel title="Your details on statements">
          <div className="form-grid">
            <Field label="Business or owner name"><input value={f.name} onChange={set('name')} required autoFocus /></Field>
            <Field label="TIN"><input value={f.tin ?? ''} onChange={set('tin')} placeholder="000-000-000-000" /></Field>
            <Field label="Address"><input value={f.address ?? ''} onChange={set('address')} /></Field>
            <Field label="Contact" hint="Phone and/or email"><input value={f.contact ?? ''} onChange={set('contact')} /></Field>
          </div>
          <Field label="How to pay" hint="Printed in a box on every statement, e.g. bank account and GCash number">
            <textarea rows={3} value={f.paymentInstructions ?? ''} onChange={set('paymentInstructions')} />
          </Field>
        </Panel>

        <Panel title="Statement format">
          <div className="form-grid">
            <Field label="Document title" hint="Only call it an invoice if it's registered with the BIR">
              <input value={f.documentTitle} onChange={set('documentTitle')} required />
            </Field>
            <Field label="Number prefix" hint={`Numbers look like ${f.numberPrefix || 'BS'}-${new Date().getFullYear()}-0001`}>
              <input value={f.numberPrefix} onChange={set('numberPrefix')} maxLength={10} required />
            </Field>
            <Field label="Days to pay" hint="Default due date = issue date + this many days">
              <input type="number" min="0" max="90" value={f.defaultDueDays} onChange={set('defaultDueDays')} required />
            </Field>
          </div>
          <Field label="Footer note"><input value={f.footerNote ?? ''} onChange={set('footerNote')} /></Field>
          <p className="muted">Changes apply to statements issued from now on. Issued statements never change.</p>
        </Panel>

        <div className="form-buttons">
          <button type="submit" disabled={saving}>Save settings</button>
          {saved && <span className="badge badge-ok">Saved</span>}
        </div>
        <ErrorBanner message={saveError} />
      </form>

      <CategoriesPanel />
      <PasswordPanel />
    </>
  )
}

function PasswordPanel() {
  const blank = { currentPassword: '', newPassword: '', confirm: '' }
  const [f, setF] = useState(blank)
  const [saved, setSaved] = useState(false)
  const { error, setError, saving, run } = useSubmit()
  const set = (k: keyof typeof f) => (e: { target: { value: string } }) => { setSaved(false); setF(s => ({ ...s, [k]: e.target.value })) }

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (f.newPassword !== f.confirm) { setError("The two new passwords don't match."); return }
    if (await run(() => api.post('/auth/password', { currentPassword: f.currentPassword, newPassword: f.newPassword }))) {
      setF(blank)
      setSaved(true)
    }
  }

  return (
    <Panel title="Change password">
      <form onSubmit={submit}>
        <div className="form-grid">
          <Field label="Current password"><input type="password" value={f.currentPassword} onChange={set('currentPassword')} autoComplete="current-password" required /></Field>
          <Field label="New password" hint="At least 8 characters"><input type="password" value={f.newPassword} onChange={set('newPassword')} autoComplete="new-password" minLength={8} required /></Field>
          <Field label="Confirm new password"><input type="password" value={f.confirm} onChange={set('confirm')} autoComplete="new-password" required /></Field>
        </div>
        <div className="form-buttons">
          <button type="submit" disabled={saving}>Change password</button>
          {saved && <span className="badge badge-ok">Password changed</span>}
        </div>
      </form>
      <ErrorBanner message={error} />
    </Panel>
  )
}

function LogoPanel() {
  const { data, reload } = useApi<Branding>('/settings/branding')
  const { error, saving, run } = useSubmit()
  const logo = data && logoUrl(data)

  const changed = () => { void reload(); notifyBrandingChanged() }
  const upload = async (e: { target: HTMLInputElement }) => {
    const file = e.target.files?.[0]
    e.target.value = '' // allow picking the same file again
    if (file && await run(() => uploadFile('/settings/logo', file))) changed()
  }
  const remove = async () => {
    if (confirm('Remove the logo?') && await run(() => api.del('/settings/logo'))) changed()
  }

  return (
    <Panel title="Logo">
      <div className="logo-row">
        {logo ? <img src={logo} alt="Current logo" className="logo-preview" /> : <span className="muted">No logo yet.</span>}
        <label className="button secondary-button">
          {logo ? 'Replace logo' : 'Upload logo'}
          <input type="file" accept=".png,.jpg,.jpeg,.webp" onChange={upload} disabled={saving} hidden />
        </label>
        {logo && <button type="button" className="link danger" onClick={remove} disabled={saving}>Remove</button>}
      </div>
      <p className="muted">Shown with your business name at the top of the app and on statements issued from now on. PNG, JPG or WEBP, up to 2 MB. A PNG with a transparent background looks best on the dark sidebar.</p>
      <ErrorBanner message={error} />
    </Panel>
  )
}

function CategoriesPanel() {
  const { data, error, reload } = useApi<ExpenseCategory[]>('/expense-categories')
  const [name, setName] = useState('')
  const [kind, setKind] = useState<ExpenseCategoryKind>('Operating')
  const { error: saveError, saving, run } = useSubmit()

  const save = async (c: ExpenseCategory, changes: Partial<ExpenseCategory>) => {
    const next = { ...c, ...changes }
    if (await run(() => api.put(`/expense-categories/${c.id}`, { name: next.name, kind: next.kind, isArchived: next.isArchived }))) void reload()
  }
  const rename = (c: ExpenseCategory) => {
    const newName = prompt('Rename category', c.name)
    if (newName && newName.trim() !== c.name) void save(c, { name: newName })
  }
  const add = async (e: FormEvent) => {
    e.preventDefault()
    if (await run(() => api.post('/expense-categories', { name, kind, isArchived: false }))) { setName(''); void reload() }
  }

  return (
    <Panel title="Expense categories">
      <ErrorBanner message={error ?? saveError} />
      <table>
        <thead><tr><th>Category</th><th>Type</th><th /></tr></thead>
        <tbody>
          {data?.map(c => (
            <tr key={c.id} className={c.isArchived ? 'archived' : ''}>
              <td>{c.name}{c.isArchived && <span className="badge">Archived</span>}</td>
              <td>{c.kind === 'Capital' ? 'Capital (not in net income)' : 'Operating'}</td>
              <td className="num">
                <button className="link" onClick={() => rename(c)}>Rename</button>
                <button className="link" onClick={() => save(c, { isArchived: !c.isArchived })}>{c.isArchived ? 'Restore' : 'Archive'}</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <form onSubmit={add} className="form-row compact">
        <Field label="New category"><input value={name} onChange={e => setName(e.target.value)} required /></Field>
        <Field label="Type">
          <select value={kind} onChange={e => setKind(e.target.value as ExpenseCategoryKind)}>
            <option value="Operating">Operating</option>
            <option value="Capital">Capital (not in net income)</option>
          </select>
        </Field>
        <div className="form-buttons"><button type="submit" disabled={saving}>Add category</button></div>
      </form>
      <p className="muted">Archived categories are hidden when adding expenses but stay on past expenses and in reports.</p>
    </Panel>
  )
}
