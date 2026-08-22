import { describe, expect, it } from 'vitest'
import {
  EMPTY_LIFE_DETAILS,
  fromDateInput,
  lifeYears,
  withDeathDate,
  withDeceased,
  type LifeDetails,
} from './lifeDetails'

const life = (over: Partial<LifeDetails> = {}): LifeDetails => ({ ...EMPTY_LIFE_DETAILS, ...over })

describe('fromDateInput', () => {
  it('turns a cleared date field into null rather than an empty string', () => {
    // The API takes null for "unknown"; '' would be a malformed date.
    expect(fromDateInput('')).toBeNull()
    expect(fromDateInput('1920-03-14')).toBe('1920-03-14')
  })
})

describe('withDeathDate', () => {
  it('marks the member deceased when a death date is entered', () => {
    // Mirrors the server's coercion, so the checkbox never contradicts what will be saved.
    expect(withDeathDate(life(), '1995-11-02').isDeceased).toBe(true)
  })

  it('leaves an already-deceased member deceased when the date is cleared', () => {
    // "Died, date unknown" is the case the explicit flag exists for.
    const next = withDeathDate(life({ isDeceased: true, dateOfDeath: '1995-11-02' }), null)

    expect(next.dateOfDeath).toBeNull()
    expect(next.isDeceased).toBe(true)
  })

  it('does not mutate the value it is given', () => {
    const original = life()

    withDeathDate(original, '1995-11-02')

    expect(original).toEqual(EMPTY_LIFE_DETAILS)
  })
})

describe('withDeceased', () => {
  it('clears the death date when the member is marked living again', () => {
    // Correcting a mistaken death record must actually clear it, not leave a date behind that
    // the server would use to flip the flag straight back.
    const next = withDeceased(life({ isDeceased: true, dateOfDeath: '1995-11-02' }), false)

    expect(next.isDeceased).toBe(false)
    expect(next.dateOfDeath).toBeNull()
  })
})

describe('lifeYears', () => {
  it('renders the genealogy convention for a member with both dates', () => {
    expect(lifeYears(life({ dateOfBirth: '1920-03-14', dateOfDeath: '1995-11-02', isDeceased: true }), 'en'))
      .toBe('1920–1995')
  })

  it('renders a bare birth year for a living member', () => {
    // No trailing dash: "1920–" would read as an unfinished range rather than a birth year.
    expect(lifeYears(life({ dateOfBirth: '1920-03-14' }), 'en')).toBe('1920')
  })

  it('renders an open range for a deceased member whose death year is unknown', () => {
    expect(lifeYears(life({ dateOfBirth: '1920-03-14', isDeceased: true }), 'en')).toBe('1920–')
  })

  it('renders a death year alone when the birth year is unknown', () => {
    expect(lifeYears(life({ dateOfDeath: '1995-11-02', isDeceased: true }), 'en')).toBe('–1995')
  })

  it('returns null when nothing is known, so the row shows no stray dash', () => {
    // The overwhelmingly common case in the imported tree: names and nothing else.
    expect(lifeYears(EMPTY_LIFE_DETAILS, 'en')).toBeNull()
    expect(lifeYears(life({ isDeceased: true }), 'en')).toBeNull()
  })

  it('formats the year in the active locale without a thousands separator', () => {
    // A year is not a quantity: the default grouping would render 1920 as "1,920". The digits
    // themselves follow the locale's numbering system, exactly as the panel's dates do.
    const expected = (1920).toLocaleString('ar', { useGrouping: false })

    expect(lifeYears(life({ dateOfBirth: '1920-03-14' }), 'ar')).toBe(expected)
    expect(lifeYears(life({ dateOfBirth: '1920-03-14' }), 'en')).toBe('1920')
  })
})
