import { Navigate, useLocation, Outlet } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'

interface Props {
  /** When set, only users with this role (or higher) can access. */
  requireRole?: 'Customer' | 'Staff' | 'Admin'
}

/**
 * Guards a route subtree. Unauthenticated users are sent to /login with
 * the original path stashed in router state so they bounce back after login.
 * Admin/Staff-only routes redirect to / with a toast.
 */
export function ProtectedRoute({ requireRole }: Props) {
  const { isAuthenticated, role } = useAuth()
  const loc = useLocation()

  if (!isAuthenticated) {
    return <Navigate to="/login" state={{ from: loc.pathname + loc.search }} replace />
  }

  if (requireRole && requireRole !== 'Customer') {
    const hasAccess = requireRole === 'Staff'
      ? role === 'Staff' || role === 'Admin'
      : role === 'Admin'
    if (!hasAccess) {
      return <Navigate to="/" replace />
    }
  }

  return <Outlet />
}
