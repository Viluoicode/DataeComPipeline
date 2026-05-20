import { api } from './client'
import type { ImportResult } from '../types/api'

export type ImportKind = 'customers' | 'products' | 'orders'

export const importApi = {
  upload: (kind: ImportKind, file: File) => {
    const form = new FormData()
    form.append('file', file)
    return api.post<ImportResult>(`/api/import/${kind}`, form, {
      headers: { 'Content-Type': 'multipart/form-data' },
    }).then(r => r.data)
  },

  downloadTemplate: async (kind: ImportKind) => {
    const r = await api.get(`/api/import/template/${kind}`, { responseType: 'blob' })
    const blob = new Blob([r.data], {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    })
    const url = window.URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${kind}-template.xlsx`
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    window.URL.revokeObjectURL(url)
  },
}
