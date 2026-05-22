import axios, { AxiosError, CanceledError, type InternalAxiosRequestConfig } from 'axios'
import { getStoredAuth, setStoredAuth } from '../contexts/AuthContext'

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

// ============================================================================
//  Request interceptor — inject Bearer header on every authenticated request.
// ============================================================================
api.interceptors.request.use((config) => {
  const session = getStoredAuth()
  if (session?.accessToken) {
    config.headers = config.headers ?? {}
    config.headers.Authorization = `Bearer ${session.accessToken}`
  }
  return config
})

// ============================================================================
//  Response interceptor — auto-refresh on 401, plus cancellation handling.
// ============================================================================
let refreshPromise: Promise<string | null> | null = null

async function tryRefresh(): Promise<string | null> {
  const session = getStoredAuth()
  if (!session?.refreshToken) return null

  try {
    const r = await axios.post(
      (import.meta.env.VITE_API_URL ?? '') + '/api/auth/refresh',
      { refreshToken: session.refreshToken },
      { headers: { 'Content-Type': 'application/json' } }
    )
    const fresh = r.data
    setStoredAuth({
      accessToken:          fresh.accessToken,
      refreshToken:         fresh.refreshToken,
      accessTokenExpiresAt: fresh.accessTokenExpiresAt,
      user:                 fresh.user,
    })
    // Notify other tabs / components — they'll pick up via storage event or remount
    window.dispatchEvent(new Event('auth:refreshed'))
    return fresh.accessToken
  } catch {
    // Refresh failed — wipe session, caller will see 401 propagate
    setStoredAuth(null)
    window.dispatchEvent(new Event('auth:expired'))
    return null
  }
}

api.interceptors.response.use(
  r => r,
  async (error: AxiosError) => {
    // Backend says client cancelled — treat as CanceledError
    if (error.response?.status === 499) {
      return Promise.reject(new CanceledError('Request cancelled by client.'))
    }

    // 401 → try refresh exactly once, then retry the original request
    const original = error.config as InternalAxiosRequestConfig & { _retry?: boolean }
    const isAuthEndpoint = original?.url?.includes('/api/auth/')

    if (error.response?.status === 401 && !original?._retry && !isAuthEndpoint) {
      original._retry = true

      // Coalesce concurrent 401s into a single refresh call
      refreshPromise ??= tryRefresh().finally(() => { refreshPromise = null })
      const newToken = await refreshPromise

      if (newToken) {
        original.headers = original.headers ?? {}
        original.headers.Authorization = `Bearer ${newToken}`
        return api.request(original)
      }
    }

    return Promise.reject(error)
  }
)
