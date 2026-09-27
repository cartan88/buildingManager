import { useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api, formatDate, formatPeso, paymentMethodLabels, todayIso, type ChargeType, type LeaseDetail, type LedgerEntry, type PaymentMethod } from '../api'
import { Empty, ErrorBanner, Field, Money, PageHeader, Panel, useApi, useSubmit } from '../ui'

type Mode = 'payment' | 'charge' | 'end' | null

export default function LeaseDetailPage() {
  const { id } = useParams()
  const { data, error, reload } = useApi<LeaseDetail>(`/leases/${id}`)
  const [mode, setMode] = useState<Mode>(null)
  const { error: actionError, run } = useSubmit()

  if (error) return <ErrorBanner message={error} />
  if (!data) return null
  const { lease, ledger, balance } = data
  const done = () => { setMode(null); void reload() }

  const voidEntry = async (e: LedgerEntry) => {
    const what = e.kind === 'Payment' ? `payment of ${formatPeso(e.payment)}` : `charge "${e.description}"`
    if (!confirm(`Void this ${what}? It stays on record, marked as void.`)) return
    if (await run(() => api.post(`/${e.kind === 'Payment' ? 'payments' : 'charges'}/${e.id}/void`))) void reload()
  }

  return (
    <>
      <p className="breadcrumb"><Link to="/leases">← Leases</Link></p>
      <PageHeader title={`${lease.tenant} — ${lease.property} · ${lease.unit}`}>
        {lease.status === 'Active' && <>
          <button onClick={() => setMode('payment')}>Record payment</button>
          <button className="secondary" onClick={() => setMode('charge')}>Add charge</button>
          <button className="secondary" onClick={() => setMode('end')}>End lease</button>
        </>}
      </PageHeader>

      <div className="stats">
        <div className="stat">
          <div className="stat-label">{balance < 0 ? 'Credit' : 'Balance due'}</div>
          <div className="stat-value"><Money value={Math.abs(balance)} className={balance > 0 ? 'alert' : ''} /></div>
          <div className="stat-sub">{balance < 0 ? 'Paid in advance' : balance === 0 ? 'All paid up' : 'Unpaid charges'}</div>
        </div>
        <div className="stat"><div className="stat-label">Monthly rent</div><div className="stat-value"><Money value={lease.monthlyRent} /></div>
          <div className="stat-sub">Due every {ordinal(lease.dueDay)}{lease.gracePeriodDays ? `, ${lease.gracePeriodDays}-day grace` : ''}</div></div>
        <div className="stat"><div className="stat-label">Term</div>
          <div className="stat-value small">{formatDate(lease.startDate)} – {lease.endDate ? formatDate(lease.endDate) : 'open-ended'}</div>
          <div className="stat-sub">{lease.status}</div></div>
        <div className="stat"><div className="stat-label">Security deposit held</div><div className="stat-value"><Money value={lease.securityDeposit} /></div>
          <div className="stat-sub">{lease.phone ?? lease.email ?? ''}</div></div>
      </div>

      {mode === 'payment' && <PaymentForm leaseId={lease.id} suggested={Math.max(balance, 0)} onDone={done} onCancel={() => setMode(null)} />}
      {mode === 'charge' && <ChargeForm leaseId={lease.id} onDone={done} onCancel={() => setMode(null)} />}
      {mode === 'end' && <EndLeaseForm leaseId={lease.id} onDone={done} onCancel={() => setMode(null)} />}

      <Panel title="Ledger">
        <ErrorBanner message={actionError} />
        {ledger.length === 0 ? <Empty>No charges yet. Rent is billed automatically on each due date.</Empty> : (
          <table>
            <thead><tr><th>Date</th><th>Description</th><th className="num">Charge</th><th className="num">Payment</th><th className="num">Balance</th><th /></tr></thead>
            <tbody>
              {ledger.map(e => (
                <tr key={`${e.kind}-${e.id}`} className={e.isVoided ? 'voided' : ''}>
                  <td>{formatDate(e.date)}</td>
                  <td>{e.description}{e.isVoided && <span className="badge">Void</span>}</td>
                  <td className="num">{e.charge ? <Money value={e.charge} /> : ''}</td>
                  <td className="num">{e.payment ? <Money value={e.payment} /> : ''}</td>
                  <td className="num"><Money value={e.balance} /></td>
                  <td className="num">{!e.isVoided && <button className="link danger" onClick={() => voidEntry(e)}>Void</button>}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Panel>
    </>
  )
}

function PaymentForm({ leaseId, suggested, onDone, onCancel }: { leaseId: number; suggested: number; onDone: () => void; onCancel: () => void }) {
  const [f, setF] = useState({ paymentDate: todayIso(), amount: suggested ? String(suggested) : '', method: 'Cash' as PaymentMethod, reference: '', notes: '' })
  const set = (k: keyof typeof f) => (e: { target: { value: string } }) => setF(s => ({ ...s, [k]: e.target.value }))
  const { error, saving, run } = useSubmit()

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    const ok = await run(() => api.post(`/leases/${leaseId}/payments`, {
      ...f, amount: Number(f.amount), reference: f.reference || null, notes: f.notes || null,
    }))
    if (ok) onDone()
  }

  return (
    <Panel title="Record payment">
      <form onSubmit={submit}>
        <div className="form-grid">
          <Field label="Date received"><input type="date" value={f.paymentDate} onChange={set('paymentDate')} required /></Field>
          <Field label="Amount (₱)" hint="Applied to the oldest unpaid charges first; any extra is kept as credit">
            <input type="number" min="0.01" step="0.01" value={f.amount} onChange={set('amount')} required autoFocus /></Field>
          <Field label="Method">
            <select value={f.method} onChange={set('method')}>
              {Object.entries(paymentMethodLabels).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
            </select>
          </Field>
          <Field label="Reference no." hint="Bank / GCash ref, check no."><input value={f.reference} onChange={set('reference')} /></Field>
        </div>
        <div className="form-buttons">
          <button type="submit" disabled={saving}>Save payment</button>
          <button type="button" className="secondary" onClick={onCancel}>Cancel</button>
        </div>
      </form>
      <ErrorBanner message={error} />
    </Panel>
  )
}

function ChargeForm({ leaseId, onDone, onCancel }: { leaseId: number; onDone: () => void; onCancel: () => void }) {
  const [f, setF] = useState({ type: 'Utility' as ChargeType, description: '', dueDate: todayIso(), amount: '' })
  const set = (k: keyof typeof f) => (e: { target: { value: string } }) => setF(s => ({ ...s, [k]: e.target.value }))
  const { error, saving, run } = useSubmit()

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (await run(() => api.post(`/leases/${leaseId}/charges`, { ...f, amount: Number(f.amount) }))) onDone()
  }

  return (
    <Panel title="Add charge">
      <form onSubmit={submit}>
        <div className="form-grid">
          <Field label="Type">
            <select value={f.type} onChange={set('type')}>
              <option value="Utility">Utility (water, electricity)</option>
              <option value="Other">Other</option>
              <option value="LateFee">Late fee</option>
              <option value="Rent">Rent (manual)</option>
            </select>
          </Field>
          <Field label="Description"><input value={f.description} onChange={set('description')} placeholder="e.g. Water – September 2026" required autoFocus /></Field>
          <Field label="Due date"><input type="date" value={f.dueDate} onChange={set('dueDate')} required /></Field>
          <Field label="Amount (₱)"><input type="number" min="0.01" step="0.01" value={f.amount} onChange={set('amount')} required /></Field>
        </div>
        <div className="form-buttons">
          <button type="submit" disabled={saving}>Add charge</button>
          <button type="button" className="secondary" onClick={onCancel}>Cancel</button>
        </div>
      </form>
      <ErrorBanner message={error} />
    </Panel>
  )
}

function EndLeaseForm({ leaseId, onDone, onCancel }: { leaseId: number; onDone: () => void; onCancel: () => void }) {
  const [endDate, setEndDate] = useState(todayIso())
  const { error, saving, run } = useSubmit()

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (await run(() => api.post(`/leases/${leaseId}/end`, { endDate }))) onDone()
  }

  return (
    <Panel title="End lease">
      <form onSubmit={submit} className="form-row">
        <Field label="Move-out date" hint="No rent is billed for periods starting after this date. The unit becomes vacant.">
          <input type="date" value={endDate} onChange={e => setEndDate(e.target.value)} required />
        </Field>
        <div className="form-buttons">
          <button type="submit" disabled={saving}>End lease</button>
          <button type="button" className="secondary" onClick={onCancel}>Cancel</button>
        </div>
      </form>
      <ErrorBanner message={error} />
    </Panel>
  )
}

const ordinal = (n: number) => {
  const s = n % 100 >= 11 && n % 100 <= 13 ? 'th' : ({ 1: 'st', 2: 'nd', 3: 'rd' } as Record<number, string>)[n % 10] ?? 'th'
  return `${n}${s}`
}
