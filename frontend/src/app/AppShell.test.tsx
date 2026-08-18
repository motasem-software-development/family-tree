import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ComponentProps } from 'react'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../i18n'
import { AppShell } from './AppShell'

let permissions: string[] = []

vi.mock('../features/auth/AuthContext', () => ({
  useAuth: () => ({
    user: { email: 'admin@example.com' },
    hasPermission: (permission: string) => permissions.includes(permission),
    logout: vi.fn(),
  }),
}))

const renderShell = (props: Partial<Omit<ComponentProps<typeof AppShell>, 'children'>> = {}) =>
  render(
    <I18nextProvider i18n={i18n}>
      <MemoryRouter>
        <AppShell familyName="عائلة السقا" statLine="4 · 3" {...props}>
          <p>canvas</p>
        </AppShell>
      </MemoryRouter>
    </I18nextProvider>,
  )

describe('AppShell', () => {
  beforeEach(() => {
    permissions = []
  })

  it('links to the tree and the members list, so neither needs a typed URL', () => {
    renderShell()

    expect(screen.getByRole('link', { name: i18n.t('nav.tree') })).toHaveAttribute('href', '/')
    expect(screen.getByRole('link', { name: i18n.t('nav.members') })).toHaveAttribute(
      'href',
      '/members',
    )
  })

  it('shows the family name and the stat line', () => {
    renderShell()

    expect(screen.getAllByText('عائلة السقا').length).toBeGreaterThan(0)
    expect(screen.getByText('4 · 3')).toBeInTheDocument()
  })

  it('hides destinations the caller has no permission for', () => {
    renderShell()

    expect(screen.queryByText(i18n.t('nav.users'))).not.toBeInTheDocument()
    expect(screen.queryByText(i18n.t('nav.roles'))).not.toBeInTheDocument()
    expect(screen.queryByText(i18n.t('nav.audit'))).not.toBeInTheDocument()
  })

  it('links to the users page once its permission is granted', () => {
    permissions = ['User.View']
    renderShell()

    // Task 11 shipped the Users page: the nav item is now a real link, not the inert
    // placeholder button this test used to assert.
    expect(screen.getByRole('link', { name: i18n.t('nav.users') })).toHaveAttribute(
      'href',
      '/users',
    )
  })

  it('renders the screen it wraps', () => {
    renderShell()

    expect(screen.getByText('canvas')).toBeInTheDocument()
  })

  it('says how many results are shown out of the true total', async () => {
    renderShell({
      query: 'محمد',
      results: [
        { id: 'a', name: 'محمد', meta: 'داوود ‹ سلمان' },
        { id: 'b', name: 'محمد', meta: 'داوود ‹ علي' },
      ],
      resultTotal: 39,
    })

    // The old label reported results.length and would have said "2".
    expect(await screen.findByText(/39/)).toBeInTheDocument()
  })

  it('reports a plain count when the page holds every match', async () => {
    renderShell({
      query: 'خالد',
      results: [{ id: 'a', name: 'خالد', meta: 'داوود ‹ سلمان' }],
      resultTotal: 1,
    })

    // ar.json tree.resultCount_one — Arabic's singular category carries no digit at all.
    expect(await screen.findByText('نتيجة واحدة')).toBeInTheDocument()
  })

  it('shows a searching state rather than "no results" while a request is in flight', async () => {
    renderShell({ query: 'محمد', results: [], resultTotal: 0, isSearching: true })

    expect(await screen.findByText('جارٍ البحث…')).toBeInTheDocument()
    // ar.json tree.noResults, copied exactly: a negative assertion against a string that does
    // not exist in the bundle passes whether or not the code is correct.
    expect(screen.queryByText('لا توجد نتائج مطابقة.')).not.toBeInTheDocument()
  })

  it('does not claim "no results" while below the minimum query length', async () => {
    renderShell({ query: 'م', results: [], resultTotal: 0, belowThreshold: true })

    expect(await screen.findByText('أكمل الكتابة للبحث…')).toBeInTheDocument()
    expect(screen.queryByText('لا توجد نتائج مطابقة.')).not.toBeInTheDocument()
  })

  it('does not show a zero count while below the minimum query length', async () => {
    renderShell({ query: 'م', results: [], resultTotal: 0, belowThreshold: true })

    await screen.findByText('أكمل الكتابة للبحث…')
    expect(screen.queryByText('لا نتائج')).not.toBeInTheDocument()
  })

  it('does not show a zero count while a request is in flight', async () => {
    renderShell({ query: 'محمد', results: [], resultTotal: 0, isSearching: true })

    await screen.findByText('جارٍ البحث…')
    expect(screen.queryByText('لا نتائج')).not.toBeInTheDocument()
  })

  it('reports a plain count when the page holds exactly every match at the page size', async () => {
    const results = Array.from({ length: 8 }, (_, index) => ({
      id: `m${index}`,
      name: 'محمد',
      meta: 'داوود ‹ سلمان',
    }))
    renderShell({ query: 'محمد', results, resultTotal: 8 })

    expect(await screen.findByText('8 نتائج')).toBeInTheDocument()
    expect(screen.queryByText(/عرض/)).not.toBeInTheDocument()
  })

  describe('search results popover', () => {
    const renderSearching = (onQueryChange = vi.fn()) => {
      renderShell({
        statLine: '14',
        query: 'سليمان',
        results: [{ id: 's1', name: 'سليمان', meta: 'الجيل 1' }],
        onQueryChange,
        onSelectResult: vi.fn(),
      })
      return onQueryChange
    }

    it('clears the search when Escape is pressed inside the search box', async () => {
      const user = userEvent.setup()
      const onQueryChange = renderSearching()

      await user.click(screen.getByLabelText(i18n.t('tree.searchPlaceholder')))
      await user.keyboard('{Escape}')

      expect(onQueryChange).toHaveBeenCalledWith('')
    })

    it('leaves Escape alone when focus is elsewhere, so the top layer keeps it', async () => {
      const user = userEvent.setup()
      const onQueryChange = renderSearching()

      // A modal above the shell owns Escape while it is open. A document-wide handler here
      // would swallow that press and the modal would need a second one to close.
      await user.click(screen.getByText('canvas'))
      await user.keyboard('{Escape}')

      expect(onQueryChange).not.toHaveBeenCalled()
    })

    it('puts the results list down on an outside click but keeps the query', async () => {
      const user = userEvent.setup()
      const onQueryChange = renderSearching()

      expect(screen.getByText('سليمان')).toBeInTheDocument()
      await user.click(screen.getByText('canvas'))

      // The matches stay highlighted on the canvas — search dims, it does not filter.
      expect(screen.queryByText('سليمان')).not.toBeInTheDocument()
      expect(onQueryChange).not.toHaveBeenCalled()
    })
  })
})
