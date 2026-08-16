import { useDirection } from '../i18n/useDirection'
import { AppRoutes } from '../routes/AppRoutes'

/**
 * Chrome lives in AppShell, which each authenticated screen wraps itself in — the login screen
 * deliberately has none. App's only jobs are keeping <html dir> in step and rendering routes.
 */
export const App = () => {
  useDirection()

  return <AppRoutes />
}
