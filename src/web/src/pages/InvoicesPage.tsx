import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { addDays, api, todayIso, type BusinessProfile, type InvoiceSummary } from '../api'
import { InvoiceTable } from '../invoiceUi'
import { ErrorBanner, Field, PageHeader, Panel, useApi, useSubmit } from '../ui'

export default function InvoicesPage() {
  const { data, error, reload } = useApi<InvoiceSummary[]>('/invoices')
  const profile = useApi<BusinessProfile>('/settings/business')
  const [batchOpen, setBatchOpen] = useState(false)
  const [message, setMessage] = useState<string>()
  const [actionError, setActionError] = useState<string>()
  const needsSetup = profile.data && !profile.data.name

  return (
    <>
      <PageHeader title="Billing statements">
        <a className="button secondary-button" href="/api/invoices.xlsx">Export to Excel</a>
        {!batchOpen && !needsSetup && <button onClick={() => { setBatchOpen(true); setMessage(undefined) }}>Issue statements for all</button>}
      </PageHeader>
      <ErrorBanner message={error ?? actionError} />
      {needsSetup && (
        <div className="notice">Add your business details in <Link to="/settings">Settings</Link> before issuing statements.</div>
      )}
      {message && <div className="notice ok">{message}</div>}

      {batchOpen && profile.data && (
        <BatchForm defaultDueDays={profile.data.defaultDueDays} onCancel={() => setBatchOpen(false)}
          onDone={(count, numbers) => {
            setBatchOpen(false)
            setMessage(count === 0
              ? 'Nothing new to bill: every unpaid charge is already on a statement.'
              : `Issued ${count} statement${count === 1 ? '' : 's'}: ${numbers.join(', ')}.`)
            void reload()
          }} />
      )}

      <Panel>
        {data && <InvoiceTable invoices={data} showTenant onChanged={reload} onError={setActionError} />}
      </Panel>
    </>
  )
}

function BatchForm({ defaultDueDays, onDone, onCancel }: {
  defaultDueDays: number; onDone: (count: number, numbers: string[]) => void; onCancel: () => void
}) {
  const [issueDate, setIssueDate] = useState(todayIso())
  const [dueDate, setDueDate] = useState(addDays(todayIso(), defaultDueDays))
  const { error, saving, run } = useSubmit()

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    let result = { count: 0, numbers: [] as string[] }
    if (await run(async () => { result = await api.post('/invoices/batch', { issueDate, dueDate }) })) onDone(result.count, result.numbers)
  }

  return (
    <Panel title="Issue statements for all tenants">
      <p className="muted">Creates one statement for each active lease that has unpaid charges not yet on a statement.
        Each statement lists all of that tenant's unpaid charges, including arrears.</p>
      <form onSubmit={submit} className="form-row">
        <Field label="Issue date">
          <input type="date" value={issueDate} required
            onChange={e => { setIssueDate(e.target.value); if (e.target.value) setDueDate(addDays(e.target.value, defaultDueDays)) }} />
        </Field>
        <Field label="Due date"><input type="date" value={dueDate} min={issueDate} onChange={e => setDueDate(e.target.value)} required /></Field>
        <div className="form-buttons">
          <button type="submit" disabled={saving}>{saving ? 'Issuing…' : 'Issue statements'}</button>
          <button type="button" className="secondary" onClick={onCancel}>Cancel</button>
        </div>
      </form>
      <ErrorBanner message={error} />
    </Panel>
  )
}
