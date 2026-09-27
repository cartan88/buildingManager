import { useState } from 'react'
import { addDays, monthEnd, monthStart, todayIso, type PnlReport, type PnlSection, type Property } from '../api'
import { Empty, ErrorBanner, Field, Money, PageHeader, Panel, useApi } from '../ui'

type Preset = 'thisMonth' | 'lastMonth' | 'thisYear' | 'lastYear' | 'custom'

function rangeFor(preset: Exclude<Preset, 'custom'>): [string, string] {
  const today = todayIso()
  const year = Number(today.slice(0, 4))
  switch (preset) {
    case 'thisMonth': return [monthStart(today), monthEnd(today)]
    case 'lastMonth': { const last = addDays(monthStart(today), -1); return [monthStart(last), last] }
    case 'thisYear': return [`${year}-01-01`, `${year}-12-31`]
    case 'lastYear': return [`${year - 1}-01-01`, `${year - 1}-12-31`]
  }
}

export default function ProfitLossPage() {
  const [preset, setPreset] = useState<Preset>('thisYear')
  const [[from, to], setRange] = useState(rangeFor('thisYear'))
  const [basis, setBasis] = useState<'Cash' | 'Accrual'>('Cash')
  const [by, setBy] = useState<'Property' | 'Month'>('Property')
  const [propertyId, setPropertyId] = useState('')

  const query = new URLSearchParams({ from, to, basis, by })
  if (propertyId) query.set('propertyId', propertyId)
  const { data, error } = useApi<PnlReport>(`/reports/pnl?${query}`)
  const properties = useApi<Property[]>('/properties')

  const pick = (p: Preset) => { setPreset(p); if (p !== 'custom') setRange(rangeFor(p)) }

  return (
    <>
      <PageHeader title="Profit & loss">
        <a className="button" href={`/api/reports/pnl.xlsx?${query}`}>Export to Excel</a>
      </PageHeader>

      <Panel>
        <div className="form-row compact-top">
          <Field label="Period">
            <select value={preset} onChange={e => pick(e.target.value as Preset)}>
              <option value="thisMonth">This month</option>
              <option value="lastMonth">Last month</option>
              <option value="thisYear">This year</option>
              <option value="lastYear">Last year</option>
              <option value="custom">Custom…</option>
            </select>
          </Field>
          {preset === 'custom' && <>
            <Field label="From"><input type="date" value={from} onChange={e => e.target.value && setRange([e.target.value, to])} /></Field>
            <Field label="To"><input type="date" value={to} min={from} onChange={e => e.target.value && setRange([from, e.target.value])} /></Field>
          </>}
          <Field label="Basis" hint={basis === 'Cash' ? 'Money actually received' : 'Amounts billed, paid or not'}>
            <select value={basis} onChange={e => setBasis(e.target.value as 'Cash' | 'Accrual')}>
              <option value="Cash">Cash</option>
              <option value="Accrual">Accrual</option>
            </select>
          </Field>
          <Field label="Columns">
            <select value={by} onChange={e => setBy(e.target.value as 'Property' | 'Month')}>
              <option value="Property">By property</option>
              <option value="Month">By month</option>
            </select>
          </Field>
          <Field label="Property">
            <select value={propertyId} onChange={e => setPropertyId(e.target.value)}>
              <option value="">All properties</option>
              {properties.data?.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
          </Field>
        </div>
      </Panel>

      <ErrorBanner message={error} />
      {data && (data.columns.length === 0 ? <Empty>Add a property first.</Empty> : <PnlTable report={data} />)}
    </>
  )
}

function PnlTable({ report }: { report: PnlReport }) {
  const cols = report.columns
  const lines = (section: PnlSection) => report.lines.filter(l => l.section === section)
  const capital = lines('CapitalExpense')

  const section = (title: string, section: PnlSection, total: PnlReport['income'], totalLabel: string) => (
    <>
      <tr key={`${section}-title`} className="section-row"><td colSpan={cols.length + 2}>{title}</td></tr>
      {lines(section).length === 0 && <tr><td className="muted indent" colSpan={cols.length + 2}>None</td></tr>}
      {lines(section).map(l => (
        <tr key={l.label}>
          <td className="indent">{l.label}</td>
          {cols.map(c => <td key={c.key} className="num">{l.amounts[c.key] ? <Money value={l.amounts[c.key]} /> : ''}</td>)}
          <td className="num"><Money value={l.total} /></td>
        </tr>
      ))}
      <tr className="subtotal">
        <td>{totalLabel}</td>
        {cols.map(c => <td key={c.key} className="num"><Money value={total.byColumn[c.key]} /></td>)}
        <td className="num"><Money value={total.total} /></td>
      </tr>
    </>
  )

  return (
    <Panel>
      <table className="pnl">
        <thead><tr><th />{cols.map(c => <th key={c.key} className="num">{c.label}</th>)}<th className="num">Total</th></tr></thead>
        <tbody>
          {section('Income', 'Income', report.income, 'Total income')}
          {section('Operating expenses', 'OperatingExpense', report.operatingExpenses, 'Total operating expenses')}
          <tr className="net">
            <td>Net income</td>
            {cols.map(c => <td key={c.key} className="num"><Money value={report.netIncome.byColumn[c.key]} /></td>)}
            <td className="num"><Money value={report.netIncome.total} /></td>
          </tr>
          {capital.length > 0 && (
            section('Capital expenses (not in net income)', 'CapitalExpense', report.capitalExpenses, 'Total capital expenses')
          )}
        </tbody>
      </table>
      {capital.length > 0 && <p className="muted">Capital items are usually depreciated over several years rather than deducted at once. Ask your accountant.</p>}
    </Panel>
  )
}
