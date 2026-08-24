import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import i18n from '../../i18n'
import { ContactFields } from './ContactFields'
import { EMPTY_CONTACT_DETAILS, type ContactDetails } from './contactDetails'
import type { Country } from '../countries/types'

const countries: Country[] = [
  { id: 1, code: 'PS', nameAr: 'فلسطين', nameEn: 'Palestine', dialCode: '+970' },
  { id: 2, code: 'EG', nameAr: 'مصر', nameEn: 'Egypt', dialCode: '+20' },
]

const renderFields = (value: ContactDetails = EMPTY_CONTACT_DETAILS) => {
  const onChange = vi.fn()
  render(
    <ContactFields
      idPrefix="member"
      value={value}
      countries={countries}
      onChange={onChange}
      labelStyle={{}}
      controlStyle={{}}
    />,
  )
  return onChange
}

// The app's default language is Arabic (see src/i18n/index.ts), so assertions go through
// i18n.t() rather than hardcoded English strings — the same pattern MembersPage.test.tsx uses.
describe('ContactFields', () => {
  it('composes the dial code and the local number into one E.164 value', async () => {
    // Seeded with a mobile number that already carries a dial code, so typing into the local
    // number field has something real to compose against. The parent is a spy, so the
    // controlled value never persists between keystrokes — each keystroke recomputes from the
    // ORIGINAL prop value, not the previous keystroke's result. A single appended digit is
    // therefore the only deterministic thing to assert: it proves the split (dial code vs.
    // local) and the join (local vs. the typed digit) both wire through correctly.
    const onChange = renderFields({
      ...EMPTY_CONTACT_DETAILS,
      mobileNumber: '+970599123456',
    })

    const [localInput] = screen.getAllByLabelText(i18n.t('members.localNumber'))
    await userEvent.type(localInput, '9')

    expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ mobileNumber: '+9705991234569' }),
    )
  })

  it('flags a national ID that is not nine digits', async () => {
    renderFields({ ...EMPTY_CONTACT_DETAILS, nationalId: '12345' })

    expect(screen.getByText(i18n.t('members.nationalIdInvalid'))).toBeInTheDocument()
  })

  it('does not complain about a nine digit national ID', () => {
    renderFields({ ...EMPTY_CONTACT_DETAILS, nationalId: '123456789' })

    expect(screen.queryByText(i18n.t('members.nationalIdInvalid'))).not.toBeInTheDocument()
  })

  it('does not complain about an empty national ID', () => {
    renderFields()

    expect(screen.queryByText(i18n.t('members.nationalIdInvalid'))).not.toBeInTheDocument()
  })

  it('copies the mobile number to WhatsApp when "same as mobile" is ticked', async () => {
    const onChange = renderFields({
      ...EMPTY_CONTACT_DETAILS,
      mobileNumber: '+970599123456',
    })

    await userEvent.click(screen.getByLabelText(i18n.t('members.sameAsMobile')))

    expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ whatsAppNumber: '+970599123456' }),
    )
  })

  it('disables the WhatsApp fields while they mirror the mobile', () => {
    renderFields({
      ...EMPTY_CONTACT_DETAILS,
      mobileNumber: '+970599123456',
      whatsAppNumber: '+970599123456',
    })

    const [, whatsAppNumberInput] = screen.getAllByLabelText(i18n.t('members.localNumber'))
    expect(whatsAppNumberInput).toBeDisabled()
  })
})
