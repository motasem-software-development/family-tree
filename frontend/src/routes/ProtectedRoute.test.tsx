import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { Route, Routes } from 'react-router-dom'
import { Providers } from '../app/providers'
import { tokenStorage } from '../services/tokenStorage'
import { ProtectedRoute } from './ProtectedRoute'

const jsonResponse = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const renderRoutes = () =>
  render(
    <Providers>
      <Routes>
        <Route path="/login" element={<p>login screen</p>} />
        <Route path="/" element={<ProtectedRoute><p>protected content</p></ProtectedRoute>} />
      </Routes>
    </Providers>,
  )

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
})
