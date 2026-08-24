import { describe, expect, it } from 'vitest'
import {
  activeFilterCount,
  EMPTY_FILTERS,
  fromSearchParams,
  toFilterParams,
  type MemberFilters,
} from './filterParams'

const roundTrip = (filters: MemberFilters): MemberFilters =>
  fromSearchParams(toFilterParams(filters))

describe('toFilterParams', () => {
  it('emits nothing for empty filters', () => {
    expect(toFilterParams(EMPTY_FILTERS).toString()).toBe('')
  })

  it('does not emit an explicit status=all', () => {
    // An explicit default is a parameter the server has to special-case, and it makes an
    // unfiltered URL look filtered.
    expect(toFilterParams({ status: 'all' }).toString()).toBe('')
  })

  it('drops a blank search rather than sending an empty pattern', () => {
    expect(toFilterParams({ search: '   ' }).toString()).toBe('')
  })

  it('trims a search term', () => {
    expect(toFilterParams({ search: '  فارس  ' }).get('search')).toBe('فارس')
  })

  it('emits generation zero, which is the root', () => {
    // A falsy-check instead of an undefined-check would silently drop the root (design §1.2).
    expect(toFilterParams({ generation: 0 }).get('generation')).toBe('0')
  })

  it('percent-encodes an Arabic search term', () => {
    expect(toFilterParams({ search: 'فارس' }).toString()).toBe('search=%D9%81%D8%A7%D8%B1%D8%B3')
  })
})

describe('fromSearchParams', () => {
  it('reads every field back', () => {
    const filters: MemberFilters = {
      search: 'فارس',
      status: 'deceased',
      branchId: 'b1',
      generation: 2,
      countryId: 165,
      rootId: 'r1',
    }

    expect(roundTrip(filters)).toEqual(filters)
  })

  it('round-trips an empty filter set', () => {
    expect(roundTrip(EMPTY_FILTERS)).toEqual({})
  })

  it('round-trips generation zero', () => {
    expect(roundTrip({ generation: 0 })).toEqual({ generation: 0 })
  })

  it('ignores parameters it does not own', () => {
    // A tab or a selected member in the same URL must survive a filter change.
    const params = new URLSearchParams({ tab: 'tree', selected: 'm1', status: 'alive' })

    expect(fromSearchParams(params)).toEqual({ status: 'alive' })
  })

  it('reads a malformed generation as undefined rather than NaN', () => {
    // NaN reaches the server as the string "NaN" and comes back a 400 the user cannot act on.
    expect(fromSearchParams(new URLSearchParams({ generation: 'abc' })).generation).toBeUndefined()
  })

  it('reads a malformed country as undefined rather than NaN', () => {
    expect(fromSearchParams(new URLSearchParams({ countryId: '' })).countryId).toBeUndefined()
  })

  it('drops an unrecognised status', () => {
    // The server answers 400 FILTER_INVALID_STATUS for one; a hand-edited URL should not strand
    // the page on an error it cannot clear.
    expect(fromSearchParams(new URLSearchParams({ status: 'dead' })).status).toBeUndefined()
  })

  it('treats status=all as no status filter', () => {
    expect(fromSearchParams(new URLSearchParams({ status: 'all' })).status).toBeUndefined()
  })
})

describe('activeFilterCount', () => {
  it('is zero for an empty filter set', () => {
    expect(activeFilterCount(EMPTY_FILTERS)).toBe(0)
  })

  it('does not count the root', () => {
    // The root selects what the numbers are measured from; it removes nobody.
    expect(activeFilterCount({ rootId: 'r1' })).toBe(0)
  })

  it('does not count status=all', () => {
    expect(activeFilterCount({ status: 'all' })).toBe(0)
  })

  it('counts generation zero', () => {
    expect(activeFilterCount({ generation: 0 })).toBe(1)
  })

  it('counts every narrowing filter', () => {
    expect(
      activeFilterCount({
        search: 'فارس',
        status: 'alive',
        branchId: 'b1',
        generation: 2,
        countryId: 165,
        rootId: 'r1',
      }),
    ).toBe(5)
  })
})
