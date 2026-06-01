import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { authApi, type AuthResponse, type AuthUser, roleName, type UserRole } from '../api/auth'

/**
 * Real JWT auth — stores access + refresh tokens + user profile in localStorage.
 * Pairs with the axios interceptor in api/client.ts that injects the Bearer header
 * and auto-refreshes on 401.
 *
 * Session shape:
 *   { accessToken, refreshToken, accessTokenExpiresAt (ISO), user }
 */
const STORAGE_KEY = 'ecom.auth'

export interface AuthSession {
  accessToken:          string
  refreshToken:         string
  accessTokenExpiresAt: string
  user:                 AuthUser
}

interface AuthContextValue {
  session: AuthSession | null
  user:    AuthUser | null
  role:    UserRole
  isAuthenticated: boolean
  isAdmin: boolean

  login:    (email: string, password: string) => Promise<void>
  register: (req: { fullName: string; email: string; password: string; phone?: string; city?: string }) => Promise<void>
  logout:   () => Promise<void>

  /** Used by the axios interceptor to refresh + update session in place. */
  applyTokens: (auth: AuthResponse) => void
  clear:       () => void
}

const AuthContext = createContext<AuthContextValue | null>(null)

function loadSession(): AuthSession | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return null
    const parsed = JSON.parse(raw)
    // Validate shape — guards against stale data from the old mock-auth format
    // (which stored { customerId, fullName, email } without accessToken/user).
    // A malformed blob would otherwise crash on session.user.role.
    if (!parsed?.accessToken || !parsed?.user || typeof parsed.user.role === 'undefined') {
      localStorage.removeItem(STORAGE_KEY)
      return null
    }
    return parsed as AuthSession
  } catch {
    localStorage.removeItem(STORAGE_KEY)
    return null
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<AuthSession | null>(loadSession)

  useEffect(() => {
    if (session) localStorage.setItem(STORAGE_KEY, JSON.stringify(session))
    else localStorage.removeItem(STORAGE_KEY)
  }, [session])

  const applyTokens = (auth: AuthResponse) => {
    setSession({
      accessToken:          auth.accessToken,
      refreshToken:         auth.refreshToken,
      accessTokenExpiresAt: auth.accessTokenExpiresAt,
      user:                 auth.user,
    })
  }

  const login = async (email: string, password: string) => {
    applyTokens(await authApi.login({ email, password }))
  }

  const register = async (req: Parameters<AuthContextValue['register']>[0]) => {
    applyTokens(await authApi.register(req))
  }

  const logout = async () => {
    if (session?.refreshToken) {
      try { await authApi.logout(session.refreshToken) }
      catch { /* server-side revoke best-effort; clear local anyway */ }
    }
    setSession(null)
  }

  const role = session ? roleName(session.user.role) : 'Customer'

  return (
    <AuthContext.Provider value={{
      session,
      user: session?.user ?? null,
      role,
      isAuthenticated: !!session,
      isAdmin:         session != null && (role === 'Admin' || role === 'Staff'),
      login,
      register,
      logout,
      applyTokens,
      clear: () => setSession(null),
    }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}

/** Used by axios interceptor (lives outside React tree). */
export function getStoredAuth(): AuthSession | null {
  return loadSession()
}

export function setStoredAuth(s: AuthSession | null) {
  if (s) localStorage.setItem(STORAGE_KEY, JSON.stringify(s))
  else   localStorage.removeItem(STORAGE_KEY)
}
