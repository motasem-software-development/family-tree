import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { I18nextProvider } from 'react-i18next'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n'
import { MembersPage } from './MembersPage'
import { membersApi } from './membersApi'
import type { FamilyMember } from './types'
import { ApiError } from '../../services/apiClient'

vi.mock('./membersApi')

// A mutable flag lets a single test flip permissions off; beforeEach resets it to the
// permissive default so the other tests are unaffected.
let permissive = true
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ hasPermission: () => permissive }),
}))

const member = (over: Partial<FamilyMember> = {}): FamilyMember => ({
  id: 'a',
  name: 'سليمان',
  parentId: null,
  version: 1,
  createdAt: '2026-08-16T12:00:00Z',
  updatedAt: '2026-08-16T12:00:00Z',
  ...over,
})

const renderPage = () => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MembersPage />
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

    await waitFor(() => expect(membersApi.create).toHaveBeenCalledWith('عمر', null))
  })

  it('creates a child under the selected parent', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('members.add') }))
    await user.type(screen.getByLabelText(i18n.t('members.name')), 'فارس')
    await user.selectOptions(screen.getByLabelText(i18n.t('members.parent')), 'a')
    await user.click(screen.getByRole('button', { name: i18n.t('members.save') }))

    await waitFor(() => expect(membersApi.create).toHaveBeenCalledWith('فارس', 'a'))
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

    await waitFor(() => expect(membersApi.update).toHaveBeenCalledWith('a', 'سليمان أحمد', 1))
  })

  it('does not offer a parent selector when editing', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('members.edit') }))

    expect(screen.queryByLabelText(i18n.t('members.parent'))).not.toBeInTheDocument()
  })

  it('deletes a member', async () => {
    const user = userEvent.setup()
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('members.delete') }))

    await waitFor(() => expect(membersApi.remove).toHaveBeenCalledWith('a'))
  })

  it('does not delete when the confirmation is declined', async () => {
    const user = userEvent.setup()
    vi.spyOn(window, 'confirm').mockReturnValue(false)
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('members.delete') }))

    expect(membersApi.remove).not.toHaveBeenCalled()
  })

  it('translates a server error code instead of showing it raw', async () => {
    const user = userEvent.setup()
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    vi.mocked(membersApi.remove).mockRejectedValue(new ApiError('MEMBER_HAS_CHILDREN', 409))
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('members.delete') }))

    expect(await screen.findByText(i18n.t('errors.MEMBER_HAS_CHILDREN'))).toBeInTheDocument()
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
