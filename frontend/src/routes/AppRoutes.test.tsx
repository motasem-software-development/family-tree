import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { I18nextProvider } from 'react-i18next'
import { describe, expect, it } from 'vitest'
import i18n from '../i18n'
import { HomePage } from './AppRoutes'

describe('HomePage', () => {
  it('renders a link to the members screen', () => {
    render(
      <I18nextProvider i18n={i18n}>
        <MemoryRouter>
          <HomePage />
        </MemoryRouter>
      </I18nextProvider>,
    )

    const link = screen.getByRole('link', { name: i18n.t('members.title') })
    expect(link).toHaveAttribute('href', '/members')
  })
})
