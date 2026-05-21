import axios, { AxiosError, CanceledError } from 'axios'

// Base axios instance.
// In dev, requests to /api/* are proxied by Vite to http://localhost:5193 (see vite.config.ts).
// In prod, point this to your deployed API origin via env var.
export const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL ?? '',
  headers: { 'Content-Type': 'application/json' },
})

/**
 * Helper for callers to detect "this was just an abort, don't show error UI".
 *
 *   try { await api.get(...) }
 *   catch (e) {
 *     if (isAbortError(e)) return
 *     ...
 *   }
 */
export function isAbortError(e: unknown): boolean {
  if (e instanceof CanceledError) return true
  const err = e as { name?: string; code?: string }
  return err?.name === 'CanceledError'
      || err?.name === 'AbortError'
      || err?.code === 'ERR_CANCELED'
}

/**
 * Treat backend 499 (client closed request) as a silent no-op the same way we
 * treat axios-side CanceledError. Anything else flows through normally.
 */
api.interceptors.response.use(
  r => r,
  (error: AxiosError) => {
    if (error.response?.status === 499) {
      // Backend acknowledged our cancel — swallow.
      return Promise.reject(new CanceledError('Request cancelled by client.'))
    }
    return Promise.reject(error)
  }
)
