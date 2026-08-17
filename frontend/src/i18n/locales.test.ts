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

const PLURAL_SUFFIX = /_(zero|one|two|few|many|other)$/

/** Counted messages are one message per language, split into that language's CLDR categories. */
const baseKeys = (obj: Record<string, unknown>): string[] => [
  ...new Set(flatten(obj).map((key) => key.replace(PLURAL_SUFFIX, ''))),
]

const pluralCategories = (obj: Record<string, unknown>, base: string): string[] =>
  flatten(obj)
    .filter((key) => key.startsWith(`${base}_`))
    .map((key) => key.slice(base.length + 1))
    .sort()

const required = (locale: string): string[] =>
  [...new Intl.PluralRules(locale).resolvedOptions().pluralCategories].sort()

/** Every message that takes a {{count}}. Arabic declines six ways here; English two. */
const COUNTED = [
  'tree.resultCount',
  'tree.membersCount',
  'tree.generationsCount',
  'modal.blockedBody',
]

describe('locale resources', () => {
  it('define exactly the same keys in both languages', () => {
    // A key present in one language and missing in the other renders as a raw key
    // in production. Catching it here is far cheaper than catching it in review.
    // Compared on base keys: a counted message is the same message in both languages
    // even though Arabic splits it into six forms and English into two.
    expect(baseKeys(ar).sort()).toEqual(baseKeys(en).sort())
  })

  it.each(COUNTED)('covers every plural category each language requires for %s', (base) => {
    // "1 results" and "جيل واحد" rendered as "1 أجيال" are the bugs this catches: a
    // counted message shipping only one form reads wrong at every count the language declines.
    expect(pluralCategories(en, base)).toEqual(required('en'))
    expect(pluralCategories(ar, base)).toEqual(required('ar'))
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
