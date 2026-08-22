import { describe, expect, it } from 'vitest'
import { lifeDetailsOf, lifeYears } from './lifeDetails'
import type { FamilyMember } from './types'

/**
 * An API response predating the life-details columns: the three fields are absent, so they
 * arrive as `undefined` rather than null. TypeScript's `string | null` says this cannot happen;
 * the wire disagrees whenever the frontend is deployed ahead of the API.
 */
const legacyMember = {
  id: 'a',
  name: 'سليمان',
  parentId: null,
  version: 1,
  createdAt: '2026-08-16T12:00:00Z',
  updatedAt: '2026-08-16T12:00:00Z',
} as unknown as FamilyMember

describe('life details from an API response missing the fields', () => {
  it('normalizes absent dates to null rather than leaving them undefined', () => {
    const life = lifeDetailsOf(legacyMember)

    expect(life.dateOfBirth).toBeNull()
    expect(life.dateOfDeath).toBeNull()
    expect(life.isDeceased).toBe(false)
  })

  it('does not throw when formatting life years', () => {
    // The crash this guards: `undefined === null` is false, so an absent date reached
    // `iso.slice(0, 4)` and took the whole view down with it after login.
    expect(() => lifeYears(lifeDetailsOf(legacyMember), 'ar')).not.toThrow()
    expect(lifeYears(lifeDetailsOf(legacyMember), 'ar')).toBeNull()
  })
})
