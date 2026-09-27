// Thin typed wrapper over the .NET API. Dates travel as "yyyy-MM-dd" strings.

export type LeaseStatus = 'Active' | 'Ended'
export type ChargeType = 'Rent' | 'Utility' | 'LateFee' | 'Other'
export type PaymentMethod = 'Cash' | 'BankTransfer' | 'GCash' | 'Maya' | 'Check' | 'Other'
export type AgingBucket = 'Current' | 'Days1To30' | 'Days31To60' | 'Days61To90' | 'Over90'

export interface UnitSummary { id: number; name: string; defaultMonthlyRent: number; notes?: string; currentTenant?: string }
export interface Property { id: number; name: string; address?: string; notes?: string; units: UnitSummary[] }
export interface Tenant { id: number; fullName: string; email?: string; phone?: string; tin?: string; notes?: string; activeLeases: number }

export interface LeaseSummary {
  id: number; status: LeaseStatus; startDate: string; endDate?: string; monthlyRent: number; dueDay: number
  property: string; unit: string; tenant: string; balance: number
}
export interface LeaseInfo {
  id: number; unitId: number; tenantId: number; status: LeaseStatus; startDate: string; endDate?: string
  monthlyRent: number; dueDay: number; gracePeriodDays: number; securityDeposit: number; notes?: string
  property: string; unit: string; tenant: string; phone?: string; email?: string
}
export interface LedgerEntry {
  date: string; kind: 'Charge' | 'Payment'; id: number; description: string
  charge: number; payment: number; balance: number; isVoided: boolean
}
export interface LeaseDetail { lease: LeaseInfo; ledger: LedgerEntry[]; balance: number }

export interface AgingRow {
  leaseId: number; chargeId: number; property: string; unit: string; tenant: string; phone?: string
  description: string; dueDate: string; amount: number; paid: number; outstanding: number
  daysOverdue: number; bucket: AgingBucket; bucketLabel: string
}
export interface Dashboard {
  totalOutstanding: number; totalOverdue: number; overdueLeases: number; tenantCredit: number; activeLeases: number; units: number
}

export class ApiError extends Error {}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const res = await fetch(`/api${url}`, {
    method,
    // The API rejects changes without this header, which other websites can't send (see CrossSiteGuard.cs).
    headers: {
      'X-Requested-With': 'BuildingManager',
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!res.ok) {
    const problem = await res.json().catch(() => null)
    const messages = problem?.errors ? Object.values(problem.errors).flat().join(' ') : problem?.title
    throw new ApiError(messages || `Request failed (${res.status})`)
  }
  const text = await res.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const api = {
  get: <T>(url: string) => request<T>('GET', url),
  post: <T = unknown>(url: string, body?: unknown) => request<T>('POST', url, body ?? {}),
  put: (url: string, body: unknown) => request<void>('PUT', url, body),
  del: (url: string) => request<void>('DELETE', url),
}

const peso = new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP' })
export const formatPeso = (n: number) => peso.format(n)

export const formatDate = (iso?: string) =>
  iso ? new Date(`${iso}T00:00:00`).toLocaleDateString('en-PH', { day: 'numeric', month: 'short', year: 'numeric' }) : '—'

export const todayIso = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

export const paymentMethodLabels: Record<PaymentMethod, string> = {
  Cash: 'Cash', BankTransfer: 'Bank transfer', GCash: 'GCash', Maya: 'Maya', Check: 'Check', Other: 'Other',
}
