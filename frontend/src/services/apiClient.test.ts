import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, apiFetch } from './apiClient'
import { tokenStorage } from './tokenStorage'

const jsonResponse = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })

describe('apiFetch', () => {
  beforeEach(() => {
    tokenStorage.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('attaches the bearer token when one is stored', async () => {
    tokenStorage.write({ accessToken: 'token-abc', refreshToken: 'refresh-abc' })
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ ok: true }))
    vi.stubGlobal('fetch', fetchMock)

    await apiFetch('/api/v1/me')

    const headers = new Headers(fetchMock.mock.calls[0][1].headers)
    expect(headers.get('Authorization')).toBe('Bearer token-abc')
  })

  it('sends no Authorization header when no token is stored', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ ok: true }))
    vi.stubGlobal('fetch', fetchMock)

    await apiFetch('/api/v1/me')

    const headers = new Headers(fetchMock.mock.calls[0][1].headers)
    expect(headers.get('Authorization')).toBeNull()
  })

  it('throws an ApiError carrying the backend code', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({ code: 'INVALID_CREDENTIALS', title: 'Authentication failed.' }, 401),
    )
    vi.stubGlobal('fetch', fetchMock)

    await expect(apiFetch('/api/v1/auth/login', { method: 'POST' })).rejects.toMatchObject({
      code: 'INVALID_CREDENTIALS',
      status: 401,
    })
  })

  it('refreshes once on 401 and replays the original request', async () => {
    tokenStorage.write({ accessToken: 'expired', refreshToken: 'refresh-abc' })

    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ code: 'UNAUTHORIZED' }, 401))
      .mockResolvedValueOnce(
        jsonResponse({ accessToken: 'fresh', expiresAt: new Date().toISOString(), refreshToken: 'refresh-def' }),
      )
      .mockResolvedValueOnce(jsonResponse({ email: 'admin@example.com' }))
    vi.stubGlobal('fetch', fetchMock)

    const result = await apiFetch<{ email: string }>('/api/v1/me')

    expect(result.email).toBe('admin@example.com')
    expect(fetchMock).toHaveBeenCalledTimes(3)
    expect(tokenStorage.read()?.accessToken).toBe('fresh')
  })

  it('clears the stored tokens and gives up when the refresh itself fails', async () => {
    tokenStorage.write({ accessToken: 'expired', refreshToken: 'stale' })

    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse({ code: 'UNAUTHORIZED' }, 401))
      .mockResolvedValueOnce(jsonResponse({ code: 'INVALID_REFRESH_TOKEN' }, 401))
    vi.stubGlobal('fetch', fetchMock)

    await expect(apiFetch('/api/v1/me')).rejects.toBeInstanceOf(ApiError)
    expect(tokenStorage.read()).toBeNull()
  })

  it('does not attempt a refresh loop on the refresh endpoint itself', async () => {
    tokenStorage.write({ accessToken: 'expired', refreshToken: 'stale' })
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ code: 'INVALID_REFRESH_TOKEN' }, 401))
    vi.stubGlobal('fetch', fetchMock)

    await expect(apiFetch('/api/v1/auth/refresh', { method: 'POST' })).rejects.toBeInstanceOf(ApiError)
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('shares a single refresh across concurrent 401s and replays both requests', async () => {
    tokenStorage.write({ accessToken: 'expired', refreshToken: 'refresh-abc' })

    const fetchMock = vi.fn().mockImplementation((path: string) => {
      if (path === '/api/v1/auth/refresh') {
        return Promise.resolve(
          jsonResponse({ accessToken: 'fresh', expiresAt: new Date().toISOString(), refreshToken: 'refresh-def' }),
        )
      }
      const tokens = tokenStorage.read()
      if (tokens?.accessToken !== 'fresh') {
        return Promise.resolve(jsonResponse({ code: 'UNAUTHORIZED' }, 401))
      }
      return Promise.resolve(jsonResponse({ ok: true }))
    })
    vi.stubGlobal('fetch', fetchMock)

    const [first, second] = await Promise.all([
      apiFetch<{ ok: boolean }>('/api/v1/me'),
      apiFetch<{ ok: boolean }>('/api/v1/family-tree'),
    ])

    expect(first.ok).toBe(true)
    expect(second.ok).toBe(true)

    const refreshCalls = fetchMock.mock.calls.filter(([path]) => path === '/api/v1/auth/refresh')
    expect(refreshCalls).toHaveLength(1)
    expect(tokenStorage.read()?.accessToken).toBe('fresh')
  })

  it('clears tokens and surfaces an ApiError when the refresh fetch itself rejects', async () => {
    tokenStorage.write({ accessToken: 'expired', refreshToken: 'stale' })

    const fetchMock = vi.fn().mockImplementation((path: string) => {
      if (path === '/api/v1/auth/refresh') {
        return Promise.reject(new Error('network down'))
      }
      return Promise.resolve(jsonResponse({ code: 'UNAUTHORIZED' }, 401))
    })
    vi.stubGlobal('fetch', fetchMock)

    await expect(apiFetch('/api/v1/me')).rejects.toBeInstanceOf(ApiError)
    expect(tokenStorage.read()).toBeNull()
  })
})
