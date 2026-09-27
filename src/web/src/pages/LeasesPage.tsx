import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api, formatDate, todayIso, type LeaseSummary, type Property, type Tenant } from '../api'
import { rentAfterUnitChange } from '../leaseForm'
import { Empty, ErrorBanner, Field, Money, PageHeader, Panel, useApi, useSubmit } from '../ui'

export default function LeasesPage() {
  const { data, error } = useApi<LeaseSummary[]>('/leases')
  const [adding, setAdding] = useState(false)

  return (
    <>
      <PageHeader title="Leases">
        {!adding && <button onClick={() => setAdding(true)}>New lease</button>}
      </PageHeader>
      <ErrorBanner message={error} />
      {adding && <NewLeaseForm onCancel={() => setAdding(false)} />}

      <Panel>
        {data?.length === 0 ? <Empty>No leases yet. Add a property, a unit and a tenant first, then create a lease.</Empty> : (
          <table>
            <thead><tr><th>Tenant</th><th>Unit</th><th>Term</th><th className="num">Rent</th><th className="num">Due day</th><th className="num">Balance</th><th>Status</th></tr></thead>
            <tbody>
              {data?.map(l => (
                <tr key={l.id}>
                  <td><Link to={`/leases/${l.id}`}>{l.tenant}</Link></td>
                  <td>{l.property} · {l.unit}</td>
                  <td>{formatDate(l.startDate)} – {l.endDate ? formatDate(l.endDate) : 'open-ended'}</td>
                  <td className="num"><Money value={l.monthlyRent} /></td>
                  <td className="num">{l.dueDay}</td>
                  <td className="num"><Money value={l.balance} className={l.balance > 0 ? 'alert' : ''} /></td>
                  <td><span className={`badge ${l.status === 'Active' ? 'badge-ok' : ''}`}>{l.status}</span></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Panel>
    </>
  )
}

function NewLeaseForm({ onCancel }: { onCancel: () => void }) {
  const properties = useApi<Property[]>('/properties')
  const tenants = useApi<Tenant[]>('/tenants')
  const navigate = useNavigate()
  const { error, saving, run } = useSubmit()
  const [f, setF] = useState({
    unitId: '', tenantId: '', startDate: todayIso(), endDate: '', monthlyRent: '', dueDay: String(new Date().getDate()),
    gracePeriodDays: '0', securityDeposit: '', notes: '',
  })
  const set = (k: keyof typeof f) => (e: { target: { value: string } }) => setF(s => ({ ...s, [k]: e.target.value }))

  const vacantUnits = (properties.data ?? []).flatMap(p =>
    p.units.filter(u => !u.currentTenant).map(u => ({ ...u, label: `${p.name} · ${u.name}` })))

  const pickUnit = (id: string) => {
    const rentOf = (unitId: string) => vacantUnits.find(u => String(u.id) === unitId)?.defaultMonthlyRent
    setF(s => ({ ...s, unitId: id, monthlyRent: rentAfterUnitChange(s.monthlyRent, rentOf(s.unitId), rentOf(id)) }))
  }
  // Due day follows the start date by default, which is how most PH leases work.
  const pickStart = (value: string) =>
    setF(s => ({ ...s, startDate: value, dueDay: value ? String(Number(value.slice(8, 10))) : s.dueDay }))

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    let id = 0
    const ok = await run(async () => {
      const res = await api.post<{ id: number }>('/leases', {
        unitId: Number(f.unitId), tenantId: Number(f.tenantId), startDate: f.startDate, endDate: f.endDate || null,
        monthlyRent: Number(f.monthlyRent), dueDay: Number(f.dueDay), gracePeriodDays: Number(f.gracePeriodDays) || 0,
        securityDeposit: Number(f.securityDeposit) || 0, notes: f.notes || null,
      })
      id = res.id
    })
    if (ok) navigate(`/leases/${id}`)
  }

  return (
    <Panel title="New lease">
      <form onSubmit={submit}>
        <div className="form-grid">
          <Field label="Unit">
            <select value={f.unitId} onChange={e => pickUnit(e.target.value)} required>
              <option value="">{vacantUnits.length ? 'Choose a vacant unit…' : 'No vacant units'}</option>
              {vacantUnits.map(u => <option key={u.id} value={u.id}>{u.label}</option>)}
            </select>
          </Field>
          <Field label="Tenant">
            <select value={f.tenantId} onChange={set('tenantId')} required>
              <option value="">Choose a tenant…</option>
              {tenants.data?.filter(t => t.isActive).map(t => <option key={t.id} value={t.id}>{t.fullName}</option>)}
            </select>
          </Field>
          <Field label="Start date"><input type="date" value={f.startDate} onChange={e => pickStart(e.target.value)} required /></Field>
          <Field label="End date" hint="Leave blank for month-to-month"><input type="date" value={f.endDate} onChange={set('endDate')} /></Field>
          <Field label="Monthly rent (₱)"><input type="number" min="0.01" step="0.01" value={f.monthlyRent} onChange={set('monthlyRent')} required /></Field>
          <Field label="Rent due day" hint="Day of month; 29–31 moves to month-end in short months"><input type="number" min="1" max="31" value={f.dueDay} onChange={set('dueDay')} required /></Field>
          <Field label="Grace period (days)" hint="Days after due date before it counts as overdue"><input type="number" min="0" max="60" value={f.gracePeriodDays} onChange={set('gracePeriodDays')} /></Field>
          <Field label="Security deposit (₱)" hint="Held for the tenant; not counted as income"><input type="number" min="0" step="0.01" value={f.securityDeposit} onChange={set('securityDeposit')} /></Field>
        </div>
        <Field label="Notes"><textarea rows={2} value={f.notes} onChange={set('notes')} /></Field>
        <div className="form-buttons">
          <button type="submit" disabled={saving}>Create lease</button>
          <button type="button" className="secondary" onClick={onCancel}>Cancel</button>
        </div>
      </form>
      <ErrorBanner message={error ?? properties.error ?? tenants.error} />
    </Panel>
  )
}
