import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

export default function ProtectedRoute() {
  const { isAuthenticated, isLoading, initializationError } = useAuth()
  const location = useLocation()

  if (isLoading) return <p role="status">Checking session…</p>
  if (initializationError) return <p role="alert">{initializationError}</p>
  if (!isAuthenticated) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }

  return <Outlet />
}
