import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { I18nextProvider } from 'react-i18next'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n'
import { countriesApi } from '../countries/countriesApi'
import { membersApi } from '../members/membersApi'
import { FilterBar } from './FilterBar'
import type { MemberFilters } from './filterParams'

vi.mock('../members/membersApi', () => ({
  membersApi: { branches: vi.fn(), generations: vi.fn() },
}))

vi.mock('../countries/countriesApi', () => ({
  countriesApi: { list: vi.fn() },
}))

const BRANCHES = [
  { id: 'b1', name: 'سليمان' },
  { id: 'b2', name: 'عمر' },
]

const GENERATIONS = [0, 1, 2, 3]

const COUNTRIES = [
  { id: 165, code: 'PS', nameAr: 'فلسطين', nameEn: 'Palestine', dialCode: '+970' },
  { id: 62, code: 'EG', nameAr: 'مصر', nameEn: 'Egypt', dialCode: '+20' },
]

const onChange = vi.fn()
const onReset = vi.fn()

const renderBar = (filters: MemberFilters = {}, activeCount = 0, layout?: 'inline' | 'stacked') => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <FilterBar
          filters={filters}
          activeCount={activeCount}
          onChange={onChange}
          onReset={onReset}
          layout={layout}
        />
      </QueryClientProvider>
    </I18nextProvider>,
  )
}

beforeEach(() => {
  vi.mocked(membersApi.branches).mockResolvedValue(BRANCHES)
  vi.mocked(membersApi.generations).mockResolvedValue(GENERATIONS)
  vi.mocked(countriesApi.list).mockResolvedValue(COUNTRIES)
})

describe('FilterBar', () => {
  it('renders all five controls with accessible names', async () => {
    renderBar()

    expect(screen.getByLabelText(i18n.t('filters.search'))).toBeInTheDocument()
    expect(screen.getByLabelText(i18n.t('filters.status'))).toBeInTheDocument()
    expect(await screen.findByLabelText(i18n.t('filters.branch'))).toBeInTheDocument()
    expect(screen.getByLabelText(i18n.t('filters.generation'))).toBeInTheDocument()
    expect(screen.getByLabelText(i18n.t('filters.country'))).toBeInTheDocument()
  })

  it('reports a status choice', async () => {
    const user = userEvent.setup()
    renderBar()

    await user.selectOptions(screen.getByLabelText(i18n.t('filters.status')), 'deceased')

    expect(onChange).toHaveBeenCalledWith('status', 'deceased')
  })

  it('clears the status by choosing All', async () => {
    const user = userEvent.setup()
    renderBar({ status: 'deceased' }, 1)

    await user.selectOptions(screen.getByLabelText(i18n.t('filters.status')), 'all')

    expect(onChange).toHaveBeenCalledWith('status', undefined)
  })

  it('reports a branch choice by id', async () => {
    const user = userEvent.setup()
    renderBar()

    await user.click(await screen.findByLabelText(i18n.t('filters.branch')))
    await user.click(await screen.findByText('عمر'))

    expect(onChange).toHaveBeenCalledWith('branchId', 'b2')
  })

  it('clears the branch by choosing the empty row', async () => {
    // Clearing a filter must be possible without reloading the page.
    const user = userEvent.setup()
    renderBar({ branchId: 'b2' }, 1)

    await user.click(await screen.findByLabelText(i18n.t('filters.branch')))
    await user.click(await screen.findByText(i18n.t('filters.branchAll')))

    expect(onChange).toHaveBeenCalledWith('branchId', undefined)
  })

  it('reports a country choice as a number', async () => {
    const user = userEvent.setup()
    renderBar()

    await user.click(await screen.findByLabelText(i18n.t('filters.country')))
    await user.click(await screen.findByText(/فلسطين/))

    expect(onChange).toHaveBeenCalledWith('countryId', 165)
  })

  it('offers the generations the server returned, root first', async () => {
    renderBar()

    const select = screen.getByLabelText(i18n.t('filters.generation'))
    await waitFor(() => expect(select).toHaveTextContent(i18n.t('filters.generationRoot')))

    const options = [...select.querySelectorAll('option')].map((option) => option.textContent)
    // "0" alone reads as a missing value rather than as the root (specification §21).
    expect(options).toEqual([
      i18n.t('filters.generationAll'),
      i18n.t('filters.generationRoot'),
      '1',
      '2',
      '3',
    ])
  })

  it('reports a generation choice as a number', async () => {
    const user = userEvent.setup()
    renderBar()

    const select = screen.getByLabelText(i18n.t('filters.generation'))
    await waitFor(() => expect(select).toHaveTextContent(i18n.t('filters.generationRoot')))
    await user.selectOptions(select, '2')

    expect(onChange).toHaveBeenCalledWith('generation', 2)
  })

  it('reports generation zero rather than dropping it', async () => {
    const user = userEvent.setup()
    renderBar()

    const select = screen.getByLabelText(i18n.t('filters.generation'))
    await waitFor(() => expect(select).toHaveTextContent(i18n.t('filters.generationRoot')))
    await user.selectOptions(select, '0')

    expect(onChange).toHaveBeenCalledWith('generation', 0)
  })

  it('debounces the search box into a single change', async () => {
    // Real timers: fake ones deadlock against TanStack Query's own scheduling in this tree, and
    // the assertion with teeth — no call per character — does not need them.
    const user = userEvent.setup()
    renderBar()
    onChange.mockClear()

    await user.type(screen.getByLabelText(i18n.t('filters.search')), 'فارس')
    expect(onChange).not.toHaveBeenCalled()

    await waitFor(() => expect(onChange).toHaveBeenCalledWith('search', 'فارس'))

    expect(onChange).toHaveBeenCalledTimes(1)
    // Every prefix would be its own request and its own recursive CTE.
    ;['ف', 'فا', 'فار'].forEach((prefix) =>
      expect(onChange).not.toHaveBeenCalledWith('search', prefix),
    )
  })

  it('shows the search term a linked URL arrived with', () => {
    // Otherwise the box reads empty over filtered results and the user cannot see why.
    renderBar({ search: 'فارس' }, 1)

    expect(screen.getByLabelText(i18n.t('filters.search'))).toHaveValue('فارس')
  })

  it('disables reset when nothing is filtered', () => {
    // A live Reset over an unfiltered list is a control that does nothing.
    renderBar({}, 0)

    expect(screen.getByRole('button', { name: i18n.t('filters.reset') })).toBeDisabled()
  })

  it('resets when something is filtered', async () => {
    const user = userEvent.setup()
    renderBar({ status: 'alive' }, 1)

    await user.click(screen.getByRole('button', { name: i18n.t('filters.reset') }))

    expect(onReset).toHaveBeenCalled()
  })

  it('lays out in a row inline and in a column stacked', () => {
    const { unmount } = renderBar({}, 0, 'inline')
    expect(screen.getByRole('group')).toHaveAttribute('data-layout', 'inline')
    unmount()

    renderBar({}, 0, 'stacked')
    expect(screen.getByRole('group')).toHaveAttribute('data-layout', 'stacked')
  })

  it('asks for the branches and generations of the selected root', async () => {
    renderBar({ rootId: 'r1' })

    await waitFor(() => expect(membersApi.branches).toHaveBeenCalledWith('r1'))
    expect(membersApi.generations).toHaveBeenCalledWith('r1')
  })
})
