import { api } from './client'
import type { CreateOrderRequest, OrderCreatedResponse, EtlEnqueuedResponse } from '../types/api'

export const ordersApi = {
  create: (req: CreateOrderRequest) =>
    api.post<OrderCreatedResponse>('/api/orders', req).then(r => r.data),
}

export const adminApi = {
  triggerEtl: () =>
    api.post<EtlEnqueuedResponse>('/api/admin/trigger-etl').then(r => r.data),
  compressColumnstore: () =>
    api.post<EtlEnqueuedResponse>('/api/admin/compress-columnstore').then(r => r.data),
  reset: () =>
    api.post('/api/admin/reset').then(r => r.data),
}
