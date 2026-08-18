import { Navigate, Route, Routes } from 'react-router-dom'
import { LoginPage } from '../features/auth/LoginPage'
import { MembersPage } from '../features/members/MembersPage'
import { RolesPage } from '../features/roles/RolesPage'
import { TreePage } from '../features/tree/TreePage'
import { UsersPage } from '../features/users/UsersPage'
import { ProtectedRoute } from './ProtectedRoute'

/**
 * The tree is the product's home screen. The placeholder dashboard it replaces is gone; the
 * link into the members list now lives in the shell's nav, alongside every other destination.
 */
export const AppRoutes = () => (
  <Routes>
    <Route path="/login" element={<LoginPage />} />
    <Route
      path="/"
      element={
        <ProtectedRoute>
          <TreePage />
        </ProtectedRoute>
      }
    />
    <Route
      path="/members"
      element={
        <ProtectedRoute>
          <MembersPage />
        </ProtectedRoute>
      }
    />
    <Route
      path="/users"
      element={
        <ProtectedRoute>
          <UsersPage />
        </ProtectedRoute>
      }
    />
    <Route
      path="/roles"
      element={
        <ProtectedRoute>
          <RolesPage />
        </ProtectedRoute>
      }
    />
    <Route path="*" element={<Navigate to="/" replace />} />
  </Routes>
)
