import { Navigate, Route, Routes } from 'react-router-dom'
import { LoginPage } from '../features/auth/LoginPage'
import { ProtectedRoute } from './ProtectedRoute'

/** Phase 2 replaces the placeholder dashboard with the family tree page. */
const Dashboard = () => <p>Dashboard</p>

export const AppRoutes = () => (
  <Routes>
    <Route path="/login" element={<LoginPage />} />
    <Route path="/" element={<ProtectedRoute><Dashboard /></ProtectedRoute>} />
    <Route path="*" element={<Navigate to="/" replace />} />
  </Routes>
)
