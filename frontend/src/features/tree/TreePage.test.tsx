import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n'
import { EMPTY_CONTACT_DETAILS } from '../members/contactDetails'
import { EMPTY_LIFE_DETAILS } from '../members/lifeDetails'
import { ApiError } from '../../services/apiClient'
import { countriesApi } from '../countries/countriesApi'
import { membersApi } from '../members/membersApi'
import type { FamilyMemberListItem, FamilyTreeNode, FamilyTreeView } from '../members/types'
import { TreePage } from './TreePage'

vi.mock('../members/membersApi')
vi.mock('../countries/countriesApi')

let permissions = ['Member.View', 'Member.Create', 'Member.Edit', 'Member.Delete', 'Member.Move']

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
): FamilyTreeNode => ({ id, name, parentId, generation, hasMoreChildren: false, matches: true, children })

const VIEW: FamilyTreeView = {
  id: 't1',
  name: 'عائلة السقا',
  rootMembers: [node('s1', 'سليمان', 1, [node('f1', 'فارس', 2, [], 's1')]), node('s2', 'عمر', 1)],
}

const stamp = {
  createdAt: '2026-08-16T12:00:00Z',
  updatedAt: '2026-08-16T12:00:00Z',
  dateOfBirth: null,
  dateOfDeath: null,
  isDeceased: false,
  nationalId: null,
  mobileNumber: null,
  whatsAppNumber: null,
  countryId: null,
  countryCode: null,
  // The list endpoint derives these; the page does not read them yet (Plan 3 adds the columns).
  branchId: null,
  branchName: null,
  generation: 0,
}

const COUNTRIES = [
  { id: 1, code: 'PS', nameAr: 'فلسطين', nameEn: 'Palestine', dialCode: '+970' },
  { id: 2, code: 'EG', nameAr: 'مصر', nameEn: 'Egypt', dialCode: '+20' },
]

const FLAT: FamilyMemberListItem[] = [
  { id: 's1', name: 'سليمان', parentId: null, version: 1, ...stamp },
  { id: 'f1', name: 'فارس', parentId: 's1', version: 3, ...stamp },
  { id: 's2', name: 'عمر', parentId: null, version: 1, ...stamp },
]

const renderPageAt = (path: string) => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[path]}>
          <TreePage />
        </MemoryRouter>
      </QueryClientProvider>
    </I18nextProvider>,
  )
}

const renderPage = () => renderPageAt('/')

describe('TreePage', () => {
  beforeEach(() => {
    permissions = ['Member.View', 'Member.Create', 'Member.Edit', 'Member.Delete', 'Member.Move']
    vi.mocked(membersApi.tree).mockResolvedValue(VIEW)
    vi.mocked(membersApi.list).mockResolvedValue(FLAT)
    vi.mocked(membersApi.create).mockResolvedValue({ ...FLAT[0], id: 'new', name: 'خالد' })
    vi.mocked(membersApi.update).mockResolvedValue({ ...FLAT[1], name: 'فارس أحمد', version: 4 })
    vi.mocked(membersApi.remove).mockResolvedValue(undefined)
    vi.mocked(membersApi.move).mockResolvedValue({ ...FLAT[1], parentId: 's2', version: 4 })
    vi.mocked(membersApi.search).mockResolvedValue({ total: 0, items: [] })
    vi.mocked(countriesApi.list).mockResolvedValue(COUNTRIES)
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

  it('titles the panel with the composed lineage name', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')
    const treeitem = screen.getByRole('treeitem', { name: /سليمان/ })
    await user.click(within(treeitem).getByRole('button', { name: i18n.t('tree.expand') }))
    await user.click(await screen.findByText('فارس'))

    // The heading carries the same split as a members row: given name, then a muted lineage.
    // Its own text node is the given name alone, so the assertion goes through textContent.
    const panel = await screen.findByRole('complementary', { name: 'فارس سليمان' })
    expect(
      within(panel).getByText(
        (_content, element) =>
          element?.tagName === 'DIV' && element.textContent === 'فارس سليمان',
      ),
    ).toBeInTheDocument()
  })

  it('enables Move now that the backend command exists', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByText('سليمان'))

    const panel = await screen.findByRole('complementary', { name: 'سليمان' })
    expect(within(panel).getByRole('button', { name: i18n.t('tree.move') })).toBeEnabled()
  })

  it('sends the version it read when renaming', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')
    const treeitem = screen.getByRole('treeitem', { name: /سليمان/ })
    await user.click(within(treeitem).getByRole('button', { name: i18n.t('tree.expand') }))
    await user.click(await screen.findByText('فارس'))

    const panel = await screen.findByRole('complementary', { name: 'فارس سليمان' })
    await user.click(within(panel).getByRole('button', { name: i18n.t('tree.edit') }))
    const field = await screen.findByLabelText(i18n.t('modal.nameLabel'))
    await user.clear(field)
    await user.type(field, 'فارس أحمد')
    await user.click(screen.getByRole('button', { name: i18n.t('modal.save') }))

    // Version 3 comes from the flat list — the tree DTO carries no version at all. The life
    // details ride the same join, so an edit that touches only the name still round-trips them.
    await waitFor(() =>
      expect(membersApi.update).toHaveBeenCalledWith(
        'f1',
        'فارس أحمد',
        3,
        EMPTY_LIFE_DETAILS,
        EMPTY_CONTACT_DETAILS,
      ),
    )
  })

  /** Opens the editor on فارس, whose father سليمان makes him the one member with a lineage. */
  const openEditorOnFares = async (user: ReturnType<typeof userEvent.setup>) => {
    renderPage()
    await screen.findByText('سليمان')
    const treeitem = screen.getByRole('treeitem', { name: /سليمان/ })
    await user.click(within(treeitem).getByRole('button', { name: i18n.t('tree.expand') }))
    await user.click(await screen.findByText('فارس'))

    const panel = await screen.findByRole('complementary', { name: 'فارس سليمان' })
    await user.click(within(panel).getByRole('button', { name: i18n.t('tree.edit') }))
    return screen.findByLabelText(i18n.t('modal.nameLabel'))
  }

  it('shows the ancestry beside the editable name, and does not let it be edited', async () => {
    const user = userEvent.setup()
    await openEditorOnFares(user)

    const lineage = screen.getByLabelText(i18n.t('modal.lineageLabel'))
    expect(lineage).toHaveValue('سليمان')
    expect(lineage).toBeDisabled()
  })

  it('carries the contact details already on file through a rename', async () => {
    // The bug this pins: the panel's Edit button used to seed only the name, so Update — which
    // replaces rather than merges — cleared whatever else the member had.
    vi.mocked(membersApi.list).mockResolvedValue([
      FLAT[0],
      {
        ...FLAT[1],
        nationalId: '123456789',
        mobileNumber: '+970599123456',
        countryId: 1,
        countryCode: 'PS',
      },
      FLAT[2],
    ])

    const user = userEvent.setup()
    const field = await openEditorOnFares(user)
    await user.clear(field)
    await user.type(field, 'فارس أحمد')
    await user.click(screen.getByRole('button', { name: i18n.t('modal.save') }))

    await waitFor(() =>
      expect(membersApi.update).toHaveBeenCalledWith(
        'f1',
        'فارس أحمد',
        3,
        EMPTY_LIFE_DETAILS,
        expect.objectContaining({
          nationalId: '123456789',
          mobileNumber: '+970599123456',
          countryId: 1,
        }),
      ),
    )
  })

  it('saves contact details entered in the tree editor', async () => {
    const user = userEvent.setup()
    await openEditorOnFares(user)

    await user.type(screen.getByLabelText(i18n.t('members.nationalId')), '987654321')

    const country = screen.getByRole('combobox', { name: i18n.t('members.country') })
    await user.click(country)
    await user.type(country, 'palest')
    await user.click(await screen.findByRole('option', { name: /فلسطين/ }))

    await user.click(screen.getByRole('button', { name: i18n.t('modal.save') }))

    await waitFor(() =>
      expect(membersApi.update).toHaveBeenCalledWith(
        'f1',
        'فارس',
        3,
        EMPTY_LIFE_DETAILS,
        expect.objectContaining({ nationalId: '987654321', countryId: 1 }),
      ),
    )
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

  it('names the delete confirmation by the composed name, not the given name alone', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')
    const treeitem = screen.getByRole('treeitem', { name: /سليمان/ })
    await user.click(within(treeitem).getByRole('button', { name: i18n.t('tree.expand') }))
    await user.click(await screen.findByText('فارس'))

    const panel = await screen.findByRole('complementary', { name: 'فارس سليمان' })
    await user.click(within(panel).getByRole('button', { name: i18n.t('tree.delete') }))

    // Deleting is irreversible, so the dialog has to say which فارس is about to go.
    expect(
      await screen.findByText(`${i18n.t('modal.deleteTitle')} — فارس سليمان`),
    ).toBeInTheDocument()
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

    await user.type(await screen.findByLabelText(i18n.t('tree.searchPlaceholder')), 'محمد')

    // Generation alone was the old caption and could not tell 39 محمدs apart.
    expect(await screen.findByText('داوود ‹ سلمان')).toBeInTheDocument()
    expect(await screen.findByText(/39/)).toBeInTheDocument()
  })

  it('asks the server rather than filtering the loaded tree', async () => {
    const user = userEvent.setup()
    vi.mocked(membersApi.search).mockResolvedValue({ total: 0, items: [] })
    renderPage()

    await user.type(await screen.findByLabelText(i18n.t('tree.searchPlaceholder')), 'فارس')

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

    await user.type(await screen.findByLabelText(i18n.t('tree.searchPlaceholder')), 'فارس')
    // The dropdown result button contains BOTH the name and the ancestor caption, so its
    // accessible name is the two joined — unique, whereas the caption alone ("سليمان") also
    // matches the root row rendered in the outline behind the dropdown.
    await user.click(await screen.findByRole('button', { name: /^فارس\s+سليمان$/ }))

    // Virtualized, the row may not be in the DOM at all — expanding its ancestors is no longer
    // enough to put it in front of the user.
    await waitFor(() => expect(scrollTo).toHaveBeenCalled())
  })

  // Report rows link here with ?memberId=, so the member must be selected on arrival.
  it('preselects the member named by the memberId parameter', async () => {
    renderPageAt('/?memberId=s1')

    const treeitem = await screen.findByRole('treeitem', { name: /سليمان/ })
    expect(treeitem).toHaveAttribute('aria-selected', 'true')
  })

  // f1 (فارس) sits under the collapsed s1 (سليمان) branch — flattenTree contributes no row for
  // it until that branch is expanded. This is the case the feature exists for: report rows link
  // to arbitrary members, not just roots.
  it('reveals a non-root member: expands its ancestors, selects it, and opens its panel', async () => {
    renderPageAt('/?memberId=f1')

    const treeitem = await screen.findByRole('treeitem', { name: /فارس/ })
    expect(treeitem).toHaveAttribute('aria-selected', 'true')
    const panel = await screen.findByRole('complementary', { name: 'فارس سليمان' })
    expect(within(panel).getByText(i18n.t('panel.descendants'))).toBeInTheDocument()
  })

  it('ignores a memberId matching no member rather than failing', async () => {
    renderPageAt('/?memberId=00000000-0000-0000-0000-000000000000')

    // The tree still renders; nothing is selected.
    expect(await screen.findByText('عمر')).toBeInTheDocument()
    await waitFor(() => {
      const selected = screen.queryAllByRole('treeitem').filter(
        (item) => item.getAttribute('aria-selected') === 'true',
      )
      expect(selected).toHaveLength(0)
    })
  })

  it('selects nothing when the parameter is absent, as before', async () => {
    renderPageAt('/')

    expect(await screen.findByText('عمر')).toBeInTheDocument()
    await waitFor(() => {
      const selected = screen.queryAllByRole('treeitem').filter(
        (item) => item.getAttribute('aria-selected') === 'true',
      )
      expect(selected).toHaveLength(0)
    })
  })

  it('moves a member to the parent chosen in the dialog', async () => {
    vi.mocked(membersApi.search).mockResolvedValue({
      total: 1,
      items: [{ id: 's2', name: 'عمر', generation: 1, ancestors: [] }],
    })
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')
    const treeitem = screen.getByRole('treeitem', { name: /سليمان/ })
    await user.click(within(treeitem).getByRole('button', { name: i18n.t('tree.expand') }))
    await user.click(await screen.findByText('فارس'))

    const panel = await screen.findByRole('complementary', { name: 'فارس سليمان' })
    await user.click(within(panel).getByRole('button', { name: i18n.t('tree.move') }))

    // Scoped to the dialog: its own "نقل" confirm button shares the panel's Move-button copy,
    // and the tree row for عمر (still mounted behind the dialog) shares the search hit's name —
    // an unscoped query would match more than one element for either.
    const dialog = within(await screen.findByRole('dialog', { name: i18n.t('move.title') }))
    await user.type(await dialog.findByLabelText(i18n.t('move.searchPlaceholder')), 'عمر')
    await user.click(await dialog.findByRole('button', { name: /عمر/ }))
    await user.click(dialog.getByRole('button', { name: i18n.t('move.confirm') }))

    // Version 3 comes from the flat list, the same join the editor reads: a move is a write
    // like any other and must carry the version the client actually held.
    await waitFor(() => expect(membersApi.move).toHaveBeenCalledWith('f1', 's2', 3))
  })

  it('promotes a member to the first generation from the dialog', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')
    const treeitem = screen.getByRole('treeitem', { name: /سليمان/ })
    await user.click(within(treeitem).getByRole('button', { name: i18n.t('tree.expand') }))
    await user.click(await screen.findByText('فارس'))

    const panel = await screen.findByRole('complementary', { name: 'فارس سليمان' })
    await user.click(within(panel).getByRole('button', { name: i18n.t('tree.move') }))
    // Scoped to the dialog: its "نقل" confirm button shares the panel's Move-button copy, so an
    // unscoped query would match both the still-mounted panel trigger and this button.
    const dialog = within(await screen.findByRole('dialog', { name: i18n.t('move.title') }))
    await user.click(
      await dialog.findByRole('button', {
        name: i18n.t('move.rootOption', { family: 'عائلة السقا' }),
      }),
    )
    await user.click(dialog.getByRole('button', { name: i18n.t('move.confirm') }))

    await waitFor(() => expect(membersApi.move).toHaveBeenCalledWith('f1', null, 3))
  })

  it('opens Move from the context menu and completes the move', async () => {
    vi.mocked(membersApi.search).mockResolvedValue({
      total: 1,
      items: [{ id: 's2', name: 'عمر', generation: 1, ancestors: [] }],
    })
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')
    const treeitem = screen.getByRole('treeitem', { name: /سليمان/ })
    await user.click(within(treeitem).getByRole('button', { name: i18n.t('tree.expand') }))
    await user.click(
      await screen.findByRole('button', { name: `${i18n.t('tree.actions')} — فارس` }),
    )

    const menu = await screen.findByRole('menu')
    await user.click(within(menu).getByRole('menuitem', { name: i18n.t('tree.move') }))

    // The menu's backdrop sits over the dialog; it must close before interaction or the first
    // click is swallowed.
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()

    // Scoped to the dialog: its "نقل" confirm button shares the panel's Move-button copy, and
    // the tree row for عمر (still mounted behind the dialog) shares the search hit's name — an
    // unscoped query would match more than one element for either.
    const dialog = within(await screen.findByRole('dialog', { name: i18n.t('move.title') }))
    await user.type(await dialog.findByLabelText(i18n.t('move.searchPlaceholder')), 'عمر')
    await user.click(await dialog.findByRole('button', { name: /عمر/ }))
    await user.click(dialog.getByRole('button', { name: i18n.t('move.confirm') }))

    await waitFor(() => expect(membersApi.move).toHaveBeenCalledWith('f1', 's2', 3))
  })

  it('translates a rejected move rather than showing the raw code', async () => {
    vi.mocked(membersApi.move).mockRejectedValue(new ApiError('MOVE_CREATES_CYCLE', 409))
    vi.mocked(membersApi.search).mockResolvedValue({
      total: 1,
      items: [{ id: 's2', name: 'عمر', generation: 1, ancestors: [] }],
    })
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByText('سليمان'))

    const panel = await screen.findByRole('complementary', { name: 'سليمان' })
    await user.click(within(panel).getByRole('button', { name: i18n.t('tree.move') }))
    // Scoped to the dialog for the same reason as the other move tests: its confirm button and
    // the عمر search hit both collide with something else still mounted behind the dialog.
    const dialog = within(await screen.findByRole('dialog', { name: i18n.t('move.title') }))
    await user.type(await dialog.findByLabelText(i18n.t('move.searchPlaceholder')), 'عمر')
    await user.click(await dialog.findByRole('button', { name: /عمر/ }))
    await user.click(dialog.getByRole('button', { name: i18n.t('move.confirm') }))

    expect(await screen.findByText(i18n.t('errors.MOVE_CREATES_CYCLE'))).toBeInTheDocument()
    // The dialog stays open, so the next act is choosing another target rather than finding
    // the member again.
    expect(screen.getByLabelText(i18n.t('move.searchPlaceholder'))).toBeInTheDocument()
  })

  it('disables Move for a caller without the permission', async () => {
    permissions = ['Member.View']
    const user = userEvent.setup()
    renderPage()
    await user.click(await screen.findByText('سليمان'))

    const panel = await screen.findByRole('complementary', { name: 'سليمان' })
    expect(within(panel).getByRole('button', { name: i18n.t('tree.move') })).toBeDisabled()
  })
})
