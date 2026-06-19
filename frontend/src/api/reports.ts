import { api } from './client'
import type {
  SalesByCategoryRow, SalesByDayRow, TopProductRow,
  PaymentMethodSalesRow, OrderFunnelRow, ProductInventoryRow,
} from '../types/api'

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

  // Phase 4 — current-state analytics (not date-filtered)
  salesByPaymentMethod: (signal?: AbortSignal) =>
    api.get<PaymentMethodSalesRow[]>('/api/reports/sales-by-payment-method', { signal })
       .then(r => r.data),

  orderFunnel: (signal?: AbortSignal) =>
    api.get<OrderFunnelRow[]>('/api/reports/order-funnel', { signal }).then(r => r.data),

  lowStock: (limit = 50, signal?: AbortSignal) =>
    api.get<ProductInventoryRow[]>('/api/reports/low-stock', { params: { limit }, signal })
       .then(r => r.data),
}
