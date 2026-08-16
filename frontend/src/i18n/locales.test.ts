import { describe, expect, it } from 'vitest'
import ar from './locales/ar.json'
import en from './locales/en.json'

const flatten = (obj: Record<string, unknown>, prefix = ''): string[] =>
  Object.entries(obj).flatMap(([key, value]) =>
    typeof value === 'object' && value !== null
      ? flatten(value as Record<string, unknown>, `${prefix}${key}.`)
      : [`${prefix}${key}`],
  )

const flattenEntries = (obj: Record<string, unknown>, prefix = ''): [string, unknown][] =>
  Object.entries(obj).flatMap(([key, value]) =>
    typeof value === 'object' && value !== null
      ? flattenEntries(value as Record<string, unknown>, `${prefix}${key}.`)
      : [[`${prefix}${key}`, value]],
  )

describe('locale resources', () => {
  it('define exactly the same keys in both languages', () => {
    // A key present in one language and missing in the other renders as a raw key
    // in production. Catching it here is far cheaper than catching it in review.
    expect(flatten(ar).sort()).toEqual(flatten(en).sort())
  })

  it('leave no value blank', () => {
    // Uses the same flatten traversal as the key-parity test above so that a
    // blank value nested at any depth (e.g. auth.signIn, errors.NETWORK) is
    // caught, not just blanks at the top level.
    const blanks = [
      ...flattenEntries(ar).filter(([, v]) => v === ''),
      ...flattenEntries(en).filter(([, v]) => v === ''),
    ]
    expect(blanks).toHaveLength(0)
  })
})
