// Thin typed wrapper over the .NET API. Dates travel as "yyyy-MM-dd" strings.

export type LeaseStatus = 'Active' | 'Ended'
export type RentFrequency = 'Monthly' | 'Daily'
/** "/day" or "/month", for showing a lease's rent. */
export const perPeriod = (f: RentFrequency) => f === 'Daily' ? '/day' : '/month'
export type ChargeType = 'Rent' | 'Utility' | 'LateFee' | 'Other'
export type PaymentMethod = 'Cash' | 'BankTransfer' | 'GCash' | 'Maya' | 'Check' | 'Other'
export type AgingBucket = 'Current' | 'Days1To30' | 'Days31To60' | 'Days61To90' | 'Over90'

export interface UnitSummary { id: number; name: string; defaultMonthlyRent: number; notes?: string; currentTenant?: string; hasLeases: boolean }
export interface Property { id: number; name: string; address?: string; notes?: string; units: UnitSummary[] }
export interface Tenant {
  id: number; fullName: string; email?: string; phone?: string; tin?: string; businessType?: string; notes?: string
  isActive: boolean; activeLeases: number; totalLeases: number
}

export interface LeaseSummary {
  id: number; status: LeaseStatus; startDate: string; endDate?: string; frequency: RentFrequency; rent: number; dueDay: number
  property: string; unit: string; tenant: string; balance: number
}
export interface LeaseInfo {
  id: number; unitId: number; tenantId: number; status: LeaseStatus; startDate: string; endDate?: string
  frequency: RentFrequency; rent: number; dueDay: number; gracePeriodDays: number; securityDeposit: number; notes?: string
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

export type InvoiceStatus = 'Issued' | 'Voided'
export type InvoicePaymentStatus = 'Unpaid' | 'PartiallyPaid' | 'Paid' | 'Voided'

export interface InvoiceSummary {
  id: number; leaseId: number; number: string; issueDate: string; dueDate: string; status: InvoiceStatus; voidReason?: string
  tenantName: string; propertyName: string; unitName: string; total: number; stillOwed: number; paymentStatus: InvoicePaymentStatus
}
export interface OpenCharge { id: number; type: ChargeType; description: string; dueDate: string; amount: number; paid: number; balance: number }
export interface BusinessProfile {
  name: string; address?: string; tin?: string; contact?: string; paymentInstructions?: string
  documentTitle: string; numberPrefix: string; footerNote?: string; defaultDueDays: number
}

/** Business name and logo shown in the sidebar. logoVersion is null when no logo is uploaded. */
export interface Branding { name?: string; logoVersion?: number }
export const logoUrl = (b: Branding) => b.logoVersion ? `/api/settings/logo?v=${b.logoVersion}` : undefined

/** Tells the sidebar to reload the branding after the name or logo changes in Settings. */
export const BRANDING_CHANGED = 'branding-changed'
export const notifyBrandingChanged = () => window.dispatchEvent(new Event(BRANDING_CHANGED))

export const paymentStatusLabels: Record<InvoicePaymentStatus, string> = {
  Unpaid: 'Unpaid', PartiallyPaid: 'Partially paid', Paid: 'Paid', Voided: 'Void',
}

export const addDays = (iso: string, days: number) => {
  const d = new Date(`${iso}T00:00:00`)
  d.setDate(d.getDate() + days)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

export type ExpenseCategoryKind = 'Operating' | 'Capital'
export interface ExpenseCategory { id: number; name: string; kind: ExpenseCategoryKind; sortOrder: number; isArchived: boolean }
export interface Receipt { id: number; fileName: string; contentType: string; sizeBytes: number }
export interface Expense {
  id: number; date: string; propertyId?: number; property?: string; categoryId: number; category: string; kind: ExpenseCategoryKind
  vendor?: string; description: string; amount: number; method: PaymentMethod; reference?: string; notes?: string
  isVoided: boolean; receipts: Receipt[]
}

export type PnlSection = 'Income' | 'OperatingExpense' | 'CapitalExpense'
export interface PnlTotals { byColumn: Record<string, number>; total: number }
export interface PnlReport {
  columns: { key: string; label: string }[]
  lines: { section: PnlSection; label: string; amounts: Record<string, number>; total: number }[]
  income: PnlTotals; operatingExpenses: PnlTotals; netIncome: PnlTotals; capitalExpenses: PnlTotals
}

/** Multipart upload; the browser sets the boundary, so no Content-Type header here. */
export async function uploadFile<T>(url: string, file: File): Promise<T> {
  const body = new FormData()
  body.append('file', file)
  const res = await fetch(`/api${url}`, { method: 'POST', body, headers: { 'X-Requested-With': 'BuildingManager' } })
  checkSignedIn(res, url)
  if (!res.ok) {
    const problem = await res.json().catch(() => null)
    throw new ApiError((problem?.errors ? Object.values(problem.errors).flat().join(' ') : problem?.title) || `Upload failed (${res.status})`)
  }
  return res.json() as Promise<T>
}

export const monthStart = (iso: string) => `${iso.slice(0, 7)}-01`
export const monthEnd = (iso: string) => addDays(monthStart(addDays(monthStart(iso), 32)), -1)

export class ApiError extends Error {}

/** Fired when the API says the session has ended (signed out elsewhere, password changed, timed out). */
export const SIGNED_OUT = 'signed-out'
const checkSignedIn = (res: Response, url: string) => {
  // /auth calls handle their own 401 (e.g. a wrong password on the sign-in form).
  if (res.status === 401 && !url.startsWith('/auth/')) window.dispatchEvent(new Event(SIGNED_OUT))
}

export interface AuthStatus { setupRequired: boolean; signedIn: boolean; username?: string }

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
  checkSignedIn(res, url)
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
