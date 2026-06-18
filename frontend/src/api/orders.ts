import { api } from './client'
import type {
  CreateOrderRequest,
  OrderCreatedResponse,
  EtlEnqueuedResponse,
  PagedResult,
  OrderListItem,
  OrderDetail,
} from '../types/api'

export interface OrderListQuery {
  page?: number
  pageSize?: number
  status?: number
  customerId?: number
  from?: string  // YYYY-MM-DD
  to?: string
  search?: string
}

export const ordersApi = {
  create: (req: CreateOrderRequest) =>
    api.post<OrderCreatedResponse>('/api/orders', req).then(r => r.data),

  list: (params: OrderListQuery = {}) =>
    api.get<PagedResult<OrderListItem>>('/api/orders', { params }).then(r => r.data),

  getById: (id: number) =>
    api.get<OrderDetail>(`/api/orders/${id}`).then(r => r.data),

  // Staff/Admin: advance the order along the fulfilment state machine.
  updateStatus: (id: number, status: number, reason?: string) =>
    api.patch<OrderDetail>(`/api/orders/${id}/status`, { status, reason }).then(r => r.data),

  // Customer: cancel own pending order (restocks items).
  cancel: (id: number) =>
    api.post<OrderDetail>(`/api/orders/${id}/cancel`).then(r => r.data),
}

export const adminApi = {
  triggerEtl: () =>
    api.post<EtlEnqueuedResponse>('/api/admin/trigger-etl').then(r => r.data),
  compressColumnstore: () =>
    api.post<EtlEnqueuedResponse>('/api/admin/compress-columnstore').then(r => r.data),
  reset: () =>
    api.post('/api/admin/reset').then(r => r.data),
}
