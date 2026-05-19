import { useEffect, useState, useCallback } from 'react'
import { reportsApi, type DateRange } from '../api/reports'
import type { SalesByCategoryRow, SalesByDayRow, TopProductRow } from '../types/api'
import { SalesByDayChart } from '../components/SalesByDayChart'
import { SalesByCategoryChart } from '../components/SalesByCategoryChart'
import { TopProductsChart } from '../components/TopProductsChart'
import { useEtlNotifications } from '../hooks/useEtlNotifications'

function defaultRange(): DateRange {
  const to = new Date()
  const from = new Date()
  from.setDate(from.getDate() - 90)
  return {
    from: from.toISOString().slice(0, 10),
    to:   to.toISOString().slice(0, 10),
  }
}

export function Dashboard() {
  const [range, setRange] = useState<DateRange>(defaultRange())
  const [byDay, setByDay] = useState<SalesByDayRow[]>([])
  const [byCategory, setByCategory] = useState<SalesByCategoryRow[]>([])
  const [topProducts, setTopProducts] = useState<TopProductRow[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async (r: DateRange) => {
    setLoading(true)
    setError(null)
    try {
      const [d, c, p] = await Promise.all([
        reportsApi.salesByDay(r),
        reportsApi.salesByCategory(r),
        reportsApi.topProducts(r, 10),
      ])
      setByDay(d)
      setByCategory(c)
      setTopProducts(p)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load reports')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { load(range) }, [load, range])

  // Auto-refresh dashboard when backend pushes ETL completion via SignalR
  const { status: signalRStatus, lastEvent } = useEtlNotifications(
    useCallback(() => { load(range) }, [load, range])
  )

  const totalRevenue = byCategory.reduce((s, r) => s + r.totalRevenue, 0)
  const totalOrders  = byCategory.reduce((s, r) => s + r.orderCount, 0)

  return (
    <div className="dashboard">
      <div className="filter-bar">
        <label>
          From:{' '}
          <input type="date" value={range.from}
                 onChange={e => setRange({ ...range, from: e.target.value })} />
        </label>
        <label>
          To:{' '}
          <input type="date" value={range.to}
                 onChange={e => setRange({ ...range, to: e.target.value })} />
        </label>
        <button onClick={() => load(range)} disabled={loading}>
          {loading ? 'Loading...' : 'Refresh'}
        </button>
        <span className={`signalr-badge ${signalRStatus}`}>
          ● SignalR: {signalRStatus}
          {lastEvent && ` — last ETL: ${lastEvent.totalRowsProcessed} rows, ${lastEvent.durationMs} ms`}
        </span>
      </div>

      {error && <div className="error">⚠️ {error}</div>}

      <div className="kpi-row">
        <div className="kpi-card">
          <div className="kpi-label">Total Revenue</div>
          <div className="kpi-value">{totalRevenue.toLocaleString('vi-VN')} ₫</div>
        </div>
        <div className="kpi-card">
          <div className="kpi-label">Orders</div>
          <div className="kpi-value">{totalOrders.toLocaleString()}</div>
        </div>
        <div className="kpi-card">
          <div className="kpi-label">Categories</div>
          <div className="kpi-value">{byCategory.length}</div>
        </div>
      </div>

      <div className="chart-grid">
        <div className="chart-card wide">
          <h3>Sales by Day</h3>
          <SalesByDayChart data={byDay} />
        </div>
        <div className="chart-card">
          <h3>Sales by Category</h3>
          <SalesByCategoryChart data={byCategory} />
        </div>
        <div className="chart-card">
          <h3>Top 10 Products</h3>
          <TopProductsChart data={topProducts} />
        </div>
      </div>
    </div>
  )
}
