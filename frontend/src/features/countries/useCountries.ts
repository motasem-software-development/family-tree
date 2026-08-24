import { useQuery } from '@tanstack/react-query'
import { countriesApi } from './countriesApi'
import type { Country } from './types'

export const countryKeys = {
  all: ['countries'] as const,
}

/**
 * Reference data: seeded server-side and changed only by a deploy, so it is cached for the
 * session rather than refetched. Every consumer — the member form here, the country filter in
 * the next plan — shares this one query.
 */
export const useCountriesQuery = () =>
  useQuery<Country[]>({
    queryKey: countryKeys.all,
    queryFn: () => countriesApi.list(),
    staleTime: Infinity,
  })
