import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n'
import { EMPTY_LIFE_DETAILS } from './lifeDetails'
import { MembersPage } from './MembersPage'
import { membersApi } from './membersApi'
import type { FamilyMember } from './types'
import { ApiError } from '../../services/apiClient'

vi.mock('./membersApi')

// A mutable flag lets a single test flip permissions off; beforeEach resets it to the
// permissive default so the other tests are unaffected.
let permissive = true
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    // The page now renders inside AppShell, which reads the signed-in user and can sign out.
    user: { email: 'admin@example.com', familyTreeName: 'عائلة السقا', permissions: [] },
    hasPermission: () => permissive,
    logout: vi.fn(),
  }),
}))

const member = (over: Partial<FamilyMember> = {}): FamilyMember => ({
  id: 'a',
  name: 'سليمان',
  parentId: null,
  version: 1,
  createdAt: '2026-08-16T12:00:00Z',
  updatedAt: '2026-08-16T12:00:00Z',
  dateOfBirth: null,
  dateOfDeath: null,
  isDeceased: false,
  ...over,
})

const renderPage = () => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <MembersPage />
        </MemoryRouter>
      </QueryClientProvider>
    </I18nextProvider>,
  )
}

describe('MembersPage', () => {
  beforeEach(() => {
    // Mock call history is cleared globally between tests (vite.config.ts `clearMocks: true`),
    // so no per-file vi.clearAllMocks() is needed here.
    permissive = true
    vi.mocked(membersApi.list).mockResolvedValue([member()])
    vi.mocked(membersApi.create).mockResolvedValue(member({ id: 'b', name: 'فارس' }))
    vi.mocked(membersApi.update).mockResolvedValue(member({ name: 'سليمان أحمد', version: 2 }))
    vi.mocked(membersApi.remove).mockResolvedValue(undefined)
  })

  it('lists the members returned by the API', async () => {
    renderPage()

    expect(await screen.findByText('سليمان')).toBeInTheDocument()
  })

  it('shows an empty state when the family has no members', async () => {
    vi.mocked(membersApi.list).mockResolvedValue([])
    renderPage()

    expect(await screen.findByText(i18n.t('members.empty'))).toBeInTheDocument()
  })

  it('creates a first-generation member when no parent is chosen', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('members.add') }))
    await user.type(screen.getByLabelText(i18n.t('members.name')), 'عمر')
    await user.click(screen.getByRole('button', { name: i18n.t('members.save') }))

    await waitFor(() => expect(membersApi.create).toHaveBeenCalledWith('عمر', null, EMPTY_LIFE_DETAILS),
    )
  })

  it('creates a child under the selected parent', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('members.add') }))
    await user.type(screen.getByLabelText(i18n.t('members.name')), 'فارس')
    await user.selectOptions(screen.getByLabelText(i18n.t('members.parent')), 'a')
    await user.click(screen.getByRole('button', { name: i18n.t('members.save') }))

    await waitFor(() => expect(membersApi.create).toHaveBeenCalledWith('فارس', 'a', EMPTY_LIFE_DETAILS),
    )
  })

  it('sends the current version when renaming', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('members.edit') }))
    const nameField = screen.getByLabelText(i18n.t('members.name'))
    await user.clear(nameField)
    await user.type(nameField, 'سليمان أحمد')
    await user.click(screen.getByRole('button', { name: i18n.t('members.save') }))

    await waitFor(() => expect(membersApi.update).toHaveBeenCalledWith('a', 'سليمان أحمد', 1, EMPTY_LIFE_DETAILS),
    )
  })

  it('does not offer a parent selector when editing', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('members.edit') }))

    expect(screen.queryByLabelText(i18n.t('members.parent'))).not.toBeInTheDocument()
  })

  it('deletes a member once the in-app dialog is confirmed', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('members.delete') }))
    // The row button only opens the dialog — nothing is destroyed until it is confirmed.
    expect(membersApi.remove).not.toHaveBeenCalled()
    await user.click(screen.getByRole('button', { name: i18n.t('modal.confirmDelete') }))

    await waitFor(() => expect(membersApi.remove).toHaveBeenCalledWith('a'))
  })

  it('does not delete when the dialog is cancelled', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('members.delete') }))
    await user.click(screen.getByRole('button', { name: i18n.t('modal.cancel') }))

    expect(membersApi.remove).not.toHaveBeenCalled()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('translates a server error code instead of showing it raw', async () => {
    const user = userEvent.setup()
    vi.mocked(membersApi.remove).mockRejectedValue(new ApiError('MEMBER_HAS_CHILDREN', 409))
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('members.delete') }))
    await user.click(screen.getByRole('button', { name: i18n.t('modal.confirmDelete') }))

    expect(await screen.findByText(i18n.t('errors.MEMBER_HAS_CHILDREN'))).toBeInTheDocument()
  })

  it('closes the edit form and refreshes data on a concurrency conflict', async () => {
    const user = userEvent.setup()
    vi.mocked(membersApi.update).mockRejectedValue(new ApiError('CONCURRENCY_CONFLICT', 409))
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('members.edit') }))
    const nameField = screen.getByLabelText(i18n.t('members.name'))
    await user.clear(nameField)
    await user.type(nameField, 'سليمان أحمد')
    await user.click(screen.getByRole('button', { name: i18n.t('members.save') }))

    expect(await screen.findByText(i18n.t('errors.CONCURRENCY_CONFLICT'))).toBeInTheDocument()
    expect(screen.queryByLabelText(i18n.t('members.name'))).not.toBeInTheDocument()
    await waitFor(() => expect(membersApi.list).toHaveBeenCalledTimes(2))
  })

  it('keeps the edit form open with the user input intact on a validation error', async () => {
    const user = userEvent.setup()
    vi.mocked(membersApi.update).mockRejectedValue(new ApiError('MEMBER_NAME_TOO_LONG', 400))
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('members.edit') }))
    const nameField = screen.getByLabelText(i18n.t('members.name'))
    await user.clear(nameField)
    await user.type(nameField, 'سليمان أحمد')
    await user.click(screen.getByRole('button', { name: i18n.t('members.save') }))

    expect(await screen.findByText(i18n.t('errors.MEMBER_NAME_TOO_LONG'))).toBeInTheDocument()
    expect(screen.getByLabelText(i18n.t('members.name'))).toBeInTheDocument()
  })

  it('hides add, edit, and delete controls without permission', async () => {
    permissive = false
    renderPage()

    expect(await screen.findByText('سليمان')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: i18n.t('members.add') })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: i18n.t('members.edit') })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: i18n.t('members.delete') })).not.toBeInTheDocument()
  })
})
