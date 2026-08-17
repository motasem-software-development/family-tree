import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../i18n'
import { AppShell } from './AppShell'
import type { FamilyTreeNode } from '../features/members/types'

let permissions: string[] = []

vi.mock('../features/auth/AuthContext', () => ({
  useAuth: () => ({
    user: { email: 'admin@example.com' },
    hasPermission: (permission: string) => permissions.includes(permission),
    logout: vi.fn(),
  }),
}))

const renderShell = () =>
  render(
    <I18nextProvider i18n={i18n}>
      <MemoryRouter>
        <AppShell familyName="عائلة السقا" statLine="4 · 3">
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

  it('shows a permitted destination, but disabled until its phase ships', () => {
    permissions = ['User.View']
    renderShell()

    // Visible so the operator knows it exists; inert so it cannot lead nowhere.
    expect(screen.getByRole('button', { name: i18n.t('nav.users') })).toBeDisabled()
  })

  it('renders the screen it wraps', () => {
    renderShell()

    expect(screen.getByText('canvas')).toBeInTheDocument()
  })

  describe('search results popover', () => {
    const node: FamilyTreeNode = {
      id: 's1',
      name: 'سليمان',
      parentId: null,
      generation: 1,
      hasMoreChildren: false,
      children: [],
    }

    const renderSearching = (onQueryChange = vi.fn()) => {
      render(
        <I18nextProvider i18n={i18n}>
          <MemoryRouter>
            <AppShell
              familyName="عائلة السقا"
              statLine="14"
              query="سليمان"
              results={[{ node, meta: 'الجيل 1' }]}
              onQueryChange={onQueryChange}
              onSelectResult={vi.fn()}
            >
              <p>canvas</p>
            </AppShell>
          </MemoryRouter>
        </I18nextProvider>,
      )
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
