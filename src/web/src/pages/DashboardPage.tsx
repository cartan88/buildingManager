import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { type AgingRow, type Dashboard, type PnlReport, formatDate, monthEnd, monthStart, todayIso } from '../api'
import { Empty, ErrorBanner, Money, PageHeader, Panel, useApi } from '../ui'

export default function DashboardPage() {
  const { data, error } = useApi<Dashboard>('/dashboard')
  const aging = useApi<AgingRow[]>('/reports/aging')
  const month = useApi<PnlReport>(`/reports/pnl?from=${monthStart(todayIso())}&to=${monthEnd(todayIso())}&basis=Cash`)
  const overdue = (aging.data ?? []).filter(r => r.bucket !== 'Current')

  return (
    <>
      <PageHeader title="Dashboard" />
      <ErrorBanner message={error ?? aging.error} />
      {data && (
        <div className="stats">
          <Stat label="Overdue" value={<Money value={data.totalOverdue} className={data.totalOverdue > 0 ? 'alert' : ''} />}
            sub={`${data.overdueLeases} lease${data.overdueLeases === 1 ? '' : 's'} behind`} />
          <Stat label="Total receivable" value={<Money value={data.totalOutstanding} />} sub="Includes charges still within grace" />
          <Stat label="Tenant credit" value={<Money value={data.tenantCredit} />} sub="Advance payments not yet applied" />
          <Stat label="Occupancy" value={`${data.activeLeases} / ${data.units}`} sub="Active leases / units" />
        </div>
      )}

      {month.data && (
        <div className="stats">
          <Stat label="Received this month" value={<Money value={month.data.income.total} />} sub="Cash basis" />
          <Stat label="Expenses this month" value={<Money value={month.data.operatingExpenses.total} />}
            sub={month.data.capitalExpenses.total ? `Plus capital items not counted` : 'Operating costs'} />
          <Stat label="Net this month" value={<Money value={month.data.netIncome.total} />} sub={<Link to="/profit-loss">Profit & loss →</Link>} />
        </div>
      )}

      <Panel title="Overdue charges">
        {overdue.length === 0 ? <Empty>Nothing overdue. 🎉</Empty> : (
          <table>
            <thead><tr><th>Tenant</th><th>Unit</th><th>Charge</th><th>Due</th><th className="num">Days late</th><th className="num">Outstanding</th></tr></thead>
            <tbody>
              {overdue.slice(0, 10).map(r => (
                <tr key={r.chargeId}>
                  <td><Link to={`/leases/${r.leaseId}`}>{r.tenant}</Link></td>
                  <td>{r.property} · {r.unit}</td>
                  <td>{r.description}</td>
                  <td>{formatDate(r.dueDate)}</td>
                  <td className="num">{r.daysOverdue}</td>
                  <td className="num"><Money value={r.outstanding} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {overdue.length > 10 && <p><Link to="/overdue">See all {overdue.length} overdue charges →</Link></p>}
      </Panel>
    </>
  )
}

function Stat({ label, value, sub }: { label: string; value: ReactNode; sub: ReactNode }) {
  return (
    <div className="stat">
      <div className="stat-label">{label}</div>
      <div className="stat-value">{value}</div>
      <div className="stat-sub">{sub}</div>
    </div>
  )
}
