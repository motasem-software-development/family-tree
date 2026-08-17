import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { membersApi } from '../members/membersApi'
import type { MemberSearchPage } from '../members/types'
import { memberKeys } from '../members/useMembers'
import { useDebouncedValue } from './useDebouncedValue'

/** How many hits the dropdown shows. The server reports the true total separately. */
export const SEARCH_LIMIT = 8

/** One Arabic character is too broad to be a useful query and matches most of the tree. */
export const MIN_QUERY_LENGTH = 2

const DEBOUNCE_MS = 250

const EMPTY: MemberSearchPage = { total: 0, items: [] }

export interface SearchState {
  page: MemberSearchPage
  /** A request is in flight for the current query — distinct from "no matches". */
  isSearching: boolean
}

/**
 * Server-side search (design spec §5.4), replacing the client-side `searchNodes` that could
 * only see the tree already loaded and could only report the size of its own truncated list.
 */
export const useSearch = (query: string): SearchState => {
  const trimmed = query.trim()
  const debounced = useDebouncedValue(trimmed, DEBOUNCE_MS)
  const enabled = debounced.length >= MIN_QUERY_LENGTH

  const { data, isFetching } = useQuery<MemberSearchPage>({
    queryKey: memberKeys.search(debounced, SEARCH_LIMIT),
    queryFn: () => membersApi.search(debounced, SEARCH_LIMIT),
    enabled,
    // Holding the previous page while the next one loads stops the dropdown collapsing to
    // "no results" between keystrokes.
    placeholderData: keepPreviousData,
  })

  return {
    page: enabled ? (data ?? EMPTY) : EMPTY,
    // The user has typed past the threshold but the settled query has not caught up yet: still
    // searching, even though no request has been issued.
    isSearching: trimmed.length >= MIN_QUERY_LENGTH && (isFetching || debounced !== trimmed),
  }
}
