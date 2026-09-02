
/**
 * The editable life facts of a member, in the shape the form holds them: ISO `yyyy-MM-dd`
 * strings, because that is what `<input type="date">` reads and writes and what the API
 * expects on the wire. Gregorian throughout — the API stores a plain calendar date.
 */
export interface LifeDetails {
  dateOfBirth: string | null
  dateOfDeath: string | null
  /**
   * Deliberately independent of `dateOfDeath`. Genealogy records routinely establish that
   * someone has died while the date itself is lost, so deriving the status from the date would
   * quietly show every such ancestor as living.
   */
  isDeceased: boolean
}

export const EMPTY_LIFE_DETAILS: LifeDetails = {
  dateOfBirth: null,
  dateOfDeath: null,
  isDeceased: false,
}

/**
 * The single normalization point between an API response and the rest of the UI. The parameter
 * is deliberately typed loose: `FamilyMember` promises `string | null`, but an API deployed a
 * step behind the frontend omits these fields entirely and they arrive as `undefined`. That is
 * not hypothetical — it took the tree down after login on the first production deploy, because
 * `undefined === null` is false and an absent date reached `iso.slice(0, 4)`.
 *
 * Everything that reads life details goes through here, so the rest of the code can rely on
 * the three fields being exactly null / null / boolean.
 */
export const lifeDetailsOf = (member: {
  dateOfBirth?: string | null
  dateOfDeath?: string | null
  isDeceased?: boolean | null
}): LifeDetails => ({
  dateOfBirth: member.dateOfBirth ?? null,
  dateOfDeath: member.dateOfDeath ?? null,
  isDeceased: member.isDeceased === true,
})

/** `<input type="date">` yields '' for an empty field; the API wants null. */
export const dateInputValue = (iso: string | null): string => iso ?? ''
export const fromDateInput = (value: string): string | null => (value === '' ? null : value)

/**
 * Mirrors the server's coercion (`FamilyMember.ValidateLifeDetails`): entering a death date
 * states the fact of death, and clearing the deceased flag clears the date that contradicts it.
 * Applied in the form so the checkbox never visibly disagrees with what will be saved.
 */
export const withDeathDate = (life: LifeDetails, dateOfDeath: string | null): LifeDetails => ({
  ...life,
  dateOfDeath,
  isDeceased: dateOfDeath !== null ? true : life.isDeceased,
})

export const withDeceased = (life: LifeDetails, isDeceased: boolean): LifeDetails => ({
  ...life,
  isDeceased,
  dateOfDeath: isDeceased ? life.dateOfDeath : null,
})

/**
 * The year of an ISO date, in the active locale's numbering system — the same treatment the
 * detail panel's dates get, so the two never disagree within one view. `useGrouping: false` is
 * load-bearing: a year is not a quantity, and the default would render 1920 as "1,920".
 */
const year = (iso: string, locale: string): string =>
  Number(iso.slice(0, 4)).toLocaleString(locale, { useGrouping: false })

/**
 * The genealogy convention — `1920–1995`, `1920–` for a living member with a known birth,
 * `–1995` when only the death year survives. Null when no date is known at all, which is the
 * common case in the imported tree and must render as nothing rather than as a bare dash.
 */
export const lifeYears = (life: LifeDetails, locale: string): string | null => {
  // Nullish, not `=== null`: normalization upstream should make undefined impossible, but this
  // formats data that came off the wire and must not be the thing that takes a page down.
  const born = life.dateOfBirth == null ? '' : year(life.dateOfBirth, locale)
  const died = life.dateOfDeath == null ? '' : year(life.dateOfDeath, locale)

  if (born === '' && died === '') return null
  // An en dash, and no trailing one for a member who is simply still alive with no death date
  // recorded: "1920–" reads as "born 1920, still living", which is exactly right.
  return life.isDeceased === true || died !== '' ? `${born}–${died}` : born
}

/**
 * Whole years lived: to the death date where there is one, to `today` otherwise — the same rule
 * the Excel export applies server-side, so the age in a downloaded workbook matches the age on
 * the screen it was exported from.
 *
 * Null, meaning a blank cell, in the two cases where no honest number exists: no birth date at
 * all, and a member marked deceased whose death date was never recorded. Measuring that second
 * one against today would report an age they never reached.
 */
export const ageYears = (life: LifeDetails, today: Date): number | null => {
  const born = parseIsoDate(life.dateOfBirth)
  if (born === null) return null

  const died = parseIsoDate(life.dateOfDeath)
  if (died !== null) return wholeYearsBetween(born, died)

  return life.isDeceased === true
    ? null
    : wholeYearsBetween(born, { year: today.getFullYear(), month: today.getMonth() + 1, day: today.getDate() })
}

interface CalendarDate {
  year: number
  month: number
  day: number
}

/**
 * The date parts of an ISO `yyyy-MM-dd`, without going through `new Date`. Deliberate: `new
 * Date('2013-04-28')` is parsed as UTC midnight and then read back in local time, so west of
 * UTC every birth date lands a day early. A calendar date has no time zone and must not acquire
 * one on its way to the screen.
 */
const parseIsoDate = (iso: string | null | undefined): CalendarDate | null => {
  if (iso == null) return null
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso)
  if (match === null) return null
  return { year: Number(match[1]), month: Number(match[2]), day: Number(match[3]) }
}

/** Decremented when the anniversary has not yet come round in the target year. */
const wholeYearsBetween = (from: CalendarDate, to: CalendarDate): number => {
  const years = to.year - from.year
  const beforeAnniversary = to.month < from.month || (to.month === from.month && to.day < from.day)
  return beforeAnniversary ? years - 1 : years
}

/**
 * The locale's ten digits, indexed 0-9. Arabic renders Arabic-Indic numerals per the design
 * system's numeral rule, and a digit table is the only way to get them onto a zero-padded field:
 * `(2).toLocaleString('ar')` is "٢", never "٠٢", so padding has to happen before translation.
 */
const digitsOf = (locale: string): string[] =>
  Array.from({ length: 10 }, (_, n) => n.toLocaleString(locale, { useGrouping: false }))

const localiseDigits = (text: string, locale: string): string => {
  const digits = digitsOf(locale)
  // Already Latin: skip the work for the language that needs none.
  if (digits[0] === '0') return text
  return text.replace(/\d/g, (digit) => digits[Number(digit)] ?? digit)
}

/**
 * A birth or death date for a table cell, as `dd/MM/yyyy`.
 *
 * Assembled by hand rather than through `Intl.DateTimeFormat`: the pattern is the same in both
 * languages by request, and Intl will not give that — an `en` locale formats the same date as
 * `03/02/1940`, day and month swapped, which is not a formatting difference but a different
 * date to anyone reading it. Only the digits follow the locale.
 */
export const formatLifeDate = (iso: string | null | undefined, locale: string): string | null => {
  const date = parseIsoDate(iso)
  if (date === null) return null

  const pad = (value: number): string => String(value).padStart(2, '0')
  return localiseDigits(`${pad(date.day)}/${pad(date.month)}/${date.year}`, locale)
}
