import { useQuery } from '@tanstack/react-query'
import { reportsApi } from './reportsApi'
import type { ReportsResponse } from './types'

export const reportKeys = {
  all: ['reports'] as const,
}

/**
 * Not nested under the members key: reports are derived from members, but they are recomputed
 * server-side per request, so a member mutation should refetch them rather than patch a cache.
 * Invalidating 'members' does not touch this key, which is why the screen refetches on mount.
 */
export const useReportsQuery = () =>
  useQuery<ReportsResponse>({ queryKey: reportKeys.all, queryFn: () => reportsApi.get() })
