import { useState, type FormEvent } from 'react'
import {
  api, formatDate, monthEnd, monthStart, paymentMethodLabels, todayIso, uploadFile,
  type Expense, type ExpenseCategory, type PaymentMethod, type Property, type Receipt,
} from '../api'
import { Empty, ErrorBanner, Field, Money, PageHeader, Panel, useApi, useSubmit } from '../ui'

type Editing = { id?: number; form: ExpenseForm; receipts: Receipt[] }
type ExpenseForm = {
  date: string; propertyId: string; categoryId: string; vendorName: string; description: string
  amount: string; method: PaymentMethod; reference: string; notes: string
}
const blank = (): ExpenseForm => ({
  date: todayIso(), propertyId: '', categoryId: '', vendorName: '', description: '', amount: '', method: 'Cash', reference: '', notes: '',
})

export default function ExpensesPage() {
  const [from, setFrom] = useState(monthStart(todayIso()))
  const [to, setTo] = useState(monthEnd(todayIso()))
  const [property, setProperty] = useState('') // '' = all, 'general', or a property id
  const [categoryId, setCategoryId] = useState('')
  const [editing, setEditing] = useState<Editing>()
  const [actionError, setActionError] = useState<string>()

  const query = new URLSearchParams({ from, to })
  if (property === 'general') query.set('general', 'true')
  else if (property) query.set('propertyId', property)
  if (categoryId) query.set('categoryId', categoryId)

  const { data, error, reload } = useApi<Expense[]>(`/expenses?${query}`)
  const properties = useApi<Property[]>('/properties')
  const categories = useApi<ExpenseCategory[]>('/expense-categories')
  const total = (data ?? []).reduce((s, e) => s + e.amount, 0)

  const edit = (e: Expense) => setEditing({
    id: e.id,
    receipts: e.receipts,
    form: {
      date: e.date, propertyId: e.propertyId ? String(e.propertyId) : '', categoryId: String(e.categoryId), vendorName: e.vendor ?? '',
      description: e.description, amount: String(e.amount), method: e.method, reference: e.reference ?? '', notes: e.notes ?? '',
    },
  })

  const voidExpense = async (e: Expense) => {
    if (!confirm(`Void "${e.description}" (${e.amount})? It stays on record but is left out of totals and reports.`)) return
    try { await api.post(`/expenses/${e.id}/void`); void reload() } catch (err) { setActionError((err as Error).message) }
  }

  return (
    <>
      <PageHeader title="Expenses">
        {!editing && <button onClick={() => setEditing({ form: blank(), receipts: [] })}>Add expense</button>}
      </PageHeader>
      <ErrorBanner message={error ?? actionError ?? properties.error ?? categories.error} />

      {editing && properties.data && categories.data && (
        <ExpenseEditor editing={editing} properties={properties.data} categories={categories.data}
          onCancel={() => setEditing(undefined)} onSaved={() => { setEditing(undefined); void reload() }} />
      )}

      <Panel>
        <div className="form-row compact-top">
          <Field label="From"><input type="date" value={from} onChange={e => e.target.value && setFrom(e.target.value)} /></Field>
          <Field label="To"><input type="date" value={to} onChange={e => e.target.value && setTo(e.target.value)} /></Field>
          <Field label="Property">
            <select value={property} onChange={e => setProperty(e.target.value)}>
              <option value="">All</option>
              {properties.data?.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
              <option value="general">General (no property)</option>
            </select>
          </Field>
          <Field label="Category">
            <select value={categoryId} onChange={e => setCategoryId(e.target.value)}>
              <option value="">All</option>
              {categories.data?.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
          </Field>
        </div>

        {data?.length === 0 ? <Empty>No expenses in this period.</Empty> : (
          <table>
            <thead><tr><th>Date</th><th>Description</th><th>Category</th><th>Property</th><th>Vendor</th><th className="num">Amount</th><th>Receipts</th><th /></tr></thead>
            <tbody>
              {data?.map(e => (
                <tr key={e.id}>
                  <td>{formatDate(e.date)}</td>
                  <td>{e.description}{e.reference && <span className="muted"> · {e.reference}</span>}</td>
                  <td>{e.category}{e.kind === 'Capital' && <span className="badge" title="Not included in net income">Capital</span>}</td>
                  <td>{e.property ?? <span className="muted">General</span>}</td>
                  <td>{e.vendor ?? '—'}</td>
                  <td className="num"><Money value={e.amount} /></td>
                  <td>{e.receipts.map((r, i) => (
                    <a key={r.id} href={`/api/receipts/${r.id}`} target="_blank" rel="noreferrer" title={r.fileName} className="link-button">📎{e.receipts.length > 1 ? i + 1 : ''}</a>
                  ))}</td>
                  <td className="num">
                    <button className="link" onClick={() => edit(e)}>Edit</button>
                    <button className="link danger" onClick={() => voidExpense(e)}>Void</button>
                  </td>
                </tr>
              ))}
            </tbody>
            <tfoot><tr><td colSpan={5}>Total</td><td className="num"><Money value={total} /></td><td colSpan={2} /></tr></tfoot>
          </table>
        )}
      </Panel>
    </>
  )
}

function ExpenseEditor({ editing, properties, categories, onSaved, onCancel }: {
  editing: Editing; properties: Property[]; categories: ExpenseCategory[]; onSaved: () => void; onCancel: () => void
}) {
  const [f, setF] = useState(editing.form)
  const [files, setFiles] = useState<File[]>([])
  const vendors = useApi<string[]>('/vendors')
  const [receipts, setReceipts] = useState(editing.receipts)
  // Set once the expense exists, so retrying after a failed upload updates it instead of adding a duplicate.
  const [savedId, setSavedId] = useState(editing.id)
  const { error, saving, run } = useSubmit()
  const set = (k: keyof ExpenseForm) => (e: { target: { value: string } }) => setF(s => ({ ...s, [k]: e.target.value }))

  // Archived categories can't be picked for new expenses, but an expense keeps the one it has.
  const choosable = categories.filter(c => !c.isArchived || String(c.id) === editing.form.categoryId)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    const ok = await run(async () => {
      const body = {
        date: f.date, propertyId: f.propertyId ? Number(f.propertyId) : null, categoryId: Number(f.categoryId),
        vendorName: f.vendorName || null, description: f.description, amount: Number(f.amount), method: f.method,
        reference: f.reference || null, notes: f.notes || null,
      }
      let id = savedId
      if (id) await api.put(`/expenses/${id}`, body)
      else setSavedId(id = (await api.post<{ id: number }>('/expenses', body)).id)
      const pending = [...files]
      while (pending.length) {
        const added = await uploadFile<Receipt>(`/expenses/${id}/receipts`, pending[0])
        pending.shift()
        setFiles([...pending]) // uploaded files aren't sent again on retry
        setReceipts(r => [...r, added])
      }
    })
    if (ok) onSaved()
  }

  const removeReceipt = async (r: Receipt) => {
    if (!confirm(`Remove ${r.fileName}?`)) return
    if (await run(() => api.del(`/receipts/${r.id}`))) setReceipts(rs => rs.filter(x => x.id !== r.id))
  }

  return (
    <Panel title={editing.id ? 'Edit expense' : 'New expense'}>
      <form onSubmit={submit}>
        <div className="form-grid">
          <Field label="Date paid"><input type="date" value={f.date} onChange={set('date')} required /></Field>
          <Field label="Amount (₱)"><input type="number" min="0.01" step="0.01" value={f.amount} onChange={set('amount')} required autoFocus /></Field>
          <Field label="Category">
            <select value={f.categoryId} onChange={set('categoryId')} required>
              <option value="">Choose…</option>
              <optgroup label="Operating">
                {choosable.filter(c => c.kind === 'Operating').map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
              </optgroup>
              <optgroup label="Capital (not in net income)">
                {choosable.filter(c => c.kind === 'Capital').map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
              </optgroup>
            </select>
          </Field>
          <Field label="Property">
            <select value={f.propertyId} onChange={set('propertyId')}>
              <option value="">General (not one property)</option>
              {properties.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
          </Field>
          <Field label="Description"><input value={f.description} onChange={set('description')} placeholder="e.g. Fix leaking faucet, Unit 1A" required /></Field>
          <Field label="Vendor / payee" hint="New names are saved for next time">
            <input value={f.vendorName} onChange={set('vendorName')} list="vendor-names" placeholder="e.g. Meralco, Maynilad, plumber" />
            <datalist id="vendor-names">{vendors.data?.map(v => <option key={v} value={v} />)}</datalist>
          </Field>
          <Field label="Paid by">
            <select value={f.method} onChange={set('method')}>
              {Object.entries(paymentMethodLabels).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
            </select>
          </Field>
          <Field label="Reference" hint="Vendor's OR / invoice no., check no."><input value={f.reference} onChange={set('reference')} /></Field>
        </div>
        <Field label="Notes"><textarea rows={2} value={f.notes} onChange={set('notes')} /></Field>

        <Field label="Receipts" hint="PDF or photo, up to 10 MB each">
          <input type="file" multiple accept=".pdf,.jpg,.jpeg,.png,.webp,.heic" onChange={e => setFiles([...(e.target.files ?? [])])} />
          {files.length > 0 && <small>{files.length} file{files.length === 1 ? '' : 's'} to upload: {files.map(x => x.name).join(', ')}</small>}
        </Field>
        {receipts.length > 0 && (
          <ul className="receipts">
            {receipts.map(r => (
              <li key={r.id}>
                <a href={`/api/receipts/${r.id}`} target="_blank" rel="noreferrer">{r.fileName}</a>
                <span className="muted"> ({Math.ceil(r.sizeBytes / 1024)} KB)</span>
                <button type="button" className="link danger" onClick={() => removeReceipt(r)}>Remove</button>
              </li>
            ))}
          </ul>
        )}

        <div className="form-buttons">
          <button type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save expense'}</button>
          <button type="button" className="secondary" onClick={onCancel}>Cancel</button>
        </div>
      </form>
      <ErrorBanner message={error} />
    </Panel>
  )
}
