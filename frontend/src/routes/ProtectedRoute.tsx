import type { PropsWithChildren } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import { useAuth } from '../features/auth/AuthContext'

export const ProtectedRoute = ({ children }: PropsWithChildren) => {
  const { user, isLoading, mustChangePassword } = useAuth()
  const location = useLocation()

  if (isLoading) return null
  if (!user) return <Navigate to="/login" replace />

  // UX only, not the enforcement point (§9): the server gate (PasswordChangeGateMiddleware)
  // rejects every route but GET /me and POST /me/password based on the JWT claim regardless
  // of what the client renders. This redirect exists so the user sees the right screen
  // instead of a wall of 403s.
  if (mustChangePassword && location.pathname !== '/change-password') {
    return <Navigate to="/change-password" replace />
  }

  return <>{children}</>
}
