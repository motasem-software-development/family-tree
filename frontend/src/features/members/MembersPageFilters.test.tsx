import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n'
import { countriesApi } from '../countries/countriesApi'
import { MembersPage } from './MembersPage'
import { membersApi } from './membersApi'
import type { FamilyMemberListItem } from './types'

vi.mock('./membersApi')
vi.mock('../countries/countriesApi', () => ({ countriesApi: { list: vi.fn() } }))

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    user: { email: 'admin@example.com', familyTreeName: 'عائلة السقا', permissions: [] },
    hasPermission: () => true,
    logout: vi.fn(),
  }),
}))

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
}

const COUNTRIES = [
  { id: 165, code: 'PS', nameAr: 'فلسطين', nameEn: 'Palestine', dialCode: '+970' },
]

/** داوود (root) → سليمان → فارس. فارس lives in Palestine; the other two have no country. */
const DAWOOD: FamilyMemberListItem = {
  ...stamp,
  id: 'd1',
  name: 'داوود',
  parentId: null,
  version: 1,
  branchId: null,
  branchName: null,
  generation: 0,
}

const SULEIMAN: FamilyMemberListItem = {
  ...stamp,
  id: 's1',
  name: 'سليمان',
  parentId: 'd1',
  version: 1,
  branchId: 's1',
  branchName: 'سليمان',
  generation: 1,
}

const FARIS: FamilyMemberListItem = {
  ...stamp,
  id: 'f1',
  name: 'فارس',
  parentId: 's1',
  version: 1,
  countryId: 165,
  countryCode: 'PS',
  branchId: 's1',
  branchName: 'سليمان',
  generation: 2,
}

const ALL = [DAWOOD, SULEIMAN, FARIS]

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

/**
 * The row whose NAME cell starts with this name. Matching on any text would be ambiguous — a
 * name appears again in a child's parent cell, in a branch cell, and in the branch dropdown.
 */
const rowFor = (name: string): HTMLElement => {
  const row = screen
    .getAllByRole('row')
    .find((candidate) => candidate.querySelector('td')?.textContent?.startsWith(name))
  if (row === undefined) throw new Error(`No row for ${name}`)
  return row
}

/** Name, Parent, Country, Branch, actions. */
const cellsOf = (name: string): string[] =>
  [...rowFor(name).querySelectorAll('td')].map((cell) => cell.textContent ?? '')

const waitForRow = (name: string) => waitFor(() => rowFor(name))

beforeEach(() => {
  vi.mocked(membersApi.list).mockResolvedValue(ALL)
  vi.mocked(membersApi.branches).mockResolvedValue([{ id: 's1', name: 'سليمان' }])
  vi.mocked(membersApi.generations).mockResolvedValue([0, 1, 2])
  vi.mocked(countriesApi.list).mockResolvedValue(COUNTRIES)
})

describe('MembersPage filters', () => {
  it('renders the filter controls above the table', async () => {
    renderPage()

    expect(
      await screen.findByRole('group', { name: i18n.t('filters.title') }),
    ).toBeInTheDocument()
  })

  it('asks the server for the chosen filter', async () => {
    const user = userEvent.setup()
    renderPage()
    await waitForRow('فارس')

    await user.selectOptions(screen.getByLabelText(i18n.t('filters.status')), 'deceased')

    await waitFor(() =>
      expect(membersApi.list).toHaveBeenCalledWith(expect.objectContaining({ status: 'deceased' })),
    )
  })

  it('shows the branch each member belongs to', async () => {
    renderPage()
    await waitForRow('فارس')

    expect(cellsOf('فارس')[3]).toBe('سليمان')
  })

  it('renders the root member as Root rather than blank', async () => {
    // The root belongs to no branch; specification §21 renders that as "Root".
    renderPage()
    await waitForRow('داوود')

    expect(cellsOf('داوود')[3]).toBe(i18n.t('filters.branchRoot'))
  })

  it('shows the country of residence with its flag', async () => {
    renderPage()
    await waitForRow('فارس')

    expect(cellsOf('فارس')[2]).toContain('فلسطين')
    expect(cellsOf('فارس')[2]).toContain('🇵🇸')
  })

  it('renders a dash for a member with no country', async () => {
    renderPage()
    await waitForRow('داوود')

    expect(cellsOf('داوود')[2]).toBe('—')
  })

  it('composes a full name through a father the filter dropped', async () => {
    // fullName walks the parent chain through the list it is given, and a filtered list has
    // holes in it. The lineage index is built from the unfiltered query for exactly this reason.
    vi.mocked(membersApi.list).mockImplementation((filters) =>
      Promise.resolve(filters?.status === 'alive' ? [FARIS] : ALL),
    )

    const user = userEvent.setup()
    renderPage()
    await waitForRow('داوود')

    await user.selectOptions(screen.getByLabelText(i18n.t('filters.status')), 'alive')

    // سليمان is filtered out of the rows, but فارس still reads as فارس سليمان داوود.
    await waitFor(() => expect(screen.getAllByRole('row')).toHaveLength(2))
    expect(cellsOf('فارس')[0]).toContain('سليمان داوود')
  })

  it('says the list is filtered rather than empty when a filter matches nothing', async () => {
    // "No members yet" over a filtered-to-zero list tells the user something false.
    vi.mocked(membersApi.list).mockImplementation((filters) =>
      Promise.resolve(filters?.status === 'deceased' ? [] : ALL),
    )

    const user = userEvent.setup()
    renderPage()
    await waitForRow('فارس')

    await user.selectOptions(screen.getByLabelText(i18n.t('filters.status')), 'deceased')

    expect(await screen.findByText(i18n.t('filters.emptyFiltered'))).toBeInTheDocument()
    expect(screen.queryByText(i18n.t('members.empty'))).not.toBeInTheDocument()
  })

  it('offers a way out of a filtered-to-zero list', async () => {
    vi.mocked(membersApi.list).mockImplementation((filters) =>
      Promise.resolve(filters?.status === 'deceased' ? [] : ALL),
    )

    const user = userEvent.setup()
    renderPage()
    await waitForRow('فارس')
    await user.selectOptions(screen.getByLabelText(i18n.t('filters.status')), 'deceased')
    await screen.findByText(i18n.t('filters.emptyFiltered'))

    // Two Reset buttons now: the bar's and the empty state's. The empty state's is the one a
    // user who has scrolled past the bar will reach for.
    const resets = screen.getAllByRole('button', { name: i18n.t('filters.reset') })
    await user.click(resets[resets.length - 1])

    await waitForRow('فارس')
  })

  it('still says the family is empty when it really is', async () => {
    vi.mocked(membersApi.list).mockResolvedValue([])
    renderPage()

    expect(await screen.findByText(i18n.t('members.empty'))).toBeInTheDocument()
  })
})
