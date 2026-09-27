import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { api, formatPeso } from './api'

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

export const Empty = ({ children }: { children: ReactNode }) => <p className="empty">{children}</p>
