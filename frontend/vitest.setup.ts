import '@testing-library/jest-dom/vitest'
import { afterEach } from 'vitest'
import { queryClient } from './src/app/providers'

// BrowserRouter drives the real jsdom History API, which — unlike component state —
// is not reset between tests by Testing Library's automatic cleanup. Without this,
// a redirect performed by one test (e.g. <Navigate to="/login" />) leaks into the
// next test's initial URL.
//
// Likewise, the app's TanStack Query cache is a module-scoped singleton shared by
// every test that imports `Providers` within a file, so a query resolved in one
// test would otherwise serve stale cached data to the next.
afterEach(() => {
  window.history.pushState({}, '', '/')
  queryClient.clear()
})
