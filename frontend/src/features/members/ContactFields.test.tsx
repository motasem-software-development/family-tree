import { useState } from 'react'
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

/**
 * A real parent, unlike `renderFields`' spy: it feeds each change back in as the next value.
 * Needed for anything that spans more than one interaction, because the fields are controlled
 * and a spy parent throws every keystroke away.
 */
const ControlledFields = () => {
  const [value, setValue] = useState<ContactDetails>(EMPTY_CONTACT_DETAILS)
  return (
    <>
      <ContactFields
        idPrefix="member"
        value={value}
        countries={countries}
        onChange={setValue}
        labelStyle={{}}
        controlStyle={{}}
      />
      <output data-testid="mobile">{value.mobileNumber ?? 'null'}</output>
    </>
  )
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

  it('keeps a dialing code chosen before the number is typed', async () => {
    // The natural order is to pick the code and then type. Composing an empty local number
    // yields null, so nothing in the saved value remembers the choice — the picker has to.
    render(<ControlledFields />)

    const [dial] = screen.getAllByRole('combobox', { name: i18n.t('members.dialCode') })
    await userEvent.click(dial)
    await userEvent.type(dial, 'egypt')
    await userEvent.click(screen.getByRole('option', { name: /\+20/ }))

    // The picker shows the option's own label, flag and country name included.
    expect((dial as HTMLInputElement).value).toContain('+20')
  })

  it('composes a number typed after the dialing code was chosen', async () => {
    render(<ControlledFields />)

    const [dial] = screen.getAllByRole('combobox', { name: i18n.t('members.dialCode') })
    await userEvent.click(dial)
    await userEvent.type(dial, 'egypt')
    await userEvent.click(screen.getByRole('option', { name: /\+20/ }))

    const [localInput] = screen.getAllByLabelText(i18n.t('members.localNumber'))
    await userEvent.type(localInput, '1001234567')

    expect(screen.getByTestId('mobile')).toHaveTextContent('+201001234567')
  })

  it('finds a country by its English name while the app is in Arabic', async () => {
    const onChange = renderFields()

    const country = screen.getByRole('combobox', { name: i18n.t('members.country') })
    await userEvent.click(country)
    await userEvent.type(country, 'palest')
    await userEvent.click(screen.getByRole('option', { name: /فلسطين/ }))

    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ countryId: 1 }))
  })

  it('clears the country through the empty row', async () => {
    const onChange = renderFields({ ...EMPTY_CONTACT_DETAILS, countryId: 1 })

    await userEvent.click(screen.getByRole('combobox', { name: i18n.t('members.country') }))
    await userEvent.click(screen.getByRole('option', { name: i18n.t('members.noCountry') }))

    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ countryId: null }))
  })

  it('finds a dialing code by the country name and applies it to the number', async () => {
    const onChange = renderFields({ ...EMPTY_CONTACT_DETAILS, mobileNumber: '+970599123456' })

    const [dial] = screen.getAllByRole('combobox', { name: i18n.t('members.dialCode') })
    await userEvent.click(dial)
    await userEvent.type(dial, 'egypt')
    await userEvent.click(screen.getByRole('option', { name: /\+20/ }))

    // The local number survives the switch; only the code in front of it changes.
    expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ mobileNumber: '+20599123456' }),
    )
  })
})
