import { api } from './client'
import type { SalesByCategoryRow, SalesByDayRow, TopProductRow } from '../types/api'

export interface DateRange {
  from: string // YYYY-MM-DD
  to: string   // YYYY-MM-DD
}

export const reportsApi = {
  salesByCategory: (range: DateRange) =>
    api.get<SalesByCategoryRow[]>('/api/reports/sales-by-category', { params: range }).then(r => r.data),

  salesByDay: (range: DateRange) =>
    api.get<SalesByDayRow[]>('/api/reports/sales-by-day', { params: range }).then(r => r.data),

  topProducts: (range: DateRange, top = 10) =>
    api.get<TopProductRow[]>('/api/reports/top-products', { params: { ...range, top } }).then(r => r.data),
}
