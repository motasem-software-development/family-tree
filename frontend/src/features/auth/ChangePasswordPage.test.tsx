import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { Providers } from '../../app/providers'
import i18n from '../../i18n'
import { tokenStorage } from '../../services/tokenStorage'
import { ChangePasswordPage } from './ChangePasswordPage'

const jsonResponse = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const meResponse = () =>
  jsonResponse({
    id: '0193...',
    email: 'pending@example.com',
    tenantId: '0193...',
    familyTreeName: 'عائلة السقا',
    permissions: [],
    mustChangePassword: true,
  })

const renderPage = () => render(<Providers><ChangePasswordPage /></Providers>)

describe('ChangePasswordPage', () => {
  beforeEach(async () => {
    tokenStorage.write({ accessToken: 'abc', refreshToken: 'def' })
    await i18n.changeLanguage('en')
  })

  afterEach(() => {
    tokenStorage.clear()
    vi.restoreAllMocks()
  })

  it('rejects a mismatched confirmation without calling the API', async () => {
    const fetchMock = vi.fn().mockResolvedValue(meResponse())
    vi.stubGlobal('fetch', fetchMock)

    renderPage()

    await userEvent.type(screen.getByLabelText('Current password'), 'OldPassw0rd!')
    await userEvent.type(screen.getByLabelText('New password'), 'NewPassw0rd!')
    await userEvent.type(screen.getByLabelText('Confirm password'), 'Different0rd!')
    await userEvent.click(screen.getByRole('button', { name: 'Change password' }))

    expect(await screen.findByText('The passwords do not match.')).toBeInTheDocument()
    expect(fetchMock).not.toHaveBeenCalledWith(
      '/api/v1/me/password',
      expect.anything(),
    )
  })

  it('submits the change with the current and new password', async () => {
    const fetchMock = vi.fn().mockImplementation((path: string) => {
      if (path === '/api/v1/me') return Promise.resolve(meResponse())
      if (path === '/api/v1/me/password') return Promise.resolve(new Response(null, { status: 204 }))
      if (path === '/api/v1/auth/login') {
        return Promise.resolve(
          jsonResponse({ accessToken: 'new-access', refreshToken: 'new-refresh' }),
        )
      }
      throw new Error(`unexpected fetch: ${path}`)
    })
    vi.stubGlobal('fetch', fetchMock)

    renderPage()

    await userEvent.type(screen.getByLabelText('Current password'), 'OldPassw0rd!')
    await userEvent.type(screen.getByLabelText('New password'), 'NewPassw0rd!')
    await userEvent.type(screen.getByLabelText('Confirm password'), 'NewPassw0rd!')
    await userEvent.click(screen.getByRole('button', { name: 'Change password' }))

    await waitFor(() =>
      expect(fetchMock).toHaveBeenCalledWith(
        '/api/v1/me/password',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ currentPassword: 'OldPassw0rd!', newPassword: 'NewPassw0rd!' }),
        }),
      ),
    )
  })

  it('shows a translated failure when the current password is wrong', async () => {
    const fetchMock = vi.fn().mockImplementation((path: string) => {
      if (path === '/api/v1/me') return Promise.resolve(meResponse())
      if (path === '/api/v1/me/password') return Promise.resolve(jsonResponse({ code: 'PASSWORD_INCORRECT' }, 400))
      throw new Error(`unexpected fetch: ${path}`)
    })
    vi.stubGlobal('fetch', fetchMock)

    renderPage()

    await userEvent.type(screen.getByLabelText('Current password'), 'WrongPassw0rd!')
    await userEvent.type(screen.getByLabelText('New password'), 'NewPassw0rd!')
    await userEvent.type(screen.getByLabelText('Confirm password'), 'NewPassw0rd!')
    await userEvent.click(screen.getByRole('button', { name: 'Change password' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('The current password is incorrect.')
  })

  it('logs in with the new password only after the change request succeeds', async () => {
    const calls: Array<{ path: string; method: string }> = []
    const fetchMock = vi.fn().mockImplementation((path: string, init?: RequestInit) => {
      calls.push({ path, method: init?.method ?? 'GET' })
      if (path === '/api/v1/me') return Promise.resolve(meResponse())
      if (path === '/api/v1/me/password') return Promise.resolve(new Response(null, { status: 204 }))
      if (path === '/api/v1/auth/login') {
        return Promise.resolve(
          jsonResponse({ accessToken: 'new-access', refreshToken: 'new-refresh' }),
        )
      }
      throw new Error(`unexpected fetch: ${path}`)
    })
    vi.stubGlobal('fetch', fetchMock)

    renderPage()

    await userEvent.type(screen.getByLabelText('Current password'), 'OldPassw0rd!')
    await userEvent.type(screen.getByLabelText('New password'), 'NewPassw0rd!')
    await userEvent.type(screen.getByLabelText('Confirm password'), 'NewPassw0rd!')
    await userEvent.click(screen.getByRole('button', { name: 'Change password' }))

    await waitFor(() => expect(tokenStorage.read()?.accessToken).toBe('new-access'))

    const relevant = calls.filter((c) => c.path === '/api/v1/me/password' || c.path === '/api/v1/auth/login')
    expect(relevant.map((c) => c.path)).toEqual(['/api/v1/me/password', '/api/v1/auth/login'])

    const loginCallIndex = calls.findIndex((c) => c.path === '/api/v1/auth/login')
    expect(loginCallIndex).toBeGreaterThanOrEqual(0)
    const loginCall = fetchMock.mock.calls[loginCallIndex] as [string, RequestInit]
    const loginBody = JSON.parse(loginCall[1].body as string) as { email: string; password: string }
    expect(loginBody).toEqual({ email: 'pending@example.com', password: 'NewPassw0rd!' })
  })
})
