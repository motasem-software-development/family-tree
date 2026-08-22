import { apiFetch } from '../../services/apiClient'
import type { ReportsResponse } from './types'

const REPORTS = '/api/v1/reports'

export const reportsApi = {
  /** One request for all five sections — the windows and caps are server-side constants. */
  get: (): Promise<ReportsResponse> => apiFetch<ReportsResponse>(REPORTS),
}
