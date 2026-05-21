import { api } from './client'
import type { SalesByCategoryRow, SalesByDayRow, TopProductRow } from '../types/api'

export interface DateRange {
  from: string // YYYY-MM-DD
  to: string   // YYYY-MM-DD
}

export const reportsApi = {
  salesByCategory: (range: DateRange, signal?: AbortSignal) =>
    api.get<SalesByCategoryRow[]>('/api/reports/sales-by-category', { params: range, signal })
       .then(r => r.data),

  salesByDay: (range: DateRange, signal?: AbortSignal) =>
    api.get<SalesByDayRow[]>('/api/reports/sales-by-day', { params: range, signal })
       .then(r => r.data),

  topProducts: (range: DateRange, top = 10, signal?: AbortSignal) =>
    api.get<TopProductRow[]>('/api/reports/top-products', { params: { ...range, top }, signal })
       .then(r => r.data),
}
