import { api } from './client'

export type UserRole = 'Customer' | 'Staff' | 'Admin'

export interface RegisterRequest {
  fullName: string
  email:    string
  password: string
  phone?:   string
  city?:    string
}

export interface LoginRequest {
  email:    string
  password: string
}

export interface AuthUser {
  id:       number
  fullName: string
  email:    string
  role:     UserRole | number   // backend serializes enum as number unless config'd otherwise
}

export interface AuthResponse {
  accessToken:           string
  refreshToken:          string
  accessTokenExpiresAt:  string   // ISO datetime
  user:                  AuthUser
}

export const authApi = {
  register: (req: RegisterRequest) =>
    api.post<AuthResponse>('/api/auth/register', req).then(r => r.data),

  login: (req: LoginRequest) =>
    api.post<AuthResponse>('/api/auth/login', req).then(r => r.data),

  refresh: (refreshToken: string) =>
    api.post<AuthResponse>('/api/auth/refresh', { refreshToken }).then(r => r.data),

  logout: (refreshToken: string) =>
    api.post('/api/auth/logout', { refreshToken }).then(r => r.data),

  me: () => api.get<AuthUser>('/api/auth/me').then(r => r.data),
}

/** Maps numeric role enum (backend) → string the frontend reads. */
export function roleName(role: AuthUser['role']): UserRole {
  if (typeof role === 'string') return role
  return role === 3 ? 'Admin' : role === 2 ? 'Staff' : 'Customer'
}
