import { tokenStorage } from './tokenStorage'

export class ApiError extends Error {
  readonly code: string
  readonly status: number

  constructor(code: string, status: number) {
    super(code)
    this.name = 'ApiError'
    this.code = code
    this.status = status
  }
}

const REFRESH_PATH = '/api/v1/auth/refresh'

const withAuth = (init: RequestInit, accessToken?: string): RequestInit => {
  const headers = new Headers(init.headers)
  headers.set('Content-Type', 'application/json')
  if (accessToken) headers.set('Authorization', `Bearer ${accessToken}`)
  return { ...init, headers }
}

const errorFrom = async (response: Response): Promise<ApiError> => {
  try {
    const body = (await response.json()) as { code?: string }
    return new ApiError(body.code ?? 'UNKNOWN', response.status)
  } catch {
    return new ApiError('UNKNOWN', response.status)
  }
}

/**
 * Guards concurrent refresh attempts. The backend rotates refresh tokens, so a second
 * caller presenting the same (now-revoked) token while a refresh is already in flight
 * would get a 401 and clear a session that actually just succeeded. All concurrent
 * callers instead await the one in-flight refresh and share its result.
 */
let refreshInFlight: Promise<boolean> | null = null

const performRefresh = async (): Promise<boolean> => {
  const tokens = tokenStorage.read()
  if (!tokens?.refreshToken) return false

  let response: Response
  try {
    response = await fetch(REFRESH_PATH, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken: tokens.refreshToken }),
    })
  } catch {
    // Network failure: treat the same as a failed refresh rather than leaking a raw
    // exception past the caller, which expects the original 401 to surface as an ApiError.
    tokenStorage.clear()
    return false
  }

  if (!response.ok) {
    tokenStorage.clear()
    return false
  }

  const body = (await response.json()) as { accessToken: string; refreshToken: string }
  tokenStorage.write({ accessToken: body.accessToken, refreshToken: body.refreshToken })
  return true
}

const tryRefresh = (): Promise<boolean> => {
  if (!refreshInFlight) {
    refreshInFlight = performRefresh().finally(() => {
      refreshInFlight = null
    })
  }
  return refreshInFlight
}

/**
 * Single entry point for every API call. On 401 it refreshes once and replays the request;
 * a second failure surfaces to the caller. The refresh endpoint is excluded so a stale
 * refresh token cannot start a loop.
 */
export const apiFetch = async <T>(path: string, init: RequestInit = {}): Promise<T> => {
  const attempt = async (): Promise<Response> =>
    fetch(path, withAuth(init, tokenStorage.read()?.accessToken))

  let response = await attempt()

  if (response.status === 401 && path !== REFRESH_PATH) {
    if (await tryRefresh()) {
      response = await attempt()
    }
  }

  if (!response.ok) throw await errorFrom(response)

  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}
