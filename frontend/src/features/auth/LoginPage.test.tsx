import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { Providers } from '../../app/providers'
import i18n from '../../i18n'
import { tokenStorage } from '../../services/tokenStorage'
import { LoginPage } from './LoginPage'

const jsonResponse = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const renderPage = () => render(<Providers><LoginPage /></Providers>)

describe('LoginPage', () => {
  beforeEach(async () => {
    tokenStorage.clear()
    await i18n.changeLanguage('en')
  })

  afterEach(() => vi.restoreAllMocks())

  it('stores the tokens returned by a successful sign-in', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      jsonResponse({ accessToken: 'abc', expiresAt: new Date().toISOString(), refreshToken: 'def' }),
    ))

    renderPage()
    await userEvent.type(screen.getByLabelText('Email'), 'admin@example.com')
    await userEvent.type(screen.getByLabelText('Password'), 'Str0ng!Password')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    await waitFor(() => expect(tokenStorage.read()?.accessToken).toBe('abc'))
  })

  it('translates the backend error code rather than showing it raw', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      jsonResponse({ code: 'INVALID_CREDENTIALS' }, 401),
    ))

    renderPage()
    await userEvent.type(screen.getByLabelText('Email'), 'admin@example.com')
    await userEvent.type(screen.getByLabelText('Password'), 'wrong')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('alert'))
      .toHaveTextContent('The email or password is incorrect.')
    expect(tokenStorage.read()).toBeNull()
  })

  it('shows the Arabic message for the same code when Arabic is active', async () => {
    await i18n.changeLanguage('ar')
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      jsonResponse({ code: 'INVALID_CREDENTIALS' }, 401),
    ))

    renderPage()
    await userEvent.type(screen.getByLabelText('البريد الإلكتروني'), 'admin@example.com')
    await userEvent.type(screen.getByLabelText('كلمة المرور'), 'wrong')
    await userEvent.click(screen.getByRole('button', { name: 'تسجيل الدخول' }))

    expect(await screen.findByRole('alert'))
      .toHaveTextContent('البريد الإلكتروني أو كلمة المرور غير صحيحة.')
  })

  it('redirects away from the login form when already signed in', async () => {
    tokenStorage.write({ accessToken: 'abc', refreshToken: 'def' })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      jsonResponse({
        id: '0193...',
        email: 'admin@example.com',
        tenantId: '0193...',
        familyTreeName: 'عائلة السقا',
        permissions: [],
        mustChangePassword: false,
      }),
    ))

    renderPage()

    await waitFor(() => expect(screen.queryByLabelText('Email')).not.toBeInTheDocument())
  })
})
