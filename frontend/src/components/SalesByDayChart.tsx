import { ResponsiveContainer, LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, Legend } from 'recharts'
import type { SalesByDayRow } from '../types/api'

interface Props { data: SalesByDayRow[] }

export function SalesByDayChart({ data }: Props) {
  const formatted = data.map(d => ({
    ...d,
    dayLabel: new Date(d.day).toLocaleDateString('vi-VN', { day: '2-digit', month: '2-digit' }),
    revenueM: Math.round(d.totalRevenue / 1_000_000), // VND -> millions
  }))

  return (
    <ResponsiveContainer width="100%" height={300}>
      <LineChart data={formatted}>
        <CartesianGrid strokeDasharray="3 3" stroke="#333" />
        <XAxis dataKey="dayLabel" stroke="#999" />
        <YAxis yAxisId="left" stroke="#4dabf7" label={{ value: 'Revenue (M ₫)', angle: -90, position: 'insideLeft', fill: '#4dabf7' }} />
        <YAxis yAxisId="right" orientation="right" stroke="#82ca9d" label={{ value: 'Orders', angle: 90, position: 'insideRight', fill: '#82ca9d' }} />
        <Tooltip contentStyle={{ background: '#1a1a1a', border: '1px solid #333' }} />
        <Legend />
        <Line yAxisId="left"  type="monotone" dataKey="revenueM"   name="Revenue (M ₫)" stroke="#4dabf7" strokeWidth={2} dot={false} />
        <Line yAxisId="right" type="monotone" dataKey="orderCount" name="Orders"        stroke="#82ca9d" strokeWidth={2} dot={false} />
      </LineChart>
    </ResponsiveContainer>
  )
}
