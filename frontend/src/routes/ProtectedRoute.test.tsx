import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { Providers } from '../app/providers'
import { AuthProvider, useAuth } from '../features/auth/AuthContext'
import { apiFetch } from '../services/apiClient'
import { tokenStorage } from '../services/tokenStorage'
import { ProtectedRoute } from './ProtectedRoute'

const jsonResponse = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const meResponse = (mustChangePassword: boolean) =>
  jsonResponse({
    id: '0193...',
    email: 'admin@example.com',
    tenantId: '0193...',
    familyTreeName: 'عائلة السقا',
    permissions: ['FamilyTree.View'],
    mustChangePassword,
  })

const renderRoutes = () =>
  render(
    <Providers>
      <Routes>
        <Route path="/login" element={<p>login screen</p>} />
        <Route path="/" element={<ProtectedRoute><p>protected content</p></ProtectedRoute>} />
      </Routes>
    </Providers>,
  )

// A stand-in for the shared sign-out control that both the change-password gate and every
// app-shell screen expose — calling the same context logout() that they call.
const SignOutButton = () => {
  const { logout } = useAuth()
  return (
    <button type="button" onClick={() => void logout()}>
      sign out
    </button>
  )
}

// Any ordinary in-app request. Its failure must be able to end the session the same way the
// sign-out button does — see the refresh test below.
const LoadButton = () => (
  <button
    type="button"
    onClick={() => {
      void apiFetch('/api/v1/family-members').catch(() => undefined)
    }}
  >
    load members
  </button>
)

const renderAt = (path: string) => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[path]}>
        <AuthProvider>
          <Routes>
            <Route path="/login" element={<p>login screen</p>} />
            <Route
              path="/change-password"
              element={
                <ProtectedRoute>
                  <SignOutButton />
                </ProtectedRoute>
              }
            />
            <Route
              path="/members"
              element={
                <ProtectedRoute>
                  <SignOutButton />
                  <LoadButton />
                </ProtectedRoute>
              }
            />
          </Routes>
        </AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('ProtectedRoute', () => {
  beforeEach(() => tokenStorage.clear())
  afterEach(() => vi.restoreAllMocks())

  it('redirects to the login screen when no token is stored', async () => {
    renderRoutes()

    expect(await screen.findByText('login screen')).toBeInTheDocument()
  })

  it('renders the protected content once the session resolves', async () => {
    tokenStorage.write({ accessToken: 'abc', refreshToken: 'def' })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      jsonResponse({
        id: '0193...', email: 'admin@example.com', tenantId: '0193...',
        familyTreeName: 'عائلة السقا', permissions: ['FamilyTree.View'],
      }),
    ))

    renderRoutes()

    await waitFor(() => expect(screen.getByText('protected content')).toBeInTheDocument())
  })

  it('redirects to login when the stored token is rejected', async () => {
    tokenStorage.write({ accessToken: 'expired', refreshToken: 'stale' })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ code: 'UNAUTHORIZED' }, 401)))

    renderRoutes()

    expect(await screen.findByText('login screen')).toBeInTheDocument()
  })

  it('lands on the login screen after signing out from the change-password gate', async () => {
    tokenStorage.write({ accessToken: 'abc', refreshToken: 'def' })
    vi.stubGlobal(
      'fetch',
      vi.fn().mockImplementation((path: string) => {
        if (path === '/api/v1/me') return Promise.resolve(meResponse(true))
        if (path === '/api/v1/auth/logout') return Promise.resolve(new Response(null, { status: 204 }))
        throw new Error(`unexpected fetch: ${path}`)
      }),
    )

    renderAt('/change-password')

    await userEvent.click(await screen.findByRole('button', { name: 'sign out' }))

    // The defect: tokens clear but the gate screen keeps rendering because nothing tells the
    // 'me' query observer, and its stale user keeps ProtectedRoute from redirecting.
    expect(await screen.findByText('login screen')).toBeInTheDocument()
  })

  it('lands on the login screen when a token refresh fails mid-session', async () => {
    // An administrator deactivates a colleague who has the app open; every path that revokes
    // refresh tokens (deactivation, an administrator password reset, the user's own password
    // change) produces this. Without a session-ended path the tokens clear and nothing else
    // happens: `user` stays non-null from the cached ['me'] data, ProtectedRoute never
    // redirects, and the colleague keeps looking at a fully rendered app whose every request
    // fails — no message, no redirect, no sign-out.
    tokenStorage.write({ accessToken: 'abc', refreshToken: 'def' })
    vi.stubGlobal(
      'fetch',
      vi.fn().mockImplementation((path: string) => {
        if (path === '/api/v1/me') return Promise.resolve(meResponse(false))
        if (path === '/api/v1/family-members') {
          return Promise.resolve(jsonResponse({ code: 'UNAUTHORIZED' }, 401))
        }
        if (path === '/api/v1/auth/refresh') {
          return Promise.resolve(jsonResponse({ code: 'INVALID_REFRESH_TOKEN' }, 401))
        }
        throw new Error(`unexpected fetch: ${path}`)
      }),
    )

    renderAt('/members')

    // The session must be fully rendered first: a cold-start 401 already redirects today, so
    // starting from an unresolved session would prove nothing.
    await userEvent.click(await screen.findByRole('button', { name: 'load members' }))

    expect(await screen.findByText('login screen')).toBeInTheDocument()
    expect(tokenStorage.read()).toBeNull()
  })

  it('lands on the login screen after signing out from an app-shell screen', async () => {
    tokenStorage.write({ accessToken: 'abc', refreshToken: 'def' })
    vi.stubGlobal(
      'fetch',
      vi.fn().mockImplementation((path: string) => {
        if (path === '/api/v1/me') return Promise.resolve(meResponse(false))
        if (path === '/api/v1/auth/logout') return Promise.resolve(new Response(null, { status: 204 }))
        throw new Error(`unexpected fetch: ${path}`)
      }),
    )

    renderAt('/members')

    await userEvent.click(await screen.findByRole('button', { name: 'sign out' }))

    expect(await screen.findByText('login screen')).toBeInTheDocument()
  })
})
