import { ResponsiveContainer, BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip } from 'recharts'
import type { TopProductRow } from '../types/api'

interface Props { data: TopProductRow[] }

export function TopProductsChart({ data }: Props) {
  const formatted = data.map(d => ({
    name: d.name.length > 20 ? d.name.slice(0, 20) + '...' : d.name,
    revenueM: Math.round(d.totalRevenue / 1_000_000),
    sku: d.sku,
  }))

  return (
    <ResponsiveContainer width="100%" height={300}>
      <BarChart data={formatted} layout="vertical" margin={{ left: 60 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="#333" />
        <XAxis type="number" stroke="#999" />
        <YAxis type="category" dataKey="name" stroke="#999" width={140} fontSize={11} />
        <Tooltip
          contentStyle={{ background: '#1a1a1a', border: '1px solid #333' }}
          formatter={(v) => `${Number(v)} M ₫`}
        />
        <Bar dataKey="revenueM" fill="#4dabf7" />
      </BarChart>
    </ResponsiveContainer>
  )
}
