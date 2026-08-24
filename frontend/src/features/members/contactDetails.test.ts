import { describe, expect, it } from 'vitest'
import {
  EMPTY_CONTACT_DETAILS,
  contactDetailsOf,
  isValidNationalId,
  joinPhone,
  splitPhone,
} from './contactDetails'
import type { Country } from '../countries/types'

const countries: Country[] = [
  { id: 1, code: 'PS', nameAr: 'فلسطين', nameEn: 'Palestine', dialCode: '+970' },
  { id: 2, code: 'EG', nameAr: 'مصر', nameEn: 'Egypt', dialCode: '+20' },
  { id: 3, code: 'GB', nameAr: 'المملكة المتحدة', nameEn: 'United Kingdom', dialCode: '+44' },
]

describe('contactDetailsOf', () => {
  it('normalizes absent fields to null', () => {
    // An API a deploy behind omits these entirely; `undefined` must not reach the inputs.
    expect(contactDetailsOf({})).toEqual(EMPTY_CONTACT_DETAILS)
  })

  it('reads the fields when present', () => {
    expect(
      contactDetailsOf({
        nationalId: '123456789',
        mobileNumber: '+970599123456',
        whatsAppNumber: null,
        countryId: 1,
      }),
    ).toEqual({
      nationalId: '123456789',
      mobileNumber: '+970599123456',
      whatsAppNumber: null,
      countryId: 1,
    })
  })
})

describe('splitPhone', () => {
  it('splits a stored number into its dial code and the rest', () => {
    expect(splitPhone('+970599123456', countries)).toEqual({
      dialCode: '+970',
      local: '599123456',
    })
  })

  it('prefers the longest matching dial code', () => {
    expect(splitPhone('+201012345678', countries)).toEqual({
      dialCode: '+20',
      local: '1012345678',
    })
  })

  it('returns the whole number as local when no dial code matches', () => {
    expect(splitPhone('+998901234567', countries)).toEqual({
      dialCode: '',
      local: '+998901234567',
    })
  })

  it('handles an empty number', () => {
    expect(splitPhone(null, countries)).toEqual({ dialCode: '', local: '' })
  })
})

describe('joinPhone', () => {
  it('concatenates the dial code and the local number', () => {
    expect(joinPhone('+970', '599123456')).toBe('+970599123456')
  })

  it('strips separators from the local number', () => {
    expect(joinPhone('+970', '599 123-456')).toBe('+970599123456')
  })

  it('drops a leading zero from the local number', () => {
    // People write their number as they dial it domestically; the trunk zero has no place
    // in E.164 and leaving it in produces a number that cannot be called.
    expect(joinPhone('+970', '0599123456')).toBe('+970599123456')
  })

  it('returns null when the local number is empty', () => {
    expect(joinPhone('+970', '')).toBeNull()
    expect(joinPhone('+970', '   ')).toBeNull()
  })

  it('returns null when no dial code is chosen', () => {
    expect(joinPhone('', '599123456')).toBeNull()
  })
})

describe('isValidNationalId', () => {
  it('accepts exactly nine digits', () => {
    expect(isValidNationalId('123456789')).toBe(true)
    expect(isValidNationalId('012345678')).toBe(true)
  })

  it('accepts an empty value, which means "not recorded"', () => {
    expect(isValidNationalId('')).toBe(true)
  })

  it('rejects anything else', () => {
    expect(isValidNationalId('12345678')).toBe(false)
    expect(isValidNationalId('1234567890')).toBe(false)
    expect(isValidNationalId('12345ABC9')).toBe(false)
  })
})
