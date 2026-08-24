import { beforeEach, describe, expect, it, vi } from 'vitest'
import { membersApi } from './membersApi'
import { EMPTY_CONTACT_DETAILS } from './contactDetails'
import { EMPTY_LIFE_DETAILS } from './lifeDetails'
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

    await membersApi.create('سليمان', null, EMPTY_LIFE_DETAILS, EMPTY_CONTACT_DETAILS)

    const [, init] = fetchMock.mock.calls[0]
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual({
      name: 'سليمان',
      parentId: null,
      dateOfBirth: null,
      dateOfDeath: null,
      isDeceased: false,
      nationalId: null,
      mobileNumber: null,
      whatsAppNumber: null,
      countryId: null,
    })
  })

  it('sends the version when updating so the server can detect a stale write', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({ id: 'a', name: 'فارس أحمد', parentId: null, version: 2 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await membersApi.update('a', 'فارس أحمد', 1, EMPTY_LIFE_DETAILS, EMPTY_CONTACT_DETAILS)

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/v1/family-members/a')
    expect(init.method).toBe('PUT')
    expect(JSON.parse(init.body as string)).toEqual({
      name: 'فارس أحمد',
      version: 1,
      dateOfBirth: null,
      dateOfDeath: null,
      isDeceased: false,
      nationalId: null,
      mobileNumber: null,
      whatsAppNumber: null,
      countryId: null,
    })
  })

  it('never sends parentId on update, because the server rejects it', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ id: 'a', version: 2 }))
    vi.stubGlobal('fetch', fetchMock)

    await membersApi.update('a', 'فارس', 1, EMPTY_LIFE_DETAILS, EMPTY_CONTACT_DETAILS)

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

  it('passes the filter set and maxDepth through as query parameters', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({ id: 't', name: 'عائلة السقا', rootMembers: [] }),
    )
    vi.stubGlobal('fetch', fetchMock)

    // maxDepth rides beside the filter set rather than inside it: it is a transport concern —
    // how much of the tree to ship — not a filter (design spec §5.1).
    await membersApi.tree({ rootId: 'abc', status: 'alive' }, { maxDepth: 2 })

    expect(fetchMock.mock.calls[0][0]).toBe(
      '/api/v1/family-tree/view?status=alive&rootId=abc&maxDepth=2',
    )
  })

  it('sends no query string for an unfiltered list', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]))
    vi.stubGlobal('fetch', fetchMock)

    await membersApi.list({})

    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/family-members')
  })

  it('serialises the filter set onto the members list', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]))
    vi.stubGlobal('fetch', fetchMock)

    await membersApi.list({ status: 'deceased', generation: 2, countryId: 165 })

    expect(fetchMock.mock.calls[0][0]).toBe(
      '/api/v1/family-members?status=deceased&generation=2&countryId=165',
    )
  })

  it('asks for the branches and generations of a root', async () => {
    // A fresh Response per call: a body can only be read once.
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(jsonResponse([])))
    vi.stubGlobal('fetch', fetchMock)

    await membersApi.branches('abc')
    await membersApi.generations('abc')

    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/family-tree/branches?rootId=abc')
    expect(fetchMock.mock.calls[1][0]).toBe('/api/v1/family-tree/generations?rootId=abc')
  })

  it('asks for the whole tree branches when no root is given', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse([]))
    vi.stubGlobal('fetch', fetchMock)

    await membersApi.branches()

    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/family-tree/branches')
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

  it('posts a move to the dedicated command endpoint', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({ id: 'm1', name: 'محمد', parentId: 'p1', version: 4 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await membersApi.move('m1', 'p1', 3)

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/family-members/m1/move',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ parentId: 'p1', version: 3 }),
      }),
    )
  })

  it('sends a null parent when promoting to the first generation', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({ id: 'm1', name: 'محمد', parentId: null, version: 4 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await membersApi.move('m1', null, 3)

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/family-members/m1/move',
      expect.objectContaining({ body: JSON.stringify({ parentId: null, version: 3 }) }),
    )
  })
})
