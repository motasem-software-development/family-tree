import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n'
import { UsersPage } from './UsersPage'
import { usersApi } from './usersApi'
import type { User } from './types'
import { ApiError } from '../../services/apiClient'

vi.mock('./usersApi')

// A mutable flag lets a single test flip permissions off; beforeEach resets it to the
// permissive default so the other tests are unaffected.
let permissive = true
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    user: { email: 'admin@example.com', familyTreeName: 'عائلة السقا', permissions: [] },
    hasPermission: () => permissive,
    logout: vi.fn(),
  }),
}))

const testUser = (over: Partial<User> = {}): User => ({
  id: 'a',
  email: 'a@example.com',
  isActive: true,
  mustChangePassword: false,
  lastLoginAt: null,
  roles: [{ id: 'r1', name: 'Viewer' }],
  ...over,
})

const renderPage = () => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <UsersPage />
        </MemoryRouter>
      </QueryClientProvider>
    </I18nextProvider>,
  )
}

describe('UsersPage', () => {
  beforeEach(() => {
    permissive = true
    vi.mocked(usersApi.list).mockResolvedValue([testUser()])
    vi.mocked(usersApi.create).mockResolvedValue(testUser({ id: 'b' }))
    vi.mocked(usersApi.update).mockResolvedValue(testUser())
    vi.mocked(usersApi.setActive).mockResolvedValue(testUser({ isActive: false }))
    vi.mocked(usersApi.resetPassword).mockResolvedValue(testUser())
  })

  it('lists users with their roles and status', async () => {
    vi.mocked(usersApi.list).mockResolvedValue([
      testUser({ id: 'a', email: 'active@example.com', isActive: true, roles: [{ id: 'r1', name: 'Viewer' }] }),
      testUser({ id: 'b', email: 'inactive@example.com', isActive: false, roles: [] }),
    ])
    renderPage()

    expect(await screen.findByText('active@example.com')).toBeInTheDocument()
    expect(screen.getByText('inactive@example.com')).toBeInTheDocument()
    expect(screen.getByText('Viewer')).toBeInTheDocument()
    expect(screen.getByText(i18n.t('users.active'))).toBeInTheDocument()
    expect(screen.getByText(i18n.t('users.inactive'))).toBeInTheDocument()
  })

  it('marks only the user who still owes a password change', async () => {
    vi.mocked(usersApi.list).mockResolvedValue([
      testUser({ id: 'a', email: 'pending@example.com', mustChangePassword: true }),
      testUser({ id: 'b', email: 'settled@example.com', mustChangePassword: false }),
    ])
    renderPage()

    await screen.findByText('pending@example.com')

    // Without the negative half, this would pass even if the badge rendered unconditionally
    // for every row: asserting exactly one match proves it is scoped to the flagged user.
    expect(screen.getAllByText(i18n.t('users.pendingPasswordChange'))).toHaveLength(1)
  })

  it('gates the add button on User.Create', async () => {
    permissive = false
    renderPage()
    await screen.findByText('a@example.com')

    // A one-sided assertion here would pass even if the button never rendered at all —
    // check both the absent and present cases.
    expect(screen.queryByRole('button', { name: i18n.t('users.add') })).not.toBeInTheDocument()

    permissive = true
    renderPage()
    await screen.findAllByText('a@example.com')

    expect(screen.getByRole('button', { name: i18n.t('users.add') })).toBeInTheDocument()
  })

  it('shows a translated refusal when deactivation is rejected', async () => {
    const user = userEvent.setup()
    vi.mocked(usersApi.setActive).mockRejectedValue(new ApiError('LAST_ADMINISTRATOR', 409))
    renderPage()
    await screen.findByText('a@example.com')

    await user.click(screen.getByRole('button', { name: i18n.t('users.deactivate') }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: i18n.t('users.deactivate') }))

    expect(await screen.findByText(i18n.t('errors.LAST_ADMINISTRATOR'))).toBeInTheDocument()
  })
})
