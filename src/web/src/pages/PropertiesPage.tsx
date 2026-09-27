import { useState, type FormEvent } from 'react'
import { api, type Property, type UnitSummary } from '../api'
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

/** Adds a property, or edits `property` when given. */
function PropertyForm({ property, onDone, onCancel }: { property?: Property; onDone: () => void; onCancel: () => void }) {
  const [name, setName] = useState(property?.name ?? '')
  const [address, setAddress] = useState(property?.address ?? '')
  const { error, saving, run } = useSubmit()

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    const body = { name, address: address || null, notes: property?.notes ?? null }
    if (await run(() => property ? api.put(`/properties/${property.id}`, body) : api.post('/properties', body))) onDone()
  }

  const form = (
    <form onSubmit={submit} className="form-row">
      <Field label="Name"><input value={name} onChange={e => setName(e.target.value)} placeholder="e.g. Mabini Apartments" autoFocus required /></Field>
      <Field label="Address"><input value={address} onChange={e => setAddress(e.target.value)} placeholder="Street, barangay, city" /></Field>
      <div className="form-buttons">
        <button type="submit" disabled={saving}>Save</button>
        <button type="button" className="secondary" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  )

  return property
    ? <>{form}<ErrorBanner message={error} /></>
    : <Panel title="New property">{form}<ErrorBanner message={error} /></Panel>
}

function PropertyCard({ property, onChanged }: { property: Property; onChanged: () => void }) {
  const [unitName, setUnitName] = useState('')
  const [rent, setRent] = useState('')
  const [editingProperty, setEditingProperty] = useState(false)
  const [editingUnit, setEditingUnit] = useState<{ id: number; name: string; rent: string }>()
  const { error, saving, run } = useSubmit()

  const addUnit = async (e: FormEvent) => {
    e.preventDefault()
    const ok = await run(() => api.post(`/properties/${property.id}/units`, { name: unitName, defaultMonthlyRent: Number(rent) || 0 }))
    if (ok) { setUnitName(''); setRent(''); onChanged() }
  }
  const saveUnit = async (e: FormEvent, u: UnitSummary) => {
    e.preventDefault()
    if (!editingUnit) return
    const body = { name: editingUnit.name, defaultMonthlyRent: Number(editingUnit.rent) || 0, notes: u.notes ?? null }
    if (await run(() => api.put(`/units/${u.id}`, body))) { setEditingUnit(undefined); onChanged() }
  }
  const removeUnit = async (id: number, name: string) => {
    if (confirm(`Delete unit ${name}?`) && await run(() => api.del(`/units/${id}`))) onChanged()
  }
  const removeProperty = async () => {
    if (confirm(`Delete ${property.name}?`) && await run(() => api.del(`/properties/${property.id}`))) onChanged()
  }

  const unitDeleteBlocked = (u: UnitSummary) =>
    u.currentTenant ? 'End the active lease first' : u.hasLeases ? 'This unit has lease history and can\'t be deleted' : undefined

  return (
    <Panel>
      {editingProperty
        ? <PropertyForm property={property} onDone={() => { setEditingProperty(false); onChanged() }} onCancel={() => setEditingProperty(false)} />
        : (
          <div className="panel-title-row">
            <div>
              <h2>{property.name}</h2>
              {property.address && <div className="muted">{property.address}</div>}
            </div>
            <div>
              <button className="link" onClick={() => setEditingProperty(true)}>Edit</button>
              <button className="link danger" onClick={removeProperty} disabled={property.units.length > 0}
                title={property.units.length > 0 ? 'Delete its units first' : undefined}>Delete property</button>
            </div>
          </div>
        )}

      {property.units.length > 0 && (
        <table>
          <thead><tr><th>Unit</th><th className="num">Asking rent</th><th>Current tenant</th><th /></tr></thead>
          <tbody>
            {property.units.map(u => editingUnit?.id === u.id ? (
              <tr key={u.id}>
                <td colSpan={4}>
                  <form onSubmit={e => saveUnit(e, u)} className="form-row compact">
                    <Field label="Unit"><input value={editingUnit.name} onChange={e => setEditingUnit({ ...editingUnit, name: e.target.value })} autoFocus required /></Field>
                    <Field label="Asking rent (₱/month)" hint="Existing leases keep their own rent">
                      <input type="number" min="0" step="0.01" value={editingUnit.rent} onChange={e => setEditingUnit({ ...editingUnit, rent: e.target.value })} />
                    </Field>
                    <div className="form-buttons">
                      <button type="submit" disabled={saving}>Save</button>
                      <button type="button" className="secondary" onClick={() => setEditingUnit(undefined)}>Cancel</button>
                    </div>
                  </form>
                </td>
              </tr>
            ) : (
              <tr key={u.id}>
                <td>{u.name}</td>
                <td className="num"><Money value={u.defaultMonthlyRent} /></td>
                <td>{u.currentTenant ?? <span className="badge">Vacant</span>}</td>
                <td className="num">
                  <button className="link" onClick={() => setEditingUnit({ id: u.id, name: u.name, rent: String(u.defaultMonthlyRent) })}>Edit</button>
                  <button className="link danger" onClick={() => removeUnit(u.id, u.name)} disabled={!!unitDeleteBlocked(u)}
                    title={unitDeleteBlocked(u)}>Delete</button>
                </td>
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
