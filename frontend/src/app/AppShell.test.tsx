import { render, screen } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../i18n'
import { AppShell } from './AppShell'

let permissions: string[] = []

vi.mock('../features/auth/AuthContext', () => ({
  useAuth: () => ({
    user: { email: 'admin@example.com' },
    hasPermission: (permission: string) => permissions.includes(permission),
    logout: vi.fn(),
  }),
}))

const renderShell = () =>
  render(
    <I18nextProvider i18n={i18n}>
      <MemoryRouter>
        <AppShell familyName="عائلة السقا" statLine="4 · 3">
          <p>canvas</p>
        </AppShell>
      </MemoryRouter>
    </I18nextProvider>,
  )

describe('AppShell', () => {
  beforeEach(() => {
    permissions = []
  })

  it('links to the tree and the members list, so neither needs a typed URL', () => {
    renderShell()

    expect(screen.getByRole('link', { name: i18n.t('nav.tree') })).toHaveAttribute('href', '/')
    expect(screen.getByRole('link', { name: i18n.t('nav.members') })).toHaveAttribute(
      'href',
      '/members',
    )
  })

  it('shows the family name and the stat line', () => {
    renderShell()

    expect(screen.getAllByText('عائلة السقا').length).toBeGreaterThan(0)
    expect(screen.getByText('4 · 3')).toBeInTheDocument()
  })

  it('hides destinations the caller has no permission for', () => {
    renderShell()

    expect(screen.queryByText(i18n.t('nav.users'))).not.toBeInTheDocument()
    expect(screen.queryByText(i18n.t('nav.roles'))).not.toBeInTheDocument()
    expect(screen.queryByText(i18n.t('nav.audit'))).not.toBeInTheDocument()
  })

  it('shows a permitted destination, but disabled until its phase ships', () => {
    permissions = ['User.View']
    renderShell()

    // Visible so the operator knows it exists; inert so it cannot lead nowhere.
    expect(screen.getByRole('button', { name: i18n.t('nav.users') })).toBeDisabled()
  })

  it('renders the screen it wraps', () => {
    renderShell()

    expect(screen.getByText('canvas')).toBeInTheDocument()
  })
})
