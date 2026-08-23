import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../i18n'
import { AppShell } from './AppShell'

vi.mock('../features/auth/AuthContext', () => ({
  useAuth: () => ({
    user: { email: 'admin@example.com' },
    hasPermission: () => false,
    logout: vi.fn(),
  }),
}))

/**
 * jsdom implements no matchMedia at all, which is why `useIsCompact` degrades to `false` and the
 * sibling AppShell.test.tsx keeps exercising the wide layout unchanged. The compact layout has to
 * be asked for explicitly, so this file installs a stub that answers the query either way.
 */
const stubMatchMedia = (matches: boolean) => {
  window.matchMedia = ((query: string) => ({
    matches,
    media: query,
    onchange: null,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
    dispatchEvent: () => false,
  })) as unknown as typeof window.matchMedia
}

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

describe('AppShell below the compact breakpoint', () => {
  beforeEach(() => stubMatchMedia(true))
  afterEach(() => {
    Reflect.deleteProperty(window, 'matchMedia')
  })

  it('folds the sidebar away so the screen it wraps gets the whole width', () => {
    renderShell()

    // A 248px column out of a 320px viewport leaves nothing for the canvas, so the navigation
    // stands down until it is asked for.
    expect(screen.queryByRole('link', { name: i18n.t('nav.tree') })).not.toBeInTheDocument()
    expect(screen.getByText('canvas')).toBeInTheDocument()
  })

  it('opens the same navigation from the menu button', async () => {
    const user = userEvent.setup()
    renderShell()

    await user.click(screen.getByRole('button', { name: i18n.t('nav.openMenu') }))

    // Every destination the wide layout offers, from the one <aside> both layouts share.
    expect(screen.getByRole('link', { name: i18n.t('nav.tree') })).toHaveAttribute('href', '/')
    expect(screen.getByRole('link', { name: i18n.t('nav.members') })).toHaveAttribute(
      'href',
      '/members',
    )
  })

  it('closes the drawer on Escape, so the keyboard is never trapped behind it', async () => {
    const user = userEvent.setup()
    renderShell()

    await user.click(screen.getByRole('button', { name: i18n.t('nav.openMenu') }))
    expect(screen.getByRole('link', { name: i18n.t('nav.tree') })).toBeInTheDocument()

    await user.keyboard('{Escape}')
    expect(screen.queryByRole('link', { name: i18n.t('nav.tree') })).not.toBeInTheDocument()
  })

  it('states whether the menu is open, since the button is the only cue that it exists', async () => {
    const user = userEvent.setup()
    renderShell()

    const button = screen.getByRole('button', { name: i18n.t('nav.openMenu') })
    expect(button).toHaveAttribute('aria-expanded', 'false')

    // Asserted on the same node, not by name: while the drawer is open two controls close it
    // — this toggle and the panel's own dismiss — and only the toggle carries the state.
    await user.click(button)
    expect(button).toHaveAttribute('aria-expanded', 'true')
    expect(button).toHaveAccessibleName(i18n.t('nav.closeMenu'))
  })
})

describe('AppShell above the compact breakpoint', () => {
  beforeEach(() => stubMatchMedia(false))
  afterEach(() => {
    Reflect.deleteProperty(window, 'matchMedia')
  })

  it('keeps the sidebar in flow, with no menu button to press', () => {
    renderShell()

    expect(screen.getByRole('link', { name: i18n.t('nav.tree') })).toHaveAttribute('href', '/')
    expect(screen.queryByRole('button', { name: i18n.t('nav.openMenu') })).not.toBeInTheDocument()
  })
})
