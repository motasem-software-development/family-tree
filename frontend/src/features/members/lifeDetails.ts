
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
