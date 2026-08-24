import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { I18nextProvider } from 'react-i18next'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n'
import { countriesApi } from '../countries/countriesApi'
import { membersApi } from '../members/membersApi'
import { FilterControls } from './FilterControls'
import type { MemberFilters } from './filterParams'

vi.mock('../members/membersApi', () => ({
  membersApi: { branches: vi.fn(), generations: vi.fn() },
}))

vi.mock('../countries/countriesApi', () => ({
  countriesApi: { list: vi.fn() },
}))

/** Drives useIsCompact, which reads matchMedia rather than a width. */
const stubViewport = (compact: boolean) => {
  vi.stubGlobal(
    'matchMedia',
    vi.fn(() => ({
      matches: compact,
      addEventListener: () => {},
      removeEventListener: () => {},
    })),
  )
}

const onChange = vi.fn()
const onReset = vi.fn()

const renderControls = (filters: MemberFilters = {}, activeCount = 0) => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <FilterControls
          filters={filters}
          activeCount={activeCount}
          onChange={onChange}
          onReset={onReset}
        />
      </QueryClientProvider>
    </I18nextProvider>,
  )
}

const openButton = () => screen.getByRole('button', { name: new RegExp(i18n.t('filters.open')) })

beforeEach(() => {
  vi.mocked(membersApi.branches).mockResolvedValue([])
  vi.mocked(membersApi.generations).mockResolvedValue([0, 1, 2])
  vi.mocked(countriesApi.list).mockResolvedValue([])
})

afterEach(() => vi.unstubAllGlobals())

describe('FilterControls', () => {
  describe('above the breakpoint', () => {
    beforeEach(() => stubViewport(false))

    it('puts the controls on the page', () => {
      renderControls()

      expect(screen.getByRole('group', { name: i18n.t('filters.title') })).toBeInTheDocument()
    })

    it('offers no Filters button', () => {
      renderControls()

      expect(screen.queryByRole('button', { name: i18n.t('filters.open') })).not.toBeInTheDocument()
    })
  })

  describe('below the breakpoint', () => {
    beforeEach(() => stubViewport(true))

    it('hides the controls behind a Filters button', () => {
      renderControls()

      expect(screen.queryByRole('group', { name: i18n.t('filters.title') })).not.toBeInTheDocument()
      expect(openButton()).toBeInTheDocument()
    })

    it('carries no badge when nothing is filtered', () => {
      renderControls({}, 0)

      expect(openButton()).toHaveTextContent(new RegExp(`^${i18n.t('filters.open')}$`))
    })

    it('carries the active count as a badge', () => {
      // Design spec §6.2's stated failure mode: a user filtered without knowing why the list
      // looks short.
      renderControls({ status: 'alive', generation: 2 }, 2)

      expect(
        screen.getByLabelText(i18n.t('filters.activeCount', { count: 2 })),
      ).toHaveTextContent('2')
    })

    it('does not steal focus on mount', async () => {
      // The effect that restores focus to the trigger on close must not fire for the initial
      // closed state: loading either page on a narrow screen would move focus — and the scroll
      // position — away from wherever the user was.
      renderControls()

      expect(openButton()).not.toHaveFocus()
      expect(document.body).toHaveFocus()
    })

    it('opens the sheet', async () => {
      const user = userEvent.setup()
      renderControls()

      await user.click(openButton())

      expect(screen.getByRole('dialog', { name: i18n.t('filters.title') })).toBeInTheDocument()
      expect(screen.getByRole('group', { name: i18n.t('filters.title') })).toBeInTheDocument()
    })

    it('closes the sheet on its close button', async () => {
      const user = userEvent.setup()
      renderControls()
      await user.click(openButton())

      await user.click(screen.getByRole('button', { name: i18n.t('filters.close') }))

      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    })

    it('closes the sheet on Escape', async () => {
      // Every other overlay on these screens does.
      const user = userEvent.setup()
      renderControls()
      await user.click(openButton())

      await user.keyboard('{Escape}')

      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    })

    it('stays open while a filter is changed inside it', async () => {
      // The user is usually setting more than one; closing after each would make the second a
      // second trip.
      const user = userEvent.setup()
      const { rerender } = renderControls({}, 0)
      await user.click(openButton())

      await user.selectOptions(screen.getByLabelText(i18n.t('filters.status')), 'deceased')
      expect(onChange).toHaveBeenCalledWith('status', 'deceased')

      const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
      rerender(
        <I18nextProvider i18n={i18n}>
          <QueryClientProvider client={queryClient}>
            <FilterControls
              filters={{ status: 'deceased' }}
              activeCount={1}
              onChange={onChange}
              onReset={onReset}
            />
          </QueryClientProvider>
        </I18nextProvider>,
      )

      expect(screen.getByRole('dialog')).toBeInTheDocument()
    })

    it('moves focus into the sheet and back to the button', async () => {
      const user = userEvent.setup()
      renderControls()

      await user.click(openButton())
      expect(screen.getByRole('dialog')).toHaveFocus()

      await user.keyboard('{Escape}')
      expect(openButton()).toHaveFocus()
    })
  })
})
