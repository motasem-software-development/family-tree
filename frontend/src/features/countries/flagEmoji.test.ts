import { describe, expect, it } from 'vitest'
import { countryName, flagEmoji } from './flagEmoji'

describe('flagEmoji', () => {
  it('maps an alpha-2 code to its regional indicator pair', () => {
    expect(flagEmoji('PS')).toBe('🇵🇸')
    expect(flagEmoji('EG')).toBe('🇪🇬')
  })

  it('accepts a lowercase code', () => {
    expect(flagEmoji('ps')).toBe('🇵🇸')
  })

  it('returns an empty string for anything that is not two letters', () => {
    // A code the API has never sent must not render as mojibake next to a real flag.
    expect(flagEmoji('PSE')).toBe('')
    expect(flagEmoji('')).toBe('')
    expect(flagEmoji('P1')).toBe('')
  })
})

describe('countryName', () => {
  const palestine = { id: 1, code: 'PS', nameAr: 'فلسطين', nameEn: 'Palestine', dialCode: '+970' }

  it('picks the Arabic name for Arabic', () => {
    expect(countryName(palestine, 'ar')).toBe('فلسطين')
  })

  it('picks the English name for anything else', () => {
    expect(countryName(palestine, 'en')).toBe('Palestine')
  })

  it('matches a regional language tag', () => {
    expect(countryName(palestine, 'ar-PS')).toBe('فلسطين')
  })
})
