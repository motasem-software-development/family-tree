import { useCallback, useEffect, useMemo, useRef } from 'react'
import { useSearchParams } from 'react-router-dom'
import {
  activeFilterCount,
  fromSearchParams,
  toFilterParams,
  type MemberFilters,
} from './filterParams'

/** The keys this module owns in the query string. Everything else is left alone. */
const OWNED = ['search', 'status', 'branchId', 'generation', 'countryId', 'rootId'] as const

export interface MemberFilterState {
  filters: MemberFilters
  /** How many filters are narrowing the result. The root is not one (design spec §6.2). */
  activeCount: number
  setFilter: <K extends keyof MemberFilters>(key: K, value: MemberFilters[K] | undefined) => void
  reset: () => void
}

/**
 * Filter state lives in the URL query string rather than in component state (design spec §6.1).
 * A filtered view is then linkable and survives a refresh, and Plan 4's export button builds its
 * download URL by passing these parameters straight through instead of re-deriving them.
 */
export const useMemberFilters = (): MemberFilterState => {
  const [searchParams, setSearchParams] = useSearchParams()

  const filters = useMemo(() => fromSearchParams(searchParams), [searchParams])

  /**
   * What the last write asked for, before the navigation carrying it has committed.
   *
   * React Router hands its functional setter the params derived from the *current* location, so
   * two writes in one tick both start from the same pre-change value and the second silently
   * discards the first. Basing each write on the pending value instead makes them compose.
   */
  const pending = useRef<URLSearchParams | null>(null)

  // The navigation landed, so the URL is the truth again. Also covers a change from elsewhere —
  // the Back button, or a link — which must not be rebased onto a stale pending value.
  useEffect(() => {
    pending.current = null
  }, [searchParams])

  /**
   * Rewrites only the keys this module owns, leaving the rest of the query string untouched —
   * `?memberId=` is already part of TreePage's contract, and a filter change that dropped it
   * would close the user's panel as a side effect.
   */
  const write = useCallback(
    (next: (current: MemberFilters) => MemberFilters) => {
      setSearchParams(
        (committed) => {
          const base = pending.current ?? committed
          const updated = next(fromSearchParams(base))

          const params = new URLSearchParams(base)
          OWNED.forEach((key) => params.delete(key))
          toFilterParams(updated).forEach((value, key) => params.set(key, value))

          pending.current = params
          return params
        },
        // Replace, not push: a filter keystroke is not a navigation, and one history entry per
        // character would make Back unusable.
        { replace: true },
      )
    },
    [setSearchParams],
  )

  const setFilter = useCallback<MemberFilterState['setFilter']>(
    (key, value) =>
      write((current) => {
        const updated = { ...current }
        if (value === undefined) delete updated[key]
        else updated[key] = value
        return updated
      }),
    [write],
  )

  /**
   * Specification §15's Reset Filters. It keeps the root: that selects what branch and
   * generation are measured from rather than narrowing anything, so clearing it would silently
   * change the numbers the user is reading.
   */
  const reset = useCallback(
    () => write((current) => (current.rootId === undefined ? {} : { rootId: current.rootId })),
    [write],
  )

  return { filters, activeCount: activeFilterCount(filters), setFilter, reset }
}
