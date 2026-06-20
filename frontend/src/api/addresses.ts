import { api } from './client'

export interface Address {
  id: number
  fullName: string
  phone: string
  address: string
  isDefault: boolean
}

export interface SaveAddressRequest {
  fullName: string
  phone: string
  address: string
  isDefault?: boolean
}

export const addressesApi = {
  list: () => api.get<Address[]>('/api/addresses').then(r => r.data),
  create: (req: SaveAddressRequest) => api.post<Address>('/api/addresses', req).then(r => r.data),
  update: (id: number, req: SaveAddressRequest) => api.put<Address>(`/api/addresses/${id}`, req).then(r => r.data),
  remove: (id: number) => api.delete(`/api/addresses/${id}`).then(r => r.data),
}
