import { Link, Navigate, Route, Routes } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { LoginPage } from '../features/auth/LoginPage'
import { MembersPage } from '../features/members/MembersPage'
import { ProtectedRoute } from './ProtectedRoute'

/**
 * Minimal home view — a heading and a link into the members screen, the phase's headline
 * deliverable. A nav shell / layout component is Phase 3's business, not this one's.
 */
export const HomePage = () => {
  const { t } = useTranslation()

  return (
    <section>
      <h1>{t('app.title')}</h1>
      <Link to="/members">{t('members.title')}</Link>
    </section>
  )
}

export const AppRoutes = () => (
  <Routes>
    <Route path="/login" element={<LoginPage />} />
    <Route path="/" element={<ProtectedRoute><HomePage /></ProtectedRoute>} />
    <Route path="/members" element={<ProtectedRoute><MembersPage /></ProtectedRoute>} />
    <Route path="*" element={<Navigate to="/" replace />} />
  </Routes>
)
