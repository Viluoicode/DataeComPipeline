import { useState } from 'react'
import { Dashboard } from './pages/Dashboard'
import { StressTest } from './pages/StressTest'
import './App.css'

type Tab = 'dashboard' | 'stress'

export default function App() {
  const [tab, setTab] = useState<Tab>('dashboard')

  return (
    <div className="app">
      <header className="header">
        <h1>📊 ECommerPipeline</h1>
        <p className="subtitle">OLTP → ETL → OLAP — Real-time Analytics Dashboard</p>
        <nav className="tabs">
          <button
            className={tab === 'dashboard' ? 'active' : ''}
            onClick={() => setTab('dashboard')}>
            Dashboard
          </button>
          <button
            className={tab === 'stress' ? 'active' : ''}
            onClick={() => setTab('stress')}>
            Stress Test
          </button>
          <a href="/hangfire" target="_blank" rel="noreferrer" className="external">
            Hangfire ↗
          </a>
        </nav>
      </header>

      <main className="main">
        {tab === 'dashboard' && <Dashboard />}
        {tab === 'stress' && <StressTest />}
      </main>
    </div>
  )
}
