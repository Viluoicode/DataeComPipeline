import { useEffect, useState, useCallback } from 'react'
import {
  Card,
  Metric,
  Text,
  Grid,
  Flex,
  Badge,
  Title,
  AreaChart,
  BarList,
  DonutChart,
  Button,
} from '@tremor/react'
import { reportsApi, type DateRange } from '../api/reports'
import { isAbortError } from '../api/client'
import type { SalesByCategoryRow, SalesByDayRow, TopProductRow } from '../types/api'
import { useEtlNotifications } from '../hooks/useEtlNotifications'
import { DateInput } from '../components/DateInput'

function defaultRange(): DateRange {
  const to = new Date()
  const from = new Date()
  from.setDate(from.getDate() - 90)
  return {
    from: from.toISOString().slice(0, 10),
    to:   to.toISOString().slice(0, 10),
  }
}

function formatVnd(n: number): string {
  return n.toLocaleString('vi-VN', { maximumFractionDigits: 0 }) + ' ₫'
}

export function Dashboard() {
  const [range, setRange] = useState<DateRange>(defaultRange())
  const [byDay, setByDay] = useState<SalesByDayRow[]>([])
  const [byCategory, setByCategory] = useState<SalesByCategoryRow[]>([])
  const [topProducts, setTopProducts] = useState<TopProductRow[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async (r: DateRange, signal?: AbortSignal) => {
    setLoading(true)
    setError(null)
    try {
      const [d, c, p] = await Promise.all([
        reportsApi.salesByDay(r, signal),
        reportsApi.salesByCategory(r, signal),
        reportsApi.topProducts(r, 10, signal),
      ])
      setByDay(d)
      setByCategory(c)
      setTopProducts(p)
    } catch (e: unknown) {
      // Swallow abort errors — they mean the user moved on (filter changed
      // or component unmounted) and we don't want to flash a fake error.
      if (isAbortError(e)) return
      const err = e as { message?: string }
      setError(err?.message ?? 'Failed to load reports')
    } finally {
      setLoading(false)
    }
  }, [])

  // Cancel previous request when range changes or component unmounts.
  useEffect(() => {
    const ctrl = new AbortController()
    load(range, ctrl.signal)
    return () => ctrl.abort()
  }, [load, range])

  const { status: signalRStatus, lastEvent } = useEtlNotifications(
    useCallback(() => { load(range) }, [load, range])
  )

  // Unique-order counts can be summed across categories (will double-count orders that
  // span multiple categories — acceptable approximation for the KPI tile).
  const totalRevenue = byCategory.reduce((s, r) => s + r.totalRevenue, 0)
  const totalOrders  = byCategory.reduce((s, r) => s + r.orderCount, 0)

  const chartData = byDay.map(d => ({
    date: new Date(d.day).toLocaleDateString('vi-VN', { day: '2-digit', month: '2-digit' }),
    Revenue: Math.round(d.totalRevenue / 1_000_000),
    Orders: d.orderCount,
  }))

  const topList = topProducts.map(p => ({ name: p.name, value: p.totalRevenue }))

  const donutData = byCategory.map(c => ({
    name: c.category,
    sales: c.totalRevenue,
  }))

  const badgeColor =
    signalRStatus === 'connected'    ? 'emerald' :
    signalRStatus === 'reconnecting' ? 'amber'   :
    signalRStatus === 'connecting'   ? 'blue'    : 'rose'

  return (
    <div className="p-6 space-y-6">
      <Flex justifyContent="between" alignItems="center">
        <div>
          <Title className="!text-2xl">Dashboard</Title>
          <Text>Real-time analytics from OLAP (Columnstore) — last 90 days</Text>
        </div>
        <Badge color={badgeColor}>
          ● SignalR: {signalRStatus}
          {lastEvent && ` • last ETL ${lastEvent.totalRowsProcessed} rows in ${lastEvent.durationMs}ms`}
        </Badge>
      </Flex>

      <Card>
        <Flex justifyContent="start" className="gap-4 flex-wrap">
          <div>
            <Text>From</Text>
            <DateInput value={range.from} onChange={v => setRange({ ...range, from: v })} />
          </div>
          <div>
            <Text>To</Text>
            <DateInput value={range.to} onChange={v => setRange({ ...range, to: v })} />
          </div>
          <div className="self-end">
            <Button onClick={() => load(range)} loading={loading}>
              Refresh
            </Button>
          </div>
        </Flex>
      </Card>

      {error && (
        <Card decoration="left" decorationColor="rose">
          <Text color="rose">⚠️ {error}</Text>
        </Card>
      )}

      <Grid numItemsMd={3} className="gap-6">
        <Card decoration="top" decorationColor="blue">
          <Text>Total Revenue (90d)</Text>
          <Metric>{formatVnd(totalRevenue)}</Metric>
        </Card>
        <Card decoration="top" decorationColor="indigo">
          <Text>Total Orders (sum across categories)</Text>
          <Metric>{totalOrders.toLocaleString('en-US')}</Metric>
        </Card>
        <Card decoration="top" decorationColor="emerald">
          <Text>Active Categories</Text>
          <Metric>{byCategory.length}</Metric>
        </Card>
      </Grid>

      <Card>
        <Title>Sales by Day</Title>
        <Text>Revenue (M VND) and order count over time</Text>
        <AreaChart
          className="h-72 mt-4"
          data={chartData}
          index="date"
          categories={['Revenue', 'Orders']}
          colors={['blue', 'emerald']}
          valueFormatter={n => n.toLocaleString('en-US')}
          showLegend
          showGridLines
          yAxisWidth={60}
        />
      </Card>

      <Grid numItemsMd={2} className="gap-6">
        <Card>
          <Title>Sales by Category</Title>
          <Text>Revenue split across categories</Text>
          <DonutChart
            className="h-60 mt-4"
            data={donutData}
            category="sales"
            index="name"
            valueFormatter={formatVnd}
            colors={['blue', 'indigo', 'cyan', 'emerald', 'amber', 'rose', 'violet', 'fuchsia']}
          />
        </Card>
        <Card>
          <Title>Top 10 Products by Revenue</Title>
          <Text>Bestsellers in selected range</Text>
          <BarList
            className="mt-4"
            data={topList}
            valueFormatter={formatVnd}
          />
        </Card>
      </Grid>
    </div>
  )
}
