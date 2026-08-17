import { beforeEach, describe, expect, it, vi } from 'vitest'
import { membersApi } from './membersApi'
import { tokenStorage } from '../../services/tokenStorage'
import { ApiError } from '../../services/apiClient'

const jsonResponse = (body: unknown, status = 200): Response =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })

describe('membersApi', () => {
  beforeEach(() => {
    tokenStorage.write({ accessToken: 'token', refreshToken: 'refresh' })
    vi.restoreAllMocks()
  })

  it('lists members', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse([{ id: 'a', name: 'سليمان', parentId: null, version: 1 }]),
    )
    vi.stubGlobal('fetch', fetchMock)

    const members = await membersApi.list()

    expect(fetchMock).toHaveBeenCalledWith('/api/v1/family-members', expect.anything())
    expect(members).toHaveLength(1)
    expect(members[0].name).toBe('سليمان')
  })

  it('creates a first-generation member with a null parent', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({ id: 'a', name: 'سليمان', parentId: null, version: 1 }, 201),
    )
    vi.stubGlobal('fetch', fetchMock)

    await membersApi.create('سليمان', null)

    const [, init] = fetchMock.mock.calls[0]
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual({ name: 'سليمان', parentId: null })
  })

  it('sends the version when updating so the server can detect a stale write', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({ id: 'a', name: 'فارس أحمد', parentId: null, version: 2 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await membersApi.update('a', 'فارس أحمد', 1)

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/v1/family-members/a')
    expect(init.method).toBe('PUT')
    expect(JSON.parse(init.body as string)).toEqual({ name: 'فارس أحمد', version: 1 })
  })

  it('never sends parentId on update, because the server rejects it', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ id: 'a', version: 2 }))
    vi.stubGlobal('fetch', fetchMock)

    await membersApi.update('a', 'فارس', 1)

    const body = JSON.parse(fetchMock.mock.calls[0][1].body as string)
    expect(body).not.toHaveProperty('parentId')
  })

  it('deletes a member', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)

    await membersApi.remove('a')

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/v1/family-members/a')
    expect(init.method).toBe('DELETE')
  })

  it('surfaces the server error code so the UI can translate it', async () => {
    // A Response body can only be read once, and this test triggers two failed calls against
    // the same mock, so each invocation must produce a fresh Response instance.
    const fetchMock = vi.fn().mockImplementation(() =>
      Promise.resolve(jsonResponse({ code: 'MEMBER_HAS_CHILDREN' }, 409)),
    )
    vi.stubGlobal('fetch', fetchMock)

    await expect(membersApi.remove('a')).rejects.toBeInstanceOf(ApiError)
    await expect(membersApi.remove('a')).rejects.toMatchObject({
      code: 'MEMBER_HAS_CHILDREN',
      status: 409,
    })
  })

  it('fetches the tree without parameters by default', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({ id: 't', name: 'عائلة السقا', rootMembers: [] }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await membersApi.tree()

    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/family-tree/view')
  })

  it('passes rootId and maxDepth through as query parameters', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({ id: 't', name: 'عائلة السقا', rootMembers: [] }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await membersApi.tree({ rootId: 'abc', maxDepth: 2 })

    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/family-tree/view?rootId=abc&maxDepth=2')
  })

  it('sends the query and limit as search parameters', async () => {
    const page = { total: 39, items: [] }
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(page))
    vi.stubGlobal('fetch', fetchMock)

    const result = await membersApi.search('محمد', 8)

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/family-members/search?q=%D9%85%D8%AD%D9%85%D8%AF&limit=8',
      expect.anything(),
    )
    expect(result).toEqual(page)
  })
})
