import { ResponsiveContainer, PieChart, Pie, Cell, Tooltip, Legend } from 'recharts'
import type { SalesByCategoryRow } from '../types/api'

interface Props { data: SalesByCategoryRow[] }

const COLORS = ['#4dabf7', '#82ca9d', '#ffc658', '#ff8042', '#a78bfa', '#f87171', '#34d399', '#fb923c']

export function SalesByCategoryChart({ data }: Props) {
  return (
    <ResponsiveContainer width="100%" height={300}>
      <PieChart>
        <Pie
          data={data}
          dataKey="totalRevenue"
          nameKey="category"
          cx="50%"
          cy="50%"
          outerRadius={100}
          label={(props: { name?: string; percent?: number }) =>
            `${props.name ?? ''} ${((props.percent ?? 0) * 100).toFixed(0)}%`
          }>
          {data.map((_, i) => (
            <Cell key={i} fill={COLORS[i % COLORS.length]} />
          ))}
        </Pie>
        <Tooltip
          contentStyle={{ background: '#1a1a1a', border: '1px solid #333' }}
          formatter={(v) => `${Number(v).toLocaleString('vi-VN')} ₫`}
        />
        <Legend />
      </PieChart>
    </ResponsiveContainer>
  )
}
