import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n'
import { RolesPage } from './RolesPage'
import { rolesApi } from './rolesApi'
import type { Role } from './types'
import { ApiError } from '../../services/apiClient'

vi.mock('./rolesApi')

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    user: { email: 'admin@example.com', familyTreeName: 'عائلة السقا', permissions: [] },
    hasPermission: () => true,
    logout: vi.fn(),
  }),
}))

const testRole = (over: Partial<Role> = {}): Role => ({
  id: 'r1',
  name: 'Viewer',
  description: null,
  isSystem: true,
  userCount: 0,
  permissions: [],
  ...over,
})

const renderPage = () => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <RolesPage />
        </MemoryRouter>
      </QueryClientProvider>
    </I18nextProvider>,
  )
}

describe('RolesPage', () => {
  beforeEach(() => {
    vi.mocked(rolesApi.list).mockResolvedValue([testRole()])
    vi.mocked(rolesApi.permissions).mockResolvedValue([])
    vi.mocked(rolesApi.create).mockResolvedValue(testRole({ id: 'r2', isSystem: false }))
    vi.mocked(rolesApi.update).mockResolvedValue(testRole())
    vi.mocked(rolesApi.remove).mockResolvedValue(undefined)
  })

  it('lists roles with user counts', async () => {
    vi.mocked(rolesApi.list).mockResolvedValue([
      testRole({ id: 'r1', name: 'Administrator', isSystem: true, userCount: 1 }),
      testRole({ id: 'r2', name: 'Helper', isSystem: false, userCount: 0 }),
    ])
    renderPage()

    expect(await screen.findByText('Administrator')).toBeInTheDocument()
    expect(screen.getByText('Helper')).toBeInTheDocument()
    expect(screen.getByText('1')).toBeInTheDocument()
    expect(screen.getByText('0')).toBeInTheDocument()
  })

  it('offers no edit or delete on a built-in role, but both on a custom role', async () => {
    vi.mocked(rolesApi.list).mockResolvedValue([
      testRole({ id: 'r1', name: 'Administrator', isSystem: true }),
      testRole({ id: 'r2', name: 'Helper', isSystem: false }),
    ])
    renderPage()

    await screen.findByText('Administrator')

    const systemRow = screen.getByText('Administrator').closest('tr')
    const customRow = screen.getByText('Helper').closest('tr')
    if (systemRow === null || customRow === null) throw new Error('row not found')

    // Without the negative half, this would pass even if the actions never rendered anywhere —
    // check that the system role's row lacks them AND the custom role's row has them.
    expect(within(systemRow).queryByRole('button', { name: i18n.t('roles.edit') })).not.toBeInTheDocument()
    expect(within(systemRow).queryByRole('button', { name: i18n.t('roles.delete') })).not.toBeInTheDocument()
    expect(within(systemRow).getByText(i18n.t('roles.systemRoleHint'))).toBeInTheDocument()

    expect(within(customRow).getByRole('button', { name: i18n.t('roles.edit') })).toBeInTheDocument()
    expect(within(customRow).getByRole('button', { name: i18n.t('roles.delete') })).toBeInTheDocument()
  })

  it('shows a translated refusal when deletion is rejected', async () => {
    const user = userEvent.setup()
    vi.mocked(rolesApi.list).mockResolvedValue([
      testRole({ id: 'r2', name: 'Helper', isSystem: false }),
    ])
    vi.mocked(rolesApi.remove).mockRejectedValue(new ApiError('ROLE_IN_USE', 409))
    renderPage()
    await screen.findByText('Helper')

    await user.click(screen.getByRole('button', { name: i18n.t('roles.delete') }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: i18n.t('roles.delete') }))

    expect(await screen.findByText(i18n.t('errors.ROLE_IN_USE'))).toBeInTheDocument()
  })

  it('groups permission checkboxes from the catalog', async () => {
    const user = userEvent.setup()
    vi.mocked(rolesApi.permissions).mockResolvedValue([
      { code: 'Member.View', description: null },
      { code: 'User.View', description: null },
    ])
    renderPage()
    await screen.findByText('Viewer')

    await user.click(screen.getByRole('button', { name: i18n.t('roles.add') }))

    expect(
      await screen.findByRole('checkbox', { name: i18n.t('permissions.Member.View') }),
    ).toBeInTheDocument()
    expect(
      screen.getByRole('checkbox', { name: i18n.t('permissions.User.View') }),
    ).toBeInTheDocument()
  })
})
