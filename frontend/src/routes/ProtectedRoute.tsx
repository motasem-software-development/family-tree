import type { PropsWithChildren } from 'react'
import { Navigate } from 'react-router-dom'
import { useAuth } from '../features/auth/AuthContext'

export const ProtectedRoute = ({ children }: PropsWithChildren) => {
  const { user, isLoading } = useAuth()

  if (isLoading) return null
  if (!user) return <Navigate to="/login" replace />

  return <>{children}</>
}
