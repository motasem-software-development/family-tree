import type { Country } from './types'

/** 'A' → U+1F1E6, the first regional indicator. */
const REGIONAL_INDICATOR_A = 0x1f1e6
const LETTER_A = 'A'.charCodeAt(0)

/**
 * The flag for an alpha-2 code, built from the two regional indicator symbols rather than
 * shipped as an asset — every platform that renders flags at all renders these.
 *
 * Returns '' for anything that is not two ASCII letters. A malformed code must render as
 * nothing rather than as two stray boxes beside a real flag.
 */
export const flagEmoji = (code: string): string => {
  const upper = code.toUpperCase()
  if (!/^[A-Z]{2}$/.test(upper)) return ''

  return String.fromCodePoint(
    ...[...upper].map((letter) => REGIONAL_INDICATOR_A + letter.charCodeAt(0) - LETTER_A),
  )
}

/**
 * The country name in the active language. Both names ride on every row, so switching language
 * never refetches — and `startsWith` rather than equality because i18next reports regional tags
 * like 'ar-PS'.
 */
export const countryName = (country: Country, language: string): string =>
  language.startsWith('ar') ? country.nameAr : country.nameEn
