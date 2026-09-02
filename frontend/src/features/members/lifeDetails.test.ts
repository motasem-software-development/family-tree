import { describe, expect, it } from 'vitest'
import {
  EMPTY_LIFE_DETAILS,
  ageYears,
  formatLifeDate,
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


describe('ageYears', () => {
  const today = new Date(2026, 7, 16) // 16 August 2026, local — the parameter is a local date.

  it('measures a living member against today', () => {
    expect(ageYears(life({ dateOfBirth: '1990-08-16' }), today)).toBe(36)
  })

  it('does not count a birthday that has not come round yet', () => {
    expect(ageYears(life({ dateOfBirth: '1990-08-17' }), today)).toBe(35)
  })

  it('measures a deceased member to their death date, not to today', () => {
    // Against today this would read 86 and would grow every time the page is opened.
    const deceased = life({ dateOfBirth: '1940-03-02', dateOfDeath: '2011-03-01', isDeceased: true })

    expect(ageYears(deceased, today)).toBe(70)
  })

  it('has no age for a member known to have died on an unknown date', () => {
    // Measuring against today would report an age they never reached.
    expect(ageYears(life({ dateOfBirth: '1940-01-01', isDeceased: true }), today)).toBeNull()
  })

  it('has no age without a birth date', () => {
    expect(ageYears(life(), today)).toBeNull()
    expect(ageYears(life({ dateOfDeath: '2011-03-01', isDeceased: true }), today)).toBeNull()
  })

  it('survives an undefined date from an API a step behind the frontend', () => {
    expect(ageYears({ dateOfBirth: undefined, dateOfDeath: undefined, isDeceased: false } as unknown as LifeDetails, today)).toBeNull()
  })
})

describe('formatLifeDate', () => {
  it('writes dd/MM/yyyy, zero-padded', () => {
    expect(formatLifeDate('1940-03-02', 'en')).toBe('02/03/1940')
  })

  it('keeps day-before-month in English, which Intl would not', () => {
    // `Intl.DateTimeFormat('en')` renders this same date 03/02/1940 — not a formatting
    // difference but a different date to anyone reading it.
    expect(formatLifeDate('1940-03-02', 'en-US')).toBe('02/03/1940')
  })

  it('renders the calendar date it was given, not one shifted by the reader time zone', () => {
    // The bug this pins: `new Date('2013-04-28')` is UTC midnight, and read back in local time
    // west of UTC it is 27 April. A calendar date has no zone and must not acquire one.
    expect(formatLifeDate('2013-04-28', 'en')).toBe('28/04/2013')
  })

  it('follows the locale numbering system, padding included', () => {
    // ar-EG selects Arabic-Indic digits where a bare 'ar' resolves to Latin ones in this ICU —
    // this pins the digit mapping itself, not whichever default the runtime happens to carry.
    // The padding has to happen before the digits are translated: (2).toLocaleString('ar-EG')
    // is '٢', never '٠٢'.
    expect(formatLifeDate('1940-03-02', 'ar-EG')).toBe('٠٢/٠٣/١٩٤٠')
  })

  it('leaves Latin digits alone, which is what a bare ar resolves to here', () => {
    // Not an accident worth hiding: lifeYears already renders 'ar' years with Latin digits, so
    // the two agree on screen.
    expect(formatLifeDate('1940-03-02', 'ar')).toBe('02/03/1940')
  })

  it('is null for an unrecorded date, so the caller picks its own placeholder', () => {
    expect(formatLifeDate(null, 'en')).toBeNull()
    expect(formatLifeDate(undefined, 'en')).toBeNull()
  })
})
