import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n'
import { ReportsPage } from './ReportsPage'
import { reportsApi } from './reportsApi'
import type { ReportsResponse } from './types'

vi.mock('./reportsApi')

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    user: { email: 'admin@example.com', familyTreeName: 'عائلة السقا', permissions: [] },
    hasPermission: () => true,
    logout: vi.fn(),
  }),
}))

const report = (over: Partial<ReportsResponse> = {}): ReportsResponse => ({
  generatedOn: '2026-08-22',
  structure: {
    totalMembers: 5,
    depth: 3,
    generations: [
      { generation: 1, count: 2 },
      { generation: 2, count: 2 },
      { generation: 3, count: 1 },
    ],
    branches: [{ id: 'a', name: 'سليمان', descendantCount: 3, depth: 3 }],
    membersWithChildren: 2,
    leafMembers: 3,
    averageChildrenPerParent: 1.5,
  },
  lifeStatus: {
    living: 4,
    deceased: 1,
    byGeneration: [{ generation: 1, living: 1, deceased: 1 }],
    livingAges: [
      { bracket: '0-17', count: 1 },
      { bracket: '18-29', count: 0 },
      { bracket: '30-44', count: 2 },
      { bracket: '45-59', count: 0 },
      { bracket: '60-74', count: 0 },
      { bracket: '75+', count: 0 },
    ],
    livingWithoutBirthDate: 1,
    longevity: { count: 1, minYears: 80, maxYears: 80, medianYears: 80 },
  },
  completeness: { totalMembers: 5, completeRecords: 3, issues: [] },
  upcoming: {
    windowDays: 30,
    birthdayCount: 0,
    anniversaryCount: 0,
    birthdays: [],
    anniversaries: [],
  },
  activity: { windowDays: 30, addedCount: 0, editedCount: 0, added: [], edited: [] },
  ...over,
})

const renderPage = () => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <ReportsPage />
        </MemoryRouter>
      </QueryClientProvider>
    </I18nextProvider>,
  )
}

describe('ReportsPage', () => {
  beforeEach(() => {
    vi.mocked(reportsApi.get).mockReset()
    vi.mocked(reportsApi.get).mockResolvedValue(report())
  })

  it('shows how deep the tree runs', async () => {
    renderPage()

    expect(await screen.findByTestId('structure-depth')).toHaveTextContent('3')
  })

  it('shows the headline member count', async () => {
    renderPage()

    expect(await screen.findByTestId('structure-total')).toHaveTextContent('5')
  })

  it('lists a row per generation', async () => {
    renderPage()

    expect(await screen.findAllByTestId('generation-row')).toHaveLength(3)
  })

  it('lists a row per branch with its descendant count', async () => {
    renderPage()

    const branch = await screen.findByTestId('branch-row')
    expect(branch).toHaveTextContent('سليمان')
    expect(branch).toHaveTextContent('3')
  })

  it('shows the living and deceased split', async () => {
    renderPage()

    expect(await screen.findByTestId('living-count')).toHaveTextContent('4')
    expect(await screen.findByTestId('deceased-count')).toHaveTextContent('1')
  })

  // Design §5: the histogram must not imply a population it did not measure.
  it('discloses living members whose age is unknown', async () => {
    renderPage()

    expect(await screen.findByTestId('living-without-birth-date')).toHaveTextContent('1')
  })

  // Null longevity means "not measurable" — showing zeros would read as "measured, and zero".
  it('says longevity is unmeasurable rather than showing zeros', async () => {
    vi.mocked(reportsApi.get).mockResolvedValue(
      report({ lifeStatus: { ...report().lifeStatus, longevity: null } }),
    )

    renderPage()

    expect(await screen.findByTestId('longevity-unavailable')).toBeInTheDocument()
  })

  it('reports a failure instead of rendering an empty screen', async () => {
    vi.mocked(reportsApi.get).mockRejectedValue(new Error('boom'))

    renderPage()

    expect(await screen.findByRole('alert')).toBeInTheDocument()
  })
})
