import type { ReactNode } from 'react'
import { render, screen } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import i18n from '../i18n'
import { AppRoutes } from './AppRoutes'

// The route table is what is under test; the screens themselves have their own suites.
vi.mock('../features/auth/LoginPage', () => ({ LoginPage: () => <p>login screen</p> }))
vi.mock('../features/tree/TreePage', () => ({ TreePage: () => <p>tree screen</p> }))
vi.mock('../features/members/MembersPage', () => ({ MembersPage: () => <p>members screen</p> }))
vi.mock('./ProtectedRoute', () => ({
  ProtectedRoute: ({ children }: { children: ReactNode }) => <>{children}</>,
}))

const renderAt = (path: string) =>
  render(
    <I18nextProvider i18n={i18n}>
      <MemoryRouter initialEntries={[path]}>
        <AppRoutes />
      </MemoryRouter>
    </I18nextProvider>,
  )

describe('AppRoutes', () => {
  it('serves the tree at the root, since it is the product home screen', async () => {
    renderAt('/')

    expect(await screen.findByText('tree screen')).toBeInTheDocument()
  })

  it('serves the members list at /members', async () => {
    renderAt('/members')

    expect(await screen.findByText('members screen')).toBeInTheDocument()
  })

  it('serves the login screen without the app chrome', async () => {
    renderAt('/login')

    expect(await screen.findByText('login screen')).toBeInTheDocument()
  })

  it('redirects an unknown path home rather than showing nothing', async () => {
    renderAt('/nowhere')

    expect(await screen.findByText('tree screen')).toBeInTheDocument()
  })
})
