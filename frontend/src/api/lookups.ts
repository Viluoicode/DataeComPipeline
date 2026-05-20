import { api } from './client'
import type { PagedResult, CustomerLookup, ProductLookup } from '../types/api'

export const customersApi = {
  search: (search?: string, page = 1, pageSize = 50) =>
    api.get<PagedResult<CustomerLookup>>('/api/customers', {
      params: { search, page, pageSize },
    }).then(r => r.data),
}

export const productsApi = {
  search: (search?: string, category?: string, page = 1, pageSize = 50) =>
    api.get<PagedResult<ProductLookup>>('/api/products', {
      params: { search, category, page, pageSize },
    }).then(r => r.data),

  categories: () =>
    api.get<string[]>('/api/products/categories').then(r => r.data),
}
