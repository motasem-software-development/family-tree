import { beforeEach, describe, expect, it, vi } from 'vitest'
import { countriesApi } from './countriesApi'
import { tokenStorage } from '../../services/tokenStorage'

const jsonResponse = (body: unknown, status = 200): Response =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })

describe('countriesApi', () => {
  beforeEach(() => {
    tokenStorage.write({ accessToken: 'token', refreshToken: 'refresh' })
    vi.restoreAllMocks()
  })

  it('lists countries', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse([{ id: 1, code: 'PS', nameAr: 'فلسطين', nameEn: 'Palestine', dialCode: '+970' }]),
    )
    vi.stubGlobal('fetch', fetchMock)

    const countries = await countriesApi.list()

    expect(fetchMock).toHaveBeenCalledWith('/api/v1/countries', expect.anything())
    expect(countries[0].dialCode).toBe('+970')
  })
})
