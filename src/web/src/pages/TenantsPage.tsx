import { useState, type FormEvent } from 'react'
import { api, type Tenant } from '../api'
import { Empty, ErrorBanner, Field, PageHeader, Panel, useApi, useSubmit } from '../ui'

type TenantForm = { fullName: string; phone: string; email: string; tin: string; notes: string }
const blank: TenantForm = { fullName: '', phone: '', email: '', tin: '', notes: '' }

export default function TenantsPage() {
  const { data, error, reload } = useApi<Tenant[]>('/tenants')
  const [editing, setEditing] = useState<{ id?: number; form: TenantForm }>()
  const { error: saveError, saving, run } = useSubmit()

  const set = (k: keyof TenantForm) => (e: { target: { value: string } }) =>
    setEditing(s => s && { ...s, form: { ...s.form, [k]: e.target.value } })

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!editing) return
    const body = Object.fromEntries(Object.entries(editing.form).map(([k, v]) => [k, v.trim() || null]))
    const ok = await run(() => editing.id ? api.put(`/tenants/${editing.id}`, body) : api.post('/tenants', body))
    if (ok) { setEditing(undefined); void reload() }
  }

  const [showInactive, setShowInactive] = useState(false)
  const shown = data?.filter(t => showInactive || t.isActive)
  const inactiveCount = data?.filter(t => !t.isActive).length ?? 0

  const remove = async (t: Tenant) => {
    if (confirm(`Delete ${t.fullName}?`) && await run(() => api.del(`/tenants/${t.id}`))) void reload()
  }

  const setActive = async (t: Tenant, active: boolean) => {
    if (await run(() => api.post(`/tenants/${t.id}/${active ? 'activate' : 'deactivate'}`))) void reload()
  }

  return (
    <>
      <PageHeader title="Tenants">
        <label className="toggle">
          <input type="checkbox" className="check" checked={showInactive} onChange={e => setShowInactive(e.target.checked)} />
          Show inactive tenants{inactiveCount > 0 && ` (${inactiveCount})`}
        </label>
        {!editing && <button onClick={() => setEditing({ form: blank })}>Add tenant</button>}
      </PageHeader>
      <ErrorBanner message={error ?? (editing ? undefined : saveError)} />

      {editing && (
        <Panel title={editing.id ? 'Edit tenant' : 'New tenant'}>
          <form onSubmit={submit}>
            <div className="form-grid">
              <Field label="Full name"><input value={editing.form.fullName} onChange={set('fullName')} required autoFocus /></Field>
              <Field label="Mobile / phone"><input value={editing.form.phone} onChange={set('phone')} placeholder="09xx xxx xxxx" /></Field>
              <Field label="Email"><input type="email" value={editing.form.email} onChange={set('email')} /></Field>
              <Field label="TIN" hint="Needed on invoices for business tenants"><input value={editing.form.tin} onChange={set('tin')} placeholder="000-000-000-000" /></Field>
            </div>
            <Field label="Notes"><textarea value={editing.form.notes} onChange={set('notes')} rows={2} /></Field>
            <div className="form-buttons">
              <button type="submit" disabled={saving}>Save</button>
              <button type="button" className="secondary" onClick={() => setEditing(undefined)}>Cancel</button>
            </div>
          </form>
          <ErrorBanner message={saveError} />
        </Panel>
      )}

      <Panel>
        {data?.length === 0 ? <Empty>No tenants yet.</Empty> : shown?.length === 0 ? <Empty>No active tenants. Tick "Show inactive tenants" to see the rest.</Empty> : (
          <table>
            <thead><tr><th>Name</th><th>Phone</th><th>Email</th><th>TIN</th><th>Status</th><th /></tr></thead>
            <tbody>
              {shown?.map(t => (
                <tr key={t.id} className={t.isActive ? undefined : 'inactive'}>
                  <td>{t.fullName}</td>
                  <td>{t.phone ?? '—'}</td>
                  <td>{t.email ?? '—'}</td>
                  <td>{t.tin ?? '—'}</td>
                  <td>{t.isActive ? <span className="badge badge-ok">Active</span> : <span className="badge">Inactive</span>}</td>
                  <td className="num">
                    <button className="link" onClick={() => setEditing({
                      id: t.id,
                      form: { fullName: t.fullName, phone: t.phone ?? '', email: t.email ?? '', tin: t.tin ?? '', notes: t.notes ?? '' },
                    })}>Edit</button>
                    {t.isActive
                      ? t.activeLeases === 0 && <button className="link" onClick={() => setActive(t, false)}>Mark inactive</button>
                      : <button className="link" onClick={() => setActive(t, true)}>Reactivate</button>}
                    <button className="link danger" onClick={() => remove(t)} disabled={t.activeLeases > 0}
                      title={t.activeLeases > 0 ? 'End the active lease first' : undefined}>Delete</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Panel>
    </>
  )
}
