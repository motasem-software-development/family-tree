import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n'
import { countriesApi } from '../countries/countriesApi'
import { membersApi } from '../members/membersApi'
import type { FamilyTreeNode, FamilyTreeView } from '../members/types'
import { TreePage } from './TreePage'

vi.mock('../members/membersApi')
vi.mock('../countries/countriesApi')

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    user: { email: 'admin@example.com', familyTreeName: 'عائلة السقا', permissions: [] },
    hasPermission: () => true,
    logout: vi.fn(),
  }),
}))

const node = (
  id: string,
  name: string,
  generation: number,
  children: FamilyTreeNode[] = [],
  parentId: string | null = null,
  matches = true,
): FamilyTreeNode => ({ id, name, parentId, generation, hasMoreChildren: false, matches, children })

const VIEW: FamilyTreeView = {
  id: 't1',
  name: 'عائلة السقا',
  rootMembers: [node('s1', 'سليمان', 1, [node('f1', 'فارس', 2, [], 's1')])],
}

/**
 * What the server returns for a filter that only فارس matches: سليمان is kept to hold up their
 * matching child, flagged as a non-match (design spec §4.2).
 */
const FILTERED_VIEW: FamilyTreeView = {
  id: 't1',
  name: 'عائلة السقا',
  rootMembers: [node('s1', 'سليمان', 1, [node('f1', 'فارس', 2, [], 's1')], null, false)],
}

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

/**
 * The outline node button for a member, which is what a click selects.
 *
 * Found through the name span's `title`, not by accessible name: a treeitem holds three buttons
 * — the caret, the node, and the actions menu — and both the node and the actions menu carry the
 * member's name. The branch filter's own dropdown carries it a fourth time.
 */
const nodeButton = (name: string): HTMLElement => {
  const item = screen
    .getAllByRole('treeitem')
    .find((candidate) => candidate.textContent?.includes(name))
  if (item === undefined) throw new Error(`No outline row for ${name}`)

  const button = within(item).getByTitle(name).closest('button')
  if (button === null) throw new Error(`No node button for ${name}`)
  return button
}

beforeEach(() => {
  vi.mocked(membersApi.tree).mockResolvedValue(VIEW)
  vi.mocked(membersApi.list).mockResolvedValue([])
  vi.mocked(membersApi.search).mockResolvedValue({ total: 0, items: [] })
  vi.mocked(membersApi.branches).mockResolvedValue([{ id: 's1', name: 'سليمان' }])
  vi.mocked(membersApi.generations).mockResolvedValue([0, 1, 2])
  vi.mocked(countriesApi.list).mockResolvedValue([])
})

describe('TreePage filters', () => {
  it('renders the filter controls', async () => {
    renderPage()

    expect(
      await screen.findByRole('group', { name: i18n.t('filters.title') }),
    ).toBeInTheDocument()
  })

  it('asks the server for the chosen filter', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')

    await user.selectOptions(screen.getByLabelText(i18n.t('filters.status')), 'deceased')

    await waitFor(() =>
      expect(membersApi.tree).toHaveBeenCalledWith(
        expect.objectContaining({ status: 'deceased' }),
        undefined,
      ),
    )
  })

  it('keeps a non-matching ancestor visible', async () => {
    // Dropping them would detach the subtree and render the outline as garbage.
    vi.mocked(membersApi.tree).mockResolvedValue(FILTERED_VIEW)
    renderPage()

    expect(await screen.findByText('سليمان')).toBeInTheDocument()
  })

  it('does not select a dimmed row', async () => {
    // A detail panel for a member who is not in the result is a dead end the user has to close.
    vi.mocked(membersApi.tree).mockResolvedValue(FILTERED_VIEW)
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')

    await user.click(nodeButton('سليمان'))

    // Named, not just any complementary region: the app's own navigation is one too.
    expect(screen.queryByRole('complementary', { name: 'سليمان' })).not.toBeInTheDocument()
    expect(nodeButton('سليمان')).toHaveAttribute('aria-disabled', 'true')
  })

  it('still selects a matching row', async () => {
    vi.mocked(membersApi.tree).mockResolvedValue(FILTERED_VIEW)
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')

    // فارس sits under سليمان, which the outline opens on demand.
    await user.click(screen.getByRole('button', { name: i18n.t('tree.expand') }))
    await user.click(nodeButton('فارس'))

    expect(await screen.findByRole('complementary', { name: /فارس/ })).toBeInTheDocument()
  })

  it('numbers the root generation zero in the detail panel', async () => {
    // Design spec §1.2: the panel follows the generation filter, which counts from the selected
    // root. سليمان is the root of this view, so they read 0 rather than the absolute 1.
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')

    await user.click(nodeButton('سليمان'))

    const panel = await screen.findByRole('complementary', { name: /سليمان/ })
    expect(within(panel).getByText(`${i18n.t('tree.gen')} 0`)).toBeInTheDocument()
  })

  it('numbers a child one below the root in the detail panel', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('tree.expand') }))
    await user.click(nodeButton('فارس'))

    const panel = await screen.findByRole('complementary', { name: /فارس/ })
    expect(within(panel).getByText(`${i18n.t('tree.gen')} 1`)).toBeInTheDocument()
  })

  it('numbers a search hit from the same root as the panel', async () => {
    // Two captions on one page must not disagree. The search endpoint's generation is absolute
    // 1-based, so a root member arrives as 1 and must read 0.
    vi.mocked(membersApi.search).mockResolvedValue({
      total: 1,
      items: [{ id: 's1', name: 'سليمان', generation: 1, ancestors: [] }],
    })

    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')

    await user.type(await screen.findByLabelText(i18n.t('tree.searchPlaceholder')), 'سليمان')

    expect(await screen.findByText(`${i18n.t('tree.gen')} 0`)).toBeInTheDocument()
  })

  it('still expands a dimmed row', async () => {
    // It is there only to hold up a matching descendant; disabling its expander would hide the
    // match it exists to carry.
    vi.mocked(membersApi.tree).mockResolvedValue(FILTERED_VIEW)
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('سليمان')

    await user.click(screen.getByRole('button', { name: i18n.t('tree.expand') }))

    expect(await screen.findByText('فارس')).toBeInTheDocument()
  })
})
