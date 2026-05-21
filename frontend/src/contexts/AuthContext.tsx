import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'

/**
 * Mock auth — no real JWT, no backend validation. Stores a tiny "session"
 * blob in localStorage so the storefront can show a logged-in name and the
 * /my-orders page can filter by customerId.
 *
 * Real auth comes in v2 (JWT + roles). This is just to flesh out the UX.
 */
export interface AuthUser {
  customerId: number
  fullName: string
  email: string
}

interface AuthContextValue {
  user: AuthUser | null
  login: (user: AuthUser) => void
  logout: () => void
}

const AuthContext = createContext<AuthContextValue | null>(null)
const STORAGE_KEY = 'ecom.auth'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(() => {
    try {
      const raw = localStorage.getItem(STORAGE_KEY)
      return raw ? JSON.parse(raw) as AuthUser : null
    } catch { return null }
  })

  useEffect(() => {
    if (user) localStorage.setItem(STORAGE_KEY, JSON.stringify(user))
    else localStorage.removeItem(STORAGE_KEY)
  }, [user])

  return (
    <AuthContext.Provider value={{ user, login: setUser, logout: () => setUser(null) }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}
