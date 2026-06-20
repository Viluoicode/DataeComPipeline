import { api } from './client'
import type { PagedResult, CustomerLookup, ProductLookup } from '../types/api'

export const customersApi = {
  search: (search?: string, page = 1, pageSize = 50) =>
    api.get<PagedResult<CustomerLookup>>('/api/customers', {
      params: { search, page, pageSize },
    }).then(r => r.data),
}

export interface CreateProductRequest {
  sku: string
  name: string
  category: string
  brand?: string
  price: number
  stockQuantity: number
}

export type UpdateProductRequest = Omit<CreateProductRequest, 'sku'>

export const productsApi = {
  search: (search?: string, category?: string, page = 1, pageSize = 50) =>
    api.get<PagedResult<ProductLookup>>('/api/products', {
      params: { search, category, page, pageSize },
    }).then(r => r.data),

  categories: () =>
    api.get<string[]>('/api/products/categories').then(r => r.data),

  // ---- Phase 8: admin catalog management ----
  create: (req: CreateProductRequest) =>
    api.post<ProductLookup>('/api/products', req).then(r => r.data),

  update: (id: number, req: UpdateProductRequest) =>
    api.put<ProductLookup>(`/api/products/${id}`, req).then(r => r.data),

  remove: (id: number) =>
    api.delete(`/api/products/${id}`).then(r => r.data),
}
