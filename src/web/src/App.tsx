import { useEffect } from 'react'
import { NavLink, Route, Routes } from 'react-router-dom'
import { BRANDING_CHANGED, logoUrl, type Branding } from './api'
import { useApi } from './ui'
import DashboardPage from './pages/DashboardPage'
import PropertiesPage from './pages/PropertiesPage'
import TenantsPage from './pages/TenantsPage'
import LeasesPage from './pages/LeasesPage'
import LeaseDetailPage from './pages/LeaseDetailPage'
import OverduePage from './pages/OverduePage'
import InvoicesPage from './pages/InvoicesPage'
import SettingsPage from './pages/SettingsPage'
import ExpensesPage from './pages/ExpensesPage'
import ProfitLossPage from './pages/ProfitLossPage'

const nav = [
  { to: '/', label: 'Dashboard', end: true },
  { to: '/overdue', label: 'Overdue & aging' },
  { to: '/leases', label: 'Leases' },
  { to: '/statements', label: 'Statements' },
  { to: '/expenses', label: 'Expenses' },
  { to: '/profit-loss', label: 'Profit & loss' },
  { to: '/properties', label: 'Properties & units' },
  { to: '/tenants', label: 'Tenants' },
  { to: '/settings', label: 'Settings' },
]

/** The business name and logo, kept up to date when they change in Settings. */
function useBranding() {
  const { data, reload } = useApi<Branding>('/settings/branding')
  useEffect(() => {
    const onChange = () => void reload()
    window.addEventListener(BRANDING_CHANGED, onChange)
    return () => window.removeEventListener(BRANDING_CHANGED, onChange)
  }, [reload])
  return data
}

function Brand({ data }: { data?: Branding }) {
  const name = data?.name ?? 'Building Manager'
  useEffect(() => { document.title = name }, [name])
  const logo = data && logoUrl(data)

  return (
    <div className="brand">
      {logo && <img src={logo} alt="" className="brand-logo" />}
      <span className="brand-name">{name}</span>
    </div>
  )
}

export default function App() {
  const branding = useBranding()
  return (
    <div className="shell">
      <aside className="sidebar">
        <Brand data={branding} />
        <nav>
          {nav.map(n => (
            <NavLink key={n.to} to={n.to} end={n.end}>{n.label}</NavLink>
          ))}
        </nav>
        <p className="copyright">© {new Date().getFullYear()} {branding?.name ?? 'Building Manager'}</p>
      </aside>
      <main className="content">
        <Routes>
          <Route path="/" element={<DashboardPage />} />
          <Route path="/overdue" element={<OverduePage />} />
          <Route path="/leases" element={<LeasesPage />} />
          <Route path="/leases/:id" element={<LeaseDetailPage />} />
          <Route path="/properties" element={<PropertiesPage />} />
          <Route path="/tenants" element={<TenantsPage />} />
          <Route path="/statements" element={<InvoicesPage />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="/expenses" element={<ExpensesPage />} />
          <Route path="/profit-loss" element={<ProfitLossPage />} />
        </Routes>
      </main>
    </div>
  )
}
