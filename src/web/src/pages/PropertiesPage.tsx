import { useState, type FormEvent } from 'react'
import { api, type Property } from '../api'
import { Empty, ErrorBanner, Field, Money, PageHeader, Panel, useApi, useSubmit } from '../ui'

export default function PropertiesPage() {
  const { data, error, reload } = useApi<Property[]>('/properties')
  const [adding, setAdding] = useState(false)

  return (
    <>
      <PageHeader title="Properties & units">
        {!adding && <button onClick={() => setAdding(true)}>Add property</button>}
      </PageHeader>
      <ErrorBanner message={error} />
      {adding && <PropertyForm onDone={() => { setAdding(false); void reload() }} onCancel={() => setAdding(false)} />}
      {data?.length === 0 && !adding && <Empty>No properties yet. Add your first building or house to get started.</Empty>}
      {data?.map(p => <PropertyCard key={p.id} property={p} onChanged={reload} />)}
    </>
  )
}

function PropertyForm({ onDone, onCancel }: { onDone: () => void; onCancel: () => void }) {
  const [name, setName] = useState('')
  const [address, setAddress] = useState('')
  const { error, saving, run } = useSubmit()

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (await run(() => api.post('/properties', { name, address: address || null }))) onDone()
  }

  return (
    <Panel title="New property">
      <form onSubmit={submit} className="form-row">
        <Field label="Name"><input value={name} onChange={e => setName(e.target.value)} placeholder="e.g. Mabini Apartments" autoFocus required /></Field>
        <Field label="Address"><input value={address} onChange={e => setAddress(e.target.value)} placeholder="Street, barangay, city" /></Field>
        <div className="form-buttons">
          <button type="submit" disabled={saving}>Save</button>
          <button type="button" className="secondary" onClick={onCancel}>Cancel</button>
        </div>
      </form>
      <ErrorBanner message={error} />
    </Panel>
  )
}

function PropertyCard({ property, onChanged }: { property: Property; onChanged: () => void }) {
  const [unitName, setUnitName] = useState('')
  const [rent, setRent] = useState('')
  const { error, saving, run } = useSubmit()

  const addUnit = async (e: FormEvent) => {
    e.preventDefault()
    const ok = await run(() => api.post(`/properties/${property.id}/units`, { name: unitName, defaultMonthlyRent: Number(rent) || 0 }))
    if (ok) { setUnitName(''); setRent(''); onChanged() }
  }
  const removeUnit = async (id: number, name: string) => {
    if (confirm(`Delete unit ${name}?`) && await run(() => api.del(`/units/${id}`))) onChanged()
  }
  const removeProperty = async () => {
    if (confirm(`Delete ${property.name}?`) && await run(() => api.del(`/properties/${property.id}`))) onChanged()
  }

  return (
    <Panel>
      <div className="panel-title-row">
        <div>
          <h2>{property.name}</h2>
          {property.address && <div className="muted">{property.address}</div>}
        </div>
        {property.units.length === 0 && <button className="link danger" onClick={removeProperty}>Delete property</button>}
      </div>

      {property.units.length > 0 && (
        <table>
          <thead><tr><th>Unit</th><th className="num">Asking rent</th><th>Current tenant</th><th /></tr></thead>
          <tbody>
            {property.units.map(u => (
              <tr key={u.id}>
                <td>{u.name}</td>
                <td className="num"><Money value={u.defaultMonthlyRent} /></td>
                <td>{u.currentTenant ?? <span className="badge">Vacant</span>}</td>
                <td className="num">{!u.currentTenant && <button className="link danger" onClick={() => removeUnit(u.id, u.name)}>Delete</button>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <form onSubmit={addUnit} className="form-row compact">
        <Field label="New unit"><input value={unitName} onChange={e => setUnitName(e.target.value)} placeholder="e.g. Unit 2B" required /></Field>
        <Field label="Asking rent (₱/month)"><input type="number" min="0" step="0.01" value={rent} onChange={e => setRent(e.target.value)} /></Field>
        <div className="form-buttons"><button type="submit" disabled={saving}>Add unit</button></div>
      </form>
      <ErrorBanner message={error} />
    </Panel>
  )
}
