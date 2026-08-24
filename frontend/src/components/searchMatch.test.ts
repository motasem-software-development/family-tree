import { describe, expect, it } from 'vitest'
import { fold, matches } from './searchMatch'

describe('fold', () => {
  it('strips Latin accents so an unaccented query still reaches the row', () => {
    expect(fold('Türkiye')).toBe('turkiye')
    expect(fold('Côte d’Ivoire')).toBe('cotedivoire')
    expect(fold('Åland Islands')).toBe('alandislands')
  })

  it('folds the Arabic spellings people disagree about', () => {
    expect(fold('الأردن')).toBe(fold('الاردن'))
    expect(fold('سوريّا')).toBe(fold('سوريا'))
    expect(fold('عُمان')).toBe(fold('عمان'))
  })

  it('reads Arabic-Indic digits as digits', () => {
    expect(fold('٩٧٠')).toBe('970')
  })

  it('discards punctuation so "+970" and "970" are one query', () => {
    expect(fold('+970')).toBe('970')
  })
})

describe('matches', () => {
  it('finds a country by either language', () => {
    expect(matches('pales', ['فلسطين', 'Palestine', 'PS'])).toBe(true)
    expect(matches('فلسط', ['فلسطين', 'Palestine', 'PS'])).toBe(true)
  })

  it('finds a country by its ISO code', () => {
    expect(matches('ps', ['فلسطين', 'Palestine', 'PS'])).toBe(true)
  })

  it('finds a dialing code typed with or without the plus', () => {
    expect(matches('+970', ['فلسطين', 'Palestine', 'PS', '+970'])).toBe(true)
    expect(matches('970', ['فلسطين', 'Palestine', 'PS', '+970'])).toBe(true)
  })

  it('matches in the middle of a name, not just at the start', () => {
    expect(matches('emirates', ['United Arab Emirates'])).toBe(true)
  })

  it('rejects a query that appears nowhere', () => {
    expect(matches('japan', ['فلسطين', 'Palestine', 'PS'])).toBe(false)
  })

  it('matches everything while the query is empty', () => {
    expect(matches('', ['anything'])).toBe(true)
    expect(matches('   ', ['anything'])).toBe(true)
  })
})
