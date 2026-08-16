import { tokenStorage } from './tokenStorage'

export class ApiError extends Error {
  constructor(
    readonly code: string,
    readonly status: number,
  ) {
    super(code)
    this.name = 'ApiError'
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

const tryRefresh = async (): Promise<boolean> => {
  const tokens = tokenStorage.read()
  if (!tokens?.refreshToken) return false

  const response = await fetch(REFRESH_PATH, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken: tokens.refreshToken }),
  })

  if (!response.ok) {
    tokenStorage.clear()
    return false
  }

  const body = (await response.json()) as { accessToken: string; refreshToken: string }
  tokenStorage.write({ accessToken: body.accessToken, refreshToken: body.refreshToken })
  return true
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
