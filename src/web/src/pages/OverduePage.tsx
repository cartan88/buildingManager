import { useState } from 'react'
import { Link } from 'react-router-dom'
import { type AgingBucket, type AgingRow, formatDate, todayIso } from '../api'
import { Empty, ErrorBanner, Field, Money, PageHeader, Panel, useApi } from '../ui'

const buckets: { key: AgingBucket; label: string }[] = [
  { key: 'Current', label: 'Not yet overdue' },
  { key: 'Days1To30', label: '1–30 days' },
  { key: 'Days31To60', label: '31–60 days' },
  { key: 'Days61To90', label: '61–90 days' },
  { key: 'Over90', label: '90+ days' },
]

export default function OverduePage() {
  const [asOf, setAsOf] = useState(todayIso())
  const { data, error } = useApi<AgingRow[]>(`/reports/aging?asOf=${asOf}`)
  const rows = data ?? []

  const byLease = Object.values(
    rows.reduce<Record<number, { leaseId: number; tenant: string; place: string; phone?: string; totals: Record<string, number> }>>((acc, r) => {
      const e = (acc[r.leaseId] ??= { leaseId: r.leaseId, tenant: r.tenant, place: `${r.property} · ${r.unit}`, phone: r.phone, totals: {} })
      e.totals[r.bucket] = (e.totals[r.bucket] ?? 0) + r.outstanding
      return acc
    }, {}),
  )
  const sum = (b: AgingBucket) => rows.filter(r => r.bucket === b).reduce((s, r) => s + r.outstanding, 0)

  return (
    <>
      <PageHeader title="Overdue & aging">
        <Field label="As of">
          <input type="date" value={asOf} onChange={e => e.target.value && setAsOf(e.target.value)} />
        </Field>
        <a className="button" href={`/api/reports/aging.xlsx?asOf=${asOf}`}>Export to Excel</a>
      </PageHeader>
      <ErrorBanner message={error} />

      <Panel title="Summary by tenant">
        {byLease.length === 0 ? <Empty>No unpaid charges as of {formatDate(asOf)}.</Empty> : (
          <table>
            <thead>
              <tr><th>Tenant</th><th>Unit</th><th>Phone</th>{buckets.map(b => <th key={b.key} className="num">{b.label}</th>)}<th className="num">Total</th></tr>
            </thead>
            <tbody>
              {byLease.map(l => (
                <tr key={l.leaseId}>
                  <td><Link to={`/leases/${l.leaseId}`}>{l.tenant}</Link></td>
                  <td>{l.place}</td>
                  <td>{l.phone ?? '—'}</td>
                  {buckets.map(b => <td key={b.key} className="num">{l.totals[b.key] ? <Money value={l.totals[b.key]} /> : ''}</td>)}
                  <td className="num"><Money value={Object.values(l.totals).reduce((a, b) => a + b, 0)} /></td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr><td colSpan={3}>Total</td>{buckets.map(b => <td key={b.key} className="num"><Money value={sum(b.key)} /></td>)}
                <td className="num"><Money value={rows.reduce((s, r) => s + r.outstanding, 0)} /></td></tr>
            </tfoot>
          </table>
        )}
      </Panel>

      {rows.length > 0 && (
        <Panel title="Unpaid charges">
          <table>
            <thead><tr><th>Tenant</th><th>Charge</th><th>Due</th><th className="num">Amount</th><th className="num">Paid</th><th className="num">Outstanding</th><th>Status</th></tr></thead>
            <tbody>
              {rows.map(r => (
                <tr key={r.chargeId}>
                  <td><Link to={`/leases/${r.leaseId}`}>{r.tenant}</Link></td>
                  <td>{r.description}</td>
                  <td>{formatDate(r.dueDate)}</td>
                  <td className="num"><Money value={r.amount} /></td>
                  <td className="num"><Money value={r.paid} /></td>
                  <td className="num"><Money value={r.outstanding} /></td>
                  <td><span className={`badge ${r.bucket === 'Current' ? '' : 'badge-alert'}`}>
                    {r.bucket === 'Current' ? 'Not yet overdue' : `${r.daysOverdue} days late`}</span></td>
                </tr>
              ))}
            </tbody>
          </table>
        </Panel>
      )}
    </>
  )
}
