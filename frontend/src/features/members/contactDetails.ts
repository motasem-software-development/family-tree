import type { Country } from '../countries/types'

/**
 * The editable contact facts of a member, in the shape the form holds them. Mirrors
 * `LifeDetails`, including its replace-semantics: a null field clears the stored value.
 */
export interface ContactDetails {
  nationalId: string | null
  mobileNumber: string | null
  whatsAppNumber: string | null
  countryId: number | null
}

export const EMPTY_CONTACT_DETAILS: ContactDetails = {
  nationalId: null,
  mobileNumber: null,
  whatsAppNumber: null,
  countryId: null,
}

/**
 * The single normalization point between an API response and the rest of the UI, for exactly
 * the reason `lifeDetailsOf` documents: an API deployed a step behind the frontend omits these
 * fields entirely and they arrive as `undefined`, which is not `null` and would reach the
 * inputs as an uncontrolled-component warning at best.
 */
export const contactDetailsOf = (member: {
  nationalId?: string | null
  mobileNumber?: string | null
  whatsAppNumber?: string | null
  countryId?: number | null
}): ContactDetails => ({
  nationalId: member.nationalId ?? null,
  mobileNumber: member.mobileNumber ?? null,
  whatsAppNumber: member.whatsAppNumber ?? null,
  countryId: member.countryId ?? null,
})

const SEPARATORS = /[\s\-()]/g

/** Mirrors the server's `^[0-9]{9}$`. Empty is valid: the field is optional. */
export const isValidNationalId = (value: string): boolean =>
  value === '' || /^[0-9]{9}$/.test(value)

/**
 * Splits a stored E.164 number into the dial code the picker should show and the local part.
 *
 * Longest match wins: dial codes are not prefix-free (+1 vs +1-something in fuller lists), and
 * picking a shorter prefix would leave a stray digit at the front of the local number. An
 * unrecognised code falls back to showing the whole number, so a member whose country was never
 * seeded is still editable rather than silently truncated.
 */
export const splitPhone = (
  e164: string | null,
  countries: readonly Country[],
): { dialCode: string; local: string } => {
  if (e164 === null || e164 === '') return { dialCode: '', local: '' }

  const match = countries
    .map((country) => country.dialCode)
    .filter((dialCode) => e164.startsWith(dialCode))
    .sort((a, b) => b.length - a.length)[0]

  if (match === undefined) return { dialCode: '', local: e164 }

  return { dialCode: match, local: e164.slice(match.length) }
}

/**
 * Composes the picker's dial code and the typed local number into one E.164 string —
 * specification §5.2's "the system combines the country dialing code and local number and
 * stores +970599123456".
 *
 * The leading trunk zero is dropped: people write their number the way they dial it at home,
 * and '+9700599123456' is not a number anyone can reach.
 *
 * A number that already carries its own country code — '+970599850444', or the '00970…' form
 * people dial from abroad — is taken as the whole value and the picker's code is ignored.
 * Typing or pasting a full international number into the digits box is how most people enter
 * one; composing it with the code beside it produced '+970+970599850444', which the server
 * rejects as malformed. `splitPhone` puts the picker back in step on the next render.
 */
export const joinPhone = (dialCode: string, local: string): string | null => {
  const cleaned = local.replace(SEPARATORS, '')

  if (cleaned.startsWith('+')) return cleaned
  // Returned even when nothing follows the '00' yet: the '+' has to survive in the field, or
  // the user cannot type the rest of the number after it.
  if (cleaned.startsWith('00')) return `+${cleaned.slice(2)}`

  const digits = cleaned.replace(/^0+/, '')
  if (digits === '' || dialCode === '') return null

  return `${dialCode}${digits}`
}
