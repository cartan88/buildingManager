import { useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { addDays, api, formatDate, formatPeso, paymentMethodLabels, todayIso, type BusinessProfile, type ChargeType, type InvoiceSummary, type LeaseDetail, type LeaseInfo, type LedgerEntry, type OpenCharge, type PaymentMethod, type RentFrequency } from '../api'
import { InvoiceTable } from '../invoiceUi'
import { amountToPayThrough } from '../leaseForm'
import { pageInfo } from '../pagination'
import { Empty, ErrorBanner, Field, Money, PageHeader, Pager, Panel, useApi, useSubmit } from '../ui'

type Mode = 'payment' | 'charge' | 'statement' | 'edit' | 'end' | null

export default function LeaseDetailPage() {
  const { id } = useParams()
  const { data, error, reload } = useApi<LeaseDetail>(`/leases/${id}`)
  const invoices = useApi<InvoiceSummary[]>(`/invoices?leaseId=${id}`)
  const openCharges = useApi<OpenCharge[]>(`/leases/${id}/open-charges`)
  const [mode, setMode] = useState<Mode>(null)
  // Set by a ledger row's "Mark paid": pay everything up to and including that charge.
  const [payThrough, setPayThrough] = useState<{ chargeId: number; description: string; amount: number }>()
  // Ledger page; null = the last page, where the newest entries are.
  const [page, setPage] = useState<number | null>(null)
  const [pageSize, setPageSize] = useState(20)
  const { error: actionError, run, setError } = useSubmit()

  if (error) return <ErrorBanner message={error} />
  if (!data) return null
  const { lease, ledger, balance } = data
  const refresh = () => { void reload(); void openCharges.reload() }
  const done = () => { closeForm(); setPage(null); refresh(); void invoices.reload() } // show the new entry
  const closeForm = () => { setMode(null); setPayThrough(undefined) }
  const shown = pageInfo(ledger.length, page, pageSize)
  const owedThrough = amountToPayThrough(openCharges.data ?? [])

  const markPaid = (e: LedgerEntry) => {
    setPayThrough({ chargeId: e.id, description: e.description, amount: owedThrough.get(e.id)! })
    setMode('payment')
  }

  const voidEntry = async (e: LedgerEntry) => {
    const what = e.kind === 'Payment' ? `payment of ${formatPeso(e.payment)}` : `charge "${e.description}"`
    if (!confirm(`Void this ${what}? It stays on record, marked as void.`)) return
    if (await run(() => api.post(`/${e.kind === 'Payment' ? 'payments' : 'charges'}/${e.id}/void`))) refresh()
  }

  return (
    <>
      <p className="breadcrumb"><Link to="/leases">← Leases</Link></p>
      <PageHeader title={`${lease.tenant} — ${lease.property} · ${lease.unit}`}>
        {lease.status === 'Active' && <>
          <button onClick={() => { setPayThrough(undefined); setMode('payment') }}>Record payment</button>
          <button className="secondary" onClick={() => setMode('charge')}>Add charge</button>
          <button className="secondary" onClick={() => setMode('statement')}>Create statement</button>
          <button className="secondary" onClick={() => setMode('edit')}>Edit lease</button>
          <button className="secondary" onClick={() => setMode('end')}>End lease</button>
        </>}
      </PageHeader>

      <div className="stats">
        <div className="stat">
          <div className="stat-label">{balance < 0 ? 'Credit' : 'Balance due'}</div>
          <div className="stat-value"><Money value={Math.abs(balance)} className={balance > 0 ? 'alert' : ''} /></div>
          <div className="stat-sub">{balance < 0 ? 'Paid in advance' : balance === 0 ? 'All paid up' : 'Unpaid charges'}</div>
        </div>
        <div className="stat"><div className="stat-label">{rentLabel(lease.frequency)}</div><div className="stat-value"><Money value={lease.rent} /></div>
          <div className="stat-sub">
            {lease.frequency === 'Daily' ? 'Charged every day' : `Due every ${ordinal(lease.dueDay)}`}
            {lease.gracePeriodDays ? `, ${lease.gracePeriodDays}-day grace` : ''}
          </div></div>
        <div className="stat"><div className="stat-label">Term</div>
          <div className="stat-value small">{formatDate(lease.startDate)} – {lease.endDate ? formatDate(lease.endDate) : 'open-ended'}</div>
          <div className="stat-sub">{lease.status}</div></div>
        <div className="stat"><div className="stat-label">Security deposit held</div><div className="stat-value"><Money value={lease.securityDeposit} /></div>
          <div className="stat-sub">{lease.phone ?? lease.email ?? ''}</div></div>
      </div>

      {mode === 'payment' && <PaymentForm key={payThrough?.chargeId ?? 'all'} leaseId={lease.id} suggested={payThrough?.amount ?? Math.max(balance, 0)}
        through={payThrough?.description} onDone={done} onCancel={closeForm} />}
      {mode === 'charge' && <ChargeForm leaseId={lease.id} onDone={done} onCancel={() => setMode(null)} />}
      {mode === 'statement' && <StatementForm leaseId={lease.id} onDone={done} onCancel={() => setMode(null)} />}
      {mode === 'edit' && <EditLeaseForm lease={lease} onDone={done} onCancel={() => setMode(null)} />}
      {mode === 'end' && <EndLeaseForm leaseId={lease.id} onDone={done} onCancel={() => setMode(null)} />}

      <Panel title="Ledger">
        <ErrorBanner message={actionError} />
        {ledger.length === 0 ? <Empty>No charges yet. Rent is billed automatically on each due date.</Empty> : (
          <table>
            <thead><tr><th>Date</th><th>Description</th><th className="num">Charge</th><th className="num">Payment</th><th className="num">Balance</th><th /></tr></thead>
            <tbody>
              {ledger.slice(shown.start, shown.end).map(e => (
                <tr key={`${e.kind}-${e.id}`} className={e.isVoided ? 'voided' : ''}>
                  <td>{formatDate(e.date)}</td>
                  <td>{e.description}{e.isVoided && <span className="badge">Void</span>}</td>
                  <td className="num">{e.charge ? <Money value={e.charge} /> : ''}</td>
                  <td className="num">{e.payment ? <Money value={e.payment} /> : ''}</td>
                  <td className="num"><Money value={e.balance} /></td>
                  <td className="num">
                    {lease.status === 'Active' && e.kind === 'Charge' && owedThrough.has(e.id) &&
                      <button className="link" onClick={() => markPaid(e)} title="Record a payment covering this and any older unpaid charges">Mark paid</button>}
                    {!e.isVoided && <button className="link danger" onClick={() => voidEntry(e)}>Void</button>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        <Pager info={shown} total={ledger.length} size={pageSize} onPage={setPage}
          onSize={size => { setPageSize(size); setPage(null) }} />
      </Panel>

      <Panel title="Statements">
        <ErrorBanner message={invoices.error} />
        {invoices.data && <InvoiceTable invoices={invoices.data} showTenant={false} onChanged={invoices.reload} onError={setError} />}
      </Panel>
    </>
  )
}

function StatementForm({ leaseId, onDone, onCancel }: { leaseId: number; onDone: () => void; onCancel: () => void }) {
  const charges = useApi<OpenCharge[]>(`/leases/${leaseId}/open-charges`)
  const profile = useApi<BusinessProfile>('/settings/business')
  const [issueDate, setIssueDate] = useState(todayIso())
  const [dueDate, setDueDate] = useState<string>()
  const [excluded, setExcluded] = useState<Set<number>>(new Set())
  const [notes, setNotes] = useState('')
  const { error, saving, run } = useSubmit()

  const dueDays = profile.data?.defaultDueDays ?? 7
  const due = dueDate ?? addDays(issueDate, dueDays)
  const selected = (charges.data ?? []).filter(c => !excluded.has(c.id))
  const toggle = (id: number) => setExcluded(s => { const n = new Set(s); if (n.has(id)) n.delete(id); else n.add(id); return n })

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    const ok = await run(() => api.post(`/leases/${leaseId}/invoices`, {
      issueDate, dueDate: due, chargeIds: selected.map(c => c.id), notes: notes || null,
    }))
    if (ok) onDone() // the new statement appears under Statements with its PDF link
  }

  if (profile.data && !profile.data.name)
    return <Panel title="Create statement"><div className="notice">Add your business details in <Link to="/settings">Settings</Link> first.</div></Panel>

  return (
    <Panel title="Create statement">
      <form onSubmit={submit}>
        {charges.data?.length === 0 ? <Empty>No unpaid charges to bill.</Empty> : (
          <table>
            <thead><tr><th /><th>Charge</th><th>Due</th><th className="num">Amount</th><th className="num">Paid</th><th className="num">Balance</th></tr></thead>
            <tbody>
              {charges.data?.map(c => (
                <tr key={c.id}>
                  <td><input type="checkbox" className="check" checked={!excluded.has(c.id)} onChange={() => toggle(c.id)} aria-label={c.description} /></td>
                  <td>{c.description}</td>
                  <td>{formatDate(c.dueDate)}</td>
                  <td className="num"><Money value={c.amount} /></td>
                  <td className="num">{c.paid ? <Money value={c.paid} /> : ''}</td>
                  <td className="num"><Money value={c.balance} /></td>
                </tr>
              ))}
            </tbody>
            <tfoot><tr><td colSpan={5}>Amount due</td><td className="num"><Money value={selected.reduce((s, c) => s + c.balance, 0)} /></td></tr></tfoot>
          </table>
        )}
        <div className="form-grid" style={{ marginTop: 12 }}>
          <Field label="Issue date"><input type="date" value={issueDate} onChange={e => e.target.value && setIssueDate(e.target.value)} required /></Field>
          <Field label="Due date" hint={`Default: ${dueDays} days after issue`}><input type="date" value={due} min={issueDate} onChange={e => setDueDate(e.target.value)} required /></Field>
        </div>
        <Field label="Note on statement (optional)"><textarea rows={2} value={notes} onChange={e => setNotes(e.target.value)} /></Field>
        <div className="form-buttons">
          <button type="submit" disabled={saving || selected.length === 0}>Issue statement</button>
          <button type="button" className="secondary" onClick={onCancel}>Cancel</button>
        </div>
      </form>
      <p className="muted">Once issued, a statement can't be edited, only voided. Its number is never reused.</p>
      <ErrorBanner message={error ?? charges.error} />
    </Panel>
  )
}

function PaymentForm({ leaseId, suggested, through, onDone, onCancel }: {
  leaseId: number; suggested: number; through?: string; onDone: () => void; onCancel: () => void
}) {
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
    <Panel title={through ? `Mark paid: ${through}` : 'Record payment'}>
      {through && <p className="muted">The amount covers this charge and any older unpaid ones, since payments are applied oldest first.</p>}
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

function EditLeaseForm({ lease, onDone, onCancel }: { lease: LeaseInfo; onDone: () => void; onCancel: () => void }) {
  const [f, setF] = useState({
    rent: String(lease.rent), dueDay: String(lease.dueDay), gracePeriodDays: String(lease.gracePeriodDays),
    endDate: lease.endDate ?? '', securityDeposit: String(lease.securityDeposit), notes: lease.notes ?? '',
  })
  const set = (k: keyof typeof f) => (e: { target: { value: string } }) => setF(s => ({ ...s, [k]: e.target.value }))
  const { error, saving, run } = useSubmit()

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    const ok = await run(() => api.put(`/leases/${lease.id}`, {
      rent: Number(f.rent), dueDay: Number(f.dueDay), gracePeriodDays: Number(f.gracePeriodDays) || 0,
      endDate: f.endDate || null, securityDeposit: Number(f.securityDeposit) || 0, notes: f.notes.trim() || null,
    }))
    if (ok) onDone()
  }

  return (
    <Panel title="Edit lease">
      <form onSubmit={submit}>
        <div className="form-grid">
          <Field label={`${rentLabel(lease.frequency)} (₱)`} hint="Applies to rent not yet billed; past charges stay as they are">
            <input type="number" min="0.01" step="0.01" value={f.rent} onChange={set('rent')} required autoFocus /></Field>
          {lease.frequency === 'Monthly' && <Field label="Due day" hint="29th–31st moves to the last day of shorter months">
            <input type="number" min="1" max="31" value={f.dueDay} onChange={set('dueDay')} required /></Field>}
          <Field label="Grace period (days)"><input type="number" min="0" max="60" value={f.gracePeriodDays} onChange={set('gracePeriodDays')} /></Field>
          <Field label="Contract end date" hint="Leave blank for open-ended. To record a move-out, use End lease">
            <input type="date" value={f.endDate} min={lease.startDate} onChange={set('endDate')} /></Field>
          <Field label="Security deposit held (₱)"><input type="number" min="0" step="0.01" value={f.securityDeposit} onChange={set('securityDeposit')} /></Field>
        </div>
        <Field label="Notes"><textarea rows={2} value={f.notes} onChange={set('notes')} /></Field>
        <div className="form-buttons">
          <button type="submit" disabled={saving}>Save</button>
          <button type="button" className="secondary" onClick={onCancel}>Cancel</button>
        </div>
      </form>
      <p className="muted">Tenant, unit and start date can't be changed. To correct them, end this lease and create a new one.</p>
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

const rentLabel = (f: RentFrequency) => f === 'Daily' ? 'Daily rent' : 'Monthly rent'

const ordinal = (n: number) => {
  const s = n % 100 >= 11 && n % 100 <= 13 ? 'th' : ({ 1: 'st', 2: 'nd', 3: 'rd' } as Record<number, string>)[n % 10] ?? 'th'
  return `${n}${s}`
}
