import { useEffect, useState, type FormEvent } from 'react'
import { api, type BusinessProfile } from '../api'
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
    if (await run(() => api.put('/settings/business', f))) setSaved(true)
  }

  return (
    <>
      <PageHeader title="Settings" />
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
    </>
  )
}
