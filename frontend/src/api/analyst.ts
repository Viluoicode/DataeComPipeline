import { api } from './client'

export interface AnalystResult {
  status: 'Answered' | 'Refused' | string
  generatedSql?: string | null
  executedSql?: string | null
  columns?: string[] | null
  rows?: (string | number | null)[][] | null
  rowCount?: number
  summary?: string | null
  referencedTables?: string[] | null
  refusalReasons?: string[] | null
}

export const analystApi = {
  ask: (question: string, includeSummary = true) =>
    api.post<AnalystResult>('/api/ask', { question, includeSummary }).then(r => r.data),
}
