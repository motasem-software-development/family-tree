import { apiFetch } from '../../services/apiClient'
import type { Country } from './types'

const COUNTRIES = '/api/v1/countries'

export const countriesApi = {
  list: (): Promise<Country[]> => apiFetch<Country[]>(COUNTRIES),
}
