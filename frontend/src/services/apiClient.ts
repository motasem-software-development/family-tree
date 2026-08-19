import { tokenStorage } from './tokenStorage'

export class ApiError extends Error {
  readonly code: string
  readonly status: number
  /**
   * The Problem Details `reason` extension, when the backend sent one.
   *
   * `code` says what rule was violated; `reason` says which cause within that rule, and exists
   * precisely because only some causes have a remedy the caller can offer. `EXPORT_TREE_TOO_LARGE`
   * with `sheet-overflow` can be retried as A4; the same code with `member-cap` or `a4-page-cap`
   * cannot be retried at all, and offering a remedy that does not exist is worse than offering
   * none.
   */
  readonly reason?: string

  constructor(code: string, status: number, reason?: string) {
    super(code)
    this.name = 'ApiError'
    this.code = code
    this.status = status
    this.reason = reason
  }
}

const REFRESH_PATH = '/api/v1/auth/refresh'
const LOGIN_PATH = '/api/v1/auth/login'

const withAuth = (init: RequestInit, accessToken?: string, json = true): RequestInit => {
  const headers = new Headers(init.headers)
  if (json) headers.set('Content-Type', 'application/json')
  if (accessToken) headers.set('Authorization', `Bearer ${accessToken}`)
  return { ...init, headers }
}

const errorFrom = async (response: Response): Promise<ApiError> => {
  try {
    const body = (await response.json()) as { code?: string; reason?: string }
    return new ApiError(body.code ?? 'UNKNOWN', response.status, body.reason)
  } catch {
    return new ApiError('UNKNOWN', response.status)
  }
}

type SessionEndedListener = () => void

const sessionEndedListeners = new Set<SessionEndedListener>()

/**
 * Subscribe to "this session is over, and not because the user asked".
 *
 * A failed refresh is the client discovering that its credentials are gone — the server
 * revokes every refresh token a user holds on deactivation, on an administrator password
 * reset, and on the user's own password change. Clearing tokenStorage is not enough on its
 * own: nothing re-renders on it, so the cached session data stays on screen and the user is
 * left inside a fully rendered app whose every request fails. AuthProvider subscribes here and
 * runs exactly the same teardown it runs for the sign-out button — one mechanism, two triggers.
 *
 * Returns an unsubscribe function.
 */
export const onSessionEnded = (listener: SessionEndedListener): (() => void) => {
  sessionEndedListeners.add(listener)
  return () => {
    sessionEndedListeners.delete(listener)
  }
}

/** Copied before iterating so a listener that unsubscribes itself cannot disturb the walk. */
const endSession = (): void => {
  tokenStorage.clear()
  for (const listener of [...sessionEndedListeners]) listener()
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
  // No refresh token means there was no session to end — a 401 on the login page, say. Ending
  // one here would fire the teardown at exactly the moment the user is trying to sign in.
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
    endSession()
    return false
  }

  if (!response.ok) {
    endSession()
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
 * The shared request core: attempt, refresh once on 401, replay. Both apiFetch and
 * apiFetchBlob route through this so the refresh rules live in exactly one place — a second
 * copy would drift the moment either is touched. The refresh endpoint is excluded so a stale
 * refresh token cannot start a loop. The login endpoint is excluded too: a 401 there means
 * the submitted credentials were wrong, never that the access token is stale, so refreshing
 * is meaningless and would needlessly spend a still-good refresh token from an unrelated
 * session held in this browser.
 */
const request = async (path: string, init: RequestInit, json: boolean): Promise<Response> => {
  const attempt = async (): Promise<Response> =>
    fetch(path, withAuth(init, tokenStorage.read()?.accessToken, json))

  let response = await attempt()

  if (response.status === 401 && path !== REFRESH_PATH && path !== LOGIN_PATH) {
    if (await tryRefresh()) {
      response = await attempt()
    }
  }

  if (!response.ok) throw await errorFrom(response)
  return response
}

/** Single entry point for every JSON API call. See `request` for the refresh-and-replay rules. */
export const apiFetch = async <T>(path: string, init: RequestInit = {}): Promise<T> => {
  const response = await request(path, init, true)

  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

/** Binary responses (the PDF export): no JSON content type, no body parsing. */
export const apiFetchBlob = async (path: string, init: RequestInit = {}): Promise<Blob> => {
  const response = await request(path, init, false)
  return await response.blob()
}
