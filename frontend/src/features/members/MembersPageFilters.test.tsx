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

/** Deceased, both dates known and a national ID on file — the fully-populated row. */
const ZAKARIA: FamilyMemberListItem = {
  ...stamp,
  id: 'z1',
  name: 'زكريا',
  parentId: 'd1',
  version: 1,
  dateOfBirth: '1940-03-02',
  dateOfDeath: '2011-03-01',
  isDeceased: true,
  nationalId: '012345678',
  branchId: 'z1',
  branchName: 'زكريا',
  generation: 1,
}

/** Known to have died, date lost — the case that must not be given an age. */
const KHALED: FamilyMemberListItem = {
  ...stamp,
  id: 'k1',
  name: 'خالد',
  parentId: 'd1',
  version: 1,
  dateOfBirth: '1935-06-10',
  isDeceased: true,
  branchId: 'k1',
  branchName: 'خالد',
  generation: 1,
}

const ALL = [DAWOOD, SULEIMAN, FARIS, ZAKARIA, KHALED]

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

const cellsOf = (name: string): string[] =>
  [...rowFor(name).querySelectorAll('td')].map((cell) => cell.textContent ?? '')

/**
 * One cell, found by its column heading rather than by a hard-coded index. Positional lookups
 * broke every time a column was added or removed, and they broke as four unrelated assertion
 * failures that named the wrong subject — "expected 'Living' to contain 'Palestine'" says
 * nothing about the column having moved.
 */
const cellOf = (name: string, heading: string): string => {
  const headings = [...screen.getAllByRole('columnheader')].map((cell) => cell.textContent ?? '')
  const column = headings.indexOf(heading)
  if (column === -1) throw new Error(`No column headed "${heading}" in [${headings.join(', ')}]`)
  return cellsOf(name)[column] ?? ''
}

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

    expect(cellOf('فارس', i18n.t('filters.branch'))).toBe('سليمان')
  })

  it('renders the root member as Root rather than blank', async () => {
    // The root belongs to no branch; specification §21 renders that as "Root".
    renderPage()
    await waitForRow('داوود')

    expect(cellOf('داوود', i18n.t('filters.branch'))).toBe(i18n.t('filters.branchRoot'))
  })

  it('shows the country of residence with its flag', async () => {
    renderPage()
    await waitForRow('فارس')

    expect(cellOf('فارس', i18n.t('filters.country'))).toContain('فلسطين')
    expect(cellOf('فارس', i18n.t('filters.country'))).toContain('🇵🇸')
  })

  it('renders a dash for a member with no country', async () => {
    renderPage()
    await waitForRow('داوود')

    expect(cellOf('داوود', i18n.t('filters.country'))).toBe('—')
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

describe('MembersPage columns', () => {
  it('reads identity, then life, then placement', async () => {
    renderPage()
    await waitForRow('فارس')

    expect([...screen.getAllByRole('columnheader')].map((cell) => cell.textContent)).toEqual([
      i18n.t('members.name'),
      i18n.t('members.nationalId'),
      i18n.t('members.dateOfBirth'),
      i18n.t('members.age'),
      i18n.t('filters.country'),
      i18n.t('filters.branch'),
      '',
    ])
  })

  it('no longer carries a father column, since the lineage already follows the name', async () => {
    renderPage()
    await waitForRow('فارس')

    const headings = [...screen.getAllByRole('columnheader')].map((cell) => cell.textContent)
    expect(headings).not.toContain(i18n.t('members.parent'))
    // The fact itself is not lost — it reads out of the name cell.
    expect(cellOf('فارس', i18n.t('members.name'))).toContain('سليمان داوود')
  })

  it('shows the national ID, keeping its leading zero', async () => {
    renderPage()
    await waitForRow('زكريا')

    expect(cellOf('زكريا', i18n.t('members.nationalId'))).toBe('012345678')
  })

  it('renders a dash where no national ID is on file', async () => {
    renderPage()
    await waitForRow('فارس')

    expect(cellOf('فارس', i18n.t('members.nationalId'))).toBe('—')
  })

  it('carries the life status as a row tint rather than a column of repeated words', async () => {
    renderPage()
    await waitForRow('زكريا')

    const headings = [...screen.getAllByRole('columnheader')].map((cell) => cell.textContent)
    expect(headings).not.toContain(i18n.t('members.status'))

    expect(rowFor('زكريا').style.background).toBe('var(--sunken)')
    expect(rowFor('فارس').style.background).toBe('var(--success-subtle)')
  })

  it('keeps a labelled, non-colour carrier of the status in the name cell', async () => {
    // The tint alone would be invisible to a screen reader and to a colour-blind reader.
    renderPage()
    await waitForRow('زكريا')

    expect(
      rowFor('زكريا').querySelector(`[aria-label="${i18n.t('members.deceased')}"]`),
    ).not.toBeNull()
    expect(
      rowFor('فارس').querySelector(`[aria-label="${i18n.t('members.living')}"]`),
    ).not.toBeNull()
  })

  it('gives a deceased member their age at death, not their age today', async () => {
    // Against today this would read 86 and would grow every time the page is opened.
    renderPage()
    await waitForRow('زكريا')

    expect(cellOf('زكريا', i18n.t('members.age'))).toBe('70')
  })

  it('leaves the age blank for a member known to have died on an unknown date', async () => {
    // Measuring against today would report an age they never reached.
    renderPage()
    await waitForRow('خالد')

    expect(cellOf('خالد', i18n.t('members.age'))).toBe('—')
  })

  it('dashes the birth date and the age for a member with nothing on file', async () => {
    renderPage()
    await waitForRow('داوود')

    expect(cellOf('داوود', i18n.t('members.dateOfBirth'))).toBe('—')
    expect(cellOf('داوود', i18n.t('members.age'))).toBe('—')
  })

  it('writes the birth date as dd/MM/yyyy', async () => {
    renderPage()
    await waitForRow('زكريا')

    // 2 March, not 3 February: the pattern is the same in both languages by request, which is
    // exactly what Intl would not give — an `en` locale would render this 03/02/1940.
    expect(cellOf('زكريا', i18n.t('members.dateOfBirth'))).toContain('02/03/1940')
  })

  it('has no death column, but carries the date in the birth cell for a hover to reveal', async () => {
    renderPage()
    await waitForRow('زكريا')

    const headings = [...screen.getAllByRole('columnheader')].map((cell) => cell.textContent)
    expect(headings).not.toContain(i18n.t('members.dateOfDeath'))

    // Present in the DOM and in the accessibility tree — opacity, not display, is what hides it,
    // so a screen reader still reads what a mouse user has to hover for.
    const reveal = rowFor('زكريا').querySelector('.revealed-on-hover')
    expect(reveal?.textContent).toContain('01/03/2011')
  })

  it('gives a living member nothing to reveal', async () => {
    renderPage()
    await waitForRow('فارس')

    expect(rowFor('فارس').querySelector('.revealed-on-hover')).toBeNull()
  })
})
