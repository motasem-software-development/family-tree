import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n'
import { ApiError } from '../../services/apiClient'
import { membersApi } from '../members/membersApi'
import type { FamilyMember, FamilyTreeNode, FamilyTreeView } from '../members/types'
import { TreePage } from './TreePage'

vi.mock('../members/membersApi')

let permissions = ['Member.View', 'Member.Create', 'Member.Edit', 'Member.Delete']

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    user: { email: 'admin@example.com' },
    hasPermission: (permission: string) => permissions.includes(permission),
    logout: vi.fn(),
  }),
}))

const node = (
  id: string,
  name: string,
  generation: number,
  children: FamilyTreeNode[] = [],
  parentId: string | null = null,
): FamilyTreeNode => ({ id, name, parentId, generation, hasMoreChildren: false, children })

const VIEW: FamilyTreeView = {
  id: 't1',
  name: 'عائلة السقا',
  rootMembers: [node('s1', 'سليمان', 1, [node('f1', 'فارس', 2, [], 's1')]), node('s2', 'عمر', 1)],
}

const stamp = { createdAt: '2026-08-16T12:00:00Z', updatedAt: '2026-08-16T12:00:00Z' }

const FLAT: FamilyMember[] = [
  { id: 's1', name: 'سليمان', parentId: null, version: 1, ...stamp },
  { id: 'f1', name: 'فارس', parentId: 's1', version: 3, ...stamp },
  { id: 's2', name: 'عمر', parentId: null, version: 1, ...stamp },
]

const renderPage = () => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <TreePage />
        </MemoryRouter>
      </QueryClientProvider>
    </I18nextProvider>,
  )
}

describe('TreePage', () => {
  beforeEach(() => {
    permissions = ['Member.View', 'Member.Create', 'Member.Edit', 'Member.Delete']
    vi.mocked(membersApi.tree).mockResolvedValue(VIEW)
    vi.mocked(membersApi.list).mockResolvedValue(FLAT)
    vi.mocked(membersApi.create).mockResolvedValue({ ...FLAT[0], id: 'new', name: 'خالد' })
    vi.mocked(membersApi.update).mockResolvedValue({ ...FLAT[1], name: 'فارس أحمد', version: 4 })
    vi.mocked(membersApi.remove).mockResolvedValue(undefined)
    vi.mocked(membersApi.search).mockResolvedValue({ total: 0, items: [] })
  })

  afterEach(() => vi.restoreAllMocks())

  it('renders the root family and its first generation', async () => {
    renderPage()

    expect(await screen.findByRole('button', { name: /عائلة السقا/ })).toBeInTheDocument()
    expect(await screen.findByText('سليمان')).toBeInTheDocument()
    expect(screen.getByText('عمر')).toBeInTheDocument()
  })

  it('keeps a collapsed branch hidden until it is expanded', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')

    expect(screen.queryByText('فارس')).not.toBeInTheDocument()

    const treeitem = screen.getByRole('treeitem', { name: /سليمان/ })
    await user.click(within(treeitem).getByRole('button', { name: i18n.t('tree.expand') }))

    expect(await screen.findByText('فارس')).toBeInTheDocument()
  })

  it('opens the detail panel when a member is selected', async () => {
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByText('سليمان'))

    const panel = await screen.findByRole('complementary', { name: 'سليمان' })
    expect(within(panel).getByText(i18n.t('panel.descendants'))).toBeInTheDocument()
  })

  it('renders Move disabled, because its backend command is a later phase', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByText('سليمان'))

    const panel = await screen.findByRole('complementary', { name: 'سليمان' })
    expect(within(panel).getByRole('button', { name: i18n.t('tree.move') })).toBeDisabled()
  })

  it('sends the version it read when renaming', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')
    const treeitem = screen.getByRole('treeitem', { name: /سليمان/ })
    await user.click(within(treeitem).getByRole('button', { name: i18n.t('tree.expand') }))
    await user.click(await screen.findByText('فارس'))

    const panel = await screen.findByRole('complementary', { name: 'فارس' })
    await user.click(within(panel).getByRole('button', { name: i18n.t('tree.edit') }))
    const field = await screen.findByLabelText(i18n.t('modal.nameLabel'))
    await user.clear(field)
    await user.type(field, 'فارس أحمد')
    await user.click(screen.getByRole('button', { name: i18n.t('modal.save') }))

    // Version 3 comes from the flat list — the tree DTO carries no version at all.
    await waitFor(() => expect(membersApi.update).toHaveBeenCalledWith('f1', 'فارس أحمد', 3))
  })

  it('shows the blocked dialog instead of a delete confirm when the member has children', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByText('سليمان'))

    const panel = await screen.findByRole('complementary', { name: 'سليمان' })
    await user.click(within(panel).getByRole('button', { name: i18n.t('tree.delete') }))

    expect(await screen.findByText(i18n.t('modal.blockedTitle'))).toBeInTheDocument()
    // Nothing to confirm — the designed escape hatch is Move, which is a later phase.
    expect(
      screen.queryByRole('button', { name: i18n.t('modal.confirmDelete') }),
    ).not.toBeInTheDocument()
    expect(membersApi.remove).not.toHaveBeenCalled()
  })

  it('deletes a leaf member', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByText('عمر'))

    const panel = await screen.findByRole('complementary', { name: 'عمر' })
    await user.click(within(panel).getByRole('button', { name: i18n.t('tree.delete') }))
    await user.click(await screen.findByRole('button', { name: i18n.t('modal.confirmDelete') }))

    await waitFor(() => expect(membersApi.remove).toHaveBeenCalledWith('s2'))
  })

  it('translates a server error code rather than showing it raw', async () => {
    const user = userEvent.setup()
    vi.mocked(membersApi.create).mockRejectedValue(new ApiError('MEMBER_NAME_TOO_LONG', 400))
    renderPage()
    await user.click(await screen.findByText('سليمان'))

    const panel = await screen.findByRole('complementary', { name: 'سليمان' })
    await user.click(within(panel).getByRole('button', { name: i18n.t('tree.addChild') }))
    await user.type(await screen.findByLabelText(i18n.t('modal.nameLabel')), 'خالد')
    await user.click(screen.getByRole('button', { name: i18n.t('modal.add') }))

    expect(await screen.findByText(i18n.t('errors.MEMBER_NAME_TOO_LONG'))).toBeInTheDocument()
    // The form stays open so the name the user typed is not thrown away.
    expect(screen.getByLabelText(i18n.t('modal.nameLabel'))).toBeInTheDocument()
  })

  it('dims non-matching members while searching instead of hiding them', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')

    await user.type(screen.getByLabelText(i18n.t('tree.searchPlaceholder')), 'عمر')

    // Both rows remain on the canvas — search is a highlight, not a filter.
    expect(screen.getByRole('treeitem', { name: /سليمان/ })).toBeInTheDocument()
    expect(screen.getByRole('treeitem', { name: /عمر/ })).toBeInTheDocument()
  })

  it('shows the ancestor path for each search hit', async () => {
    const user = userEvent.setup()
    vi.mocked(membersApi.search).mockResolvedValue({
      total: 39,
      items: [
        {
          id: 'f1',
          name: 'محمد',
          generation: 3,
          ancestors: [
            { id: 's1', name: 'داوود' },
            { id: 's2', name: 'سلمان' },
          ],
        },
      ],
    })
    renderPage()

    await user.type(await screen.findByLabelText(/بحث|Search/i), 'محمد')

    // Generation alone was the old caption and could not tell 39 محمدs apart.
    expect(await screen.findByText('داوود ‹ سلمان')).toBeInTheDocument()
    expect(await screen.findByText(/39/)).toBeInTheDocument()
  })

  it('asks the server rather than filtering the loaded tree', async () => {
    const user = userEvent.setup()
    vi.mocked(membersApi.search).mockResolvedValue({ total: 0, items: [] })
    renderPage()

    await user.type(await screen.findByLabelText(/بحث|Search/i), 'فارس')

    await waitFor(() => expect(membersApi.search).toHaveBeenCalledWith('فارس', 8))
  })

  it('disables write actions for a caller who only has view permission', async () => {
    const user = userEvent.setup()
    permissions = ['Member.View']
    renderPage()
    await user.click(await screen.findByText('سليمان'))

    const panel = await screen.findByRole('complementary', { name: 'سليمان' })
    expect(within(panel).getByRole('button', { name: i18n.t('tree.addChild') })).toBeDisabled()
    expect(within(panel).getByRole('button', { name: i18n.t('tree.edit') })).toBeDisabled()
    expect(within(panel).getByRole('button', { name: i18n.t('tree.delete') })).toBeDisabled()
  })

  it('labels each row with its position in the whole outline', async () => {
    // Windowing removes rows from the DOM, so a screen reader can no longer count them. Without
    // setsize/posinset it would announce "1 of 2" on a 349-member tree.
    renderPage()

    const rows = await screen.findAllByRole('treeitem')
    expect(rows[0]).toHaveAttribute('aria-setsize', String(rows.length))
    expect(rows[0]).toHaveAttribute('aria-posinset', '1')
  })

  it('scrolls a revealed search hit into view', async () => {
    const user = userEvent.setup()
    // jsdom does not implement Element.scrollTo at all, so vi.spyOn has nothing to wrap until a
    // no-op stub exists on the prototype for it to replace.
    Element.prototype.scrollTo ??= () => {}
    const scrollTo = vi.spyOn(Element.prototype, 'scrollTo').mockImplementation(() => {})

    vi.mocked(membersApi.search).mockResolvedValue({
      total: 1,
      items: [{ id: 'f1', name: 'فارس', generation: 2, ancestors: [{ id: 's1', name: 'سليمان' }] }],
    })
    renderPage()

    await user.type(await screen.findByLabelText(/بحث|Search/i), 'فارس')
    // The dropdown result button contains BOTH the name and the ancestor caption, so its
    // accessible name is the two joined — unique, whereas the caption alone ("سليمان") also
    // matches the root row rendered in the outline behind the dropdown.
    await user.click(await screen.findByRole('button', { name: /^فارس\s+سليمان$/ }))

    // Virtualized, the row may not be in the DOM at all — expanding its ancestors is no longer
    // enough to put it in front of the user.
    await waitFor(() => expect(scrollTo).toHaveBeenCalled())
  })
})
