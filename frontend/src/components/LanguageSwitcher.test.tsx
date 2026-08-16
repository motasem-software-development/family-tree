import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import { I18nextProvider } from 'react-i18next'
import i18n from '../i18n'
import { LanguageSwitcher } from './LanguageSwitcher'

const renderSwitcher = () =>
  render(
    <I18nextProvider i18n={i18n}>
      <LanguageSwitcher />
    </I18nextProvider>,
  )

describe('LanguageSwitcher', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('ar')
  })

  it('offers the other language while Arabic is active', () => {
    renderSwitcher()
    expect(screen.getByRole('button', { name: 'English' })).toBeInTheDocument()
  })

  it('switches the active language and the document direction', async () => {
    renderSwitcher()

    await userEvent.click(screen.getByRole('button', { name: 'English' }))

    expect(i18n.language).toBe('en')
    expect(document.documentElement.dir).toBe('ltr')
    expect(screen.getByRole('button', { name: 'العربية' })).toBeInTheDocument()
  })
})
