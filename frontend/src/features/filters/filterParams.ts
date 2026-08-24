/**
 * The seam where the client and the server could disagree about what a filter means, isolated
 * and unit-tested (design spec §6.1).
 *
 * Filter state lives in the URL query string rather than component state, so a filtered view is
 * linkable and survives a refresh — and the export button builds its download URL by passing the
 * same parameters straight through instead of re-deriving them.
 */

/** `all` is the absence of a status filter, not a value the server needs to be told. */
export type MemberStatusFilter = 'all' | 'alive' | 'deceased'

const STATUSES: readonly MemberStatusFilter[] = ['all', 'alive', 'deceased']

export interface MemberFilters {
  search?: string
  status?: MemberStatusFilter
  branchId?: string
  /** Root-relative: the selected root is 0 (design spec §1.2). */
  generation?: number
  countryId?: number
  /** Not a filter — it selects what branch and generation are measured from. */
  rootId?: string
}

export const EMPTY_FILTERS: MemberFilters = {}

/**
 * Serialises to exactly the query string the API expects. An absent field emits no parameter:
 * `?status=all` is a default the server would have to special-case, and it makes an unfiltered
 * URL look filtered.
 */
export const toFilterParams = (filters: MemberFilters): URLSearchParams => {
  const params = new URLSearchParams()

  const search = filters.search?.trim()
  if (search) params.set('search', search)
  if (filters.status && filters.status !== 'all') params.set('status', filters.status)
  if (filters.branchId) params.set('branchId', filters.branchId)
  if (filters.generation !== undefined) params.set('generation', String(filters.generation))
  if (filters.countryId !== undefined) params.set('countryId', String(filters.countryId))
  if (filters.rootId) params.set('rootId', filters.rootId)

  return params
}

/**
 * Reads filters back out of a URL, ignoring anything it does not own — a tab, a selected member,
 * whatever else the page keeps there — so a filter change cannot drop an unrelated parameter.
 */
export const fromSearchParams = (params: URLSearchParams): MemberFilters => {
  const filters: MemberFilters = {}

  const search = params.get('search')?.trim()
  if (search) filters.search = search

  const status = params.get('status')
  // An unrecognised status is dropped rather than forwarded: the server answers 400
  // FILTER_INVALID_STATUS for one, and a hand-edited URL should not strand the page on an error
  // it cannot clear.
  if (status && isStatus(status) && status !== 'all') filters.status = status

  const branchId = params.get('branchId')
  if (branchId) filters.branchId = branchId

  const generation = toNumber(params.get('generation'))
  if (generation !== undefined) filters.generation = generation

  const countryId = toNumber(params.get('countryId'))
  if (countryId !== undefined) filters.countryId = countryId

  const rootId = params.get('rootId')
  if (rootId) filters.rootId = rootId

  return filters
}

/** How many filters are narrowing the list. The root is excluded — it narrows nothing. */
export const activeFilterCount = (filters: MemberFilters): number =>
  [
    filters.search?.trim() ? 1 : 0,
    filters.status && filters.status !== 'all' ? 1 : 0,
    filters.branchId ? 1 : 0,
    filters.generation !== undefined ? 1 : 0,
    filters.countryId !== undefined ? 1 : 0,
  ].reduce((total, one) => total + one, 0)

const isStatus = (value: string): value is MemberStatusFilter =>
  (STATUSES as readonly string[]).includes(value)

/**
 * Undefined rather than NaN for a malformed value. NaN reaches the server as the string "NaN"
 * and comes back a 400 the user has no way to act on.
 */
const toNumber = (value: string | null): number | undefined => {
  if (value === null || value.trim() === '') return undefined
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}
