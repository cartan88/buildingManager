import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { api, formatPeso } from './api'
import { PAGE_SIZES, type PageInfo } from './pagination'

/** Loads a GET endpoint; call reload() after a mutation to refresh. */
export function useApi<T>(url: string | null) {
  const [data, setData] = useState<T>()
  const [error, setError] = useState<string>()
  const [loading, setLoading] = useState(true)

  const reload = useCallback(async () => {
    if (!url) return
    setLoading(true)
    try {
      setData(await api.get<T>(url))
      setError(undefined)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setLoading(false)
    }
  }, [url])

  useEffect(() => { void reload() }, [reload])
  return { data, error, loading, reload }
}

/** Wraps a form submit: shows API validation messages, disables while saving. */
export function useSubmit() {
  const [error, setError] = useState<string>()
  const [saving, setSaving] = useState(false)

  const run = async (action: () => Promise<unknown>) => {
    setSaving(true)
    setError(undefined)
    try {
      await action()
      return true
    } catch (e) {
      setError((e as Error).message)
      return false
    } finally {
      setSaving(false)
    }
  }
  return { error, saving, run, setError }
}

export const Money = ({ value, className }: { value: number; className?: string }) => (
  <span className={`money ${className ?? ''} ${value < 0 ? 'negative' : ''}`}>{formatPeso(value)}</span>
)

export const ErrorBanner = ({ message }: { message?: string }) =>
  message ? <div className="error-banner" role="alert">{message}</div> : null

export const PageHeader = ({ title, children }: { title: string; children?: ReactNode }) => (
  <div className="page-header">
    <h1>{title}</h1>
    <div className="actions">{children}</div>
  </div>
)

export const Panel = ({ title, children }: { title?: string; children: ReactNode }) => (
  <section className="panel">
    {title && <h2>{title}</h2>}
    {children}
  </section>
)

export const Field = ({ label, children, hint }: { label: string; children: ReactNode; hint?: string }) => (
  <label className="field">
    <span>{label}</span>
    {children}
    {hint && <small>{hint}</small>}
  </label>
)

/** Prev / next controls and a rows-per-page picker. Hidden when everything fits on one page at the smallest size. */
export const Pager = ({ info, total, size, onPage, onSize }: {
  info: PageInfo; total: number; size: number; onPage: (page: number) => void; onSize: (size: number) => void
}) => total <= PAGE_SIZES[0] ? null : (
  <div className="pager">
    <span className="muted">{info.start + 1}–{info.end} of {total}</span>
    <button type="button" className="secondary" onClick={() => onPage(info.page - 1)} disabled={info.page <= 1}>‹ Prev</button>
    <span>Page {info.page} of {info.pageCount}</span>
    <button type="button" className="secondary" onClick={() => onPage(info.page + 1)} disabled={info.page >= info.pageCount}>Next ›</button>
    <select value={size} onChange={e => onSize(Number(e.target.value))} aria-label="Rows per page">
      {PAGE_SIZES.map(n => <option key={n} value={n}>{n} per page</option>)}
    </select>
  </div>
)

export const Empty = ({ children }: { children: ReactNode }) => <p className="empty">{children}</p>
