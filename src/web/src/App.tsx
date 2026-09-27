import { NavLink, Route, Routes } from 'react-router-dom'
import DashboardPage from './pages/DashboardPage'
import PropertiesPage from './pages/PropertiesPage'
import TenantsPage from './pages/TenantsPage'
import LeasesPage from './pages/LeasesPage'
import LeaseDetailPage from './pages/LeaseDetailPage'
import OverduePage from './pages/OverduePage'
import InvoicesPage from './pages/InvoicesPage'
import SettingsPage from './pages/SettingsPage'

const nav = [
  { to: '/', label: 'Dashboard', end: true },
  { to: '/overdue', label: 'Overdue & aging' },
  { to: '/leases', label: 'Leases' },
  { to: '/statements', label: 'Statements' },
  { to: '/properties', label: 'Properties & units' },
  { to: '/tenants', label: 'Tenants' },
  { to: '/settings', label: 'Settings' },
]

export default function App() {
  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">Building Manager</div>
        <nav>
          {nav.map(n => (
            <NavLink key={n.to} to={n.to} end={n.end}>{n.label}</NavLink>
          ))}
        </nav>
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
        </Routes>
      </main>
    </div>
  )
}
