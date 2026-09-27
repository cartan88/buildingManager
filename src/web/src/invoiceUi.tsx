import { Link } from 'react-router-dom'
import { api, formatDate, paymentStatusLabels, todayIso, type InvoiceSummary } from './api'
import { Empty, Money } from './ui'

const badgeClass: Record<InvoiceSummary['paymentStatus'], string> = {
  Paid: 'badge-ok', PartiallyPaid: 'badge-warn', Unpaid: '', Voided: '',
}

export function PaymentBadge({ invoice }: { invoice: InvoiceSummary }) {
  const overdue = invoice.paymentStatus !== 'Paid' && invoice.status === 'Issued' && invoice.dueDate < todayIso()
  return (
    <span className={`badge ${overdue ? 'badge-alert' : badgeClass[invoice.paymentStatus]}`}>
      {paymentStatusLabels[invoice.paymentStatus]}{overdue ? ' · overdue' : ''}
    </span>
  )
}

/** Table of statements, shared by the Statements page and each lease's page. */
export function InvoiceTable({ invoices, showTenant, onChanged, onError }: {
  invoices: InvoiceSummary[]; showTenant: boolean; onChanged: () => void; onError: (message: string) => void
}) {
  if (invoices.length === 0) return <Empty>No statements yet.</Empty>

  const voidInvoice = async (inv: InvoiceSummary) => {
    const reason = prompt(`Void ${inv.number}? Its number stays used and the PDF will be stamped VOID.\n\nReason (optional):`)
    if (reason === null) return
    try {
      await api.post(`/invoices/${inv.id}/void`, { reason })
      onChanged()
    } catch (e) {
      onError((e as Error).message)
    }
  }

  return (
    <table>
      <thead>
        <tr>
          <th>No.</th><th>Issued</th>{showTenant && <th>Tenant</th>}<th>Due</th>
          <th className="num">Amount</th><th className="num">Still owed</th><th>Payment</th><th />
        </tr>
      </thead>
      <tbody>
        {invoices.map(inv => (
          <tr key={inv.id} className={inv.status === 'Voided' ? 'voided' : ''}>
            <td><a href={`/api/invoices/${inv.id}/pdf`} target="_blank" rel="noreferrer">{inv.number}</a></td>
            <td>{formatDate(inv.issueDate)}</td>
            {showTenant && <td><Link to={`/leases/${inv.leaseId}`}>{inv.tenantName}</Link> <span className="muted">· {inv.unitName}</span></td>}
            <td>{formatDate(inv.dueDate)}</td>
            <td className="num"><Money value={inv.total} /></td>
            <td className="num">{inv.status === 'Voided' ? '' : <Money value={inv.stillOwed} />}</td>
            <td>{inv.status === 'Voided'
              ? <span className="badge" title={inv.voidReason}>Void{inv.voidReason ? `: ${inv.voidReason}` : ''}</span>
              : <PaymentBadge invoice={inv} />}</td>
            <td className="num">
              <a className="link-button" href={`/api/invoices/${inv.id}/pdf`} target="_blank" rel="noreferrer">PDF</a>
              {inv.status === 'Issued' && <button className="link danger" onClick={() => voidInvoice(inv)}>Void</button>}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
