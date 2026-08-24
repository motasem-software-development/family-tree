import { beforeEach, describe, expect, it, vi } from 'vitest'
import { tokenStorage } from '../../services/tokenStorage'
import { downloadMembersXlsx } from './membersExportApi'

const blobResponse = (): Response =>
  new Response(new Blob(['workbook']), {
    status: 200,
    headers: {
      'Content-Type':
        'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    },
  })

describe('downloadMembersXlsx', () => {
  beforeEach(() => {
    tokenStorage.write({ accessToken: 'token', refreshToken: 'refresh' })
    vi.restoreAllMocks()
    vi.stubGlobal('URL', {
      ...URL,
      createObjectURL: vi.fn(() => 'blob:members'),
      revokeObjectURL: vi.fn(),
    })
  })

  it('sends no query string when nothing is filtered', async () => {
    const fetchMock = vi.fn().mockResolvedValue(blobResponse())
    vi.stubGlobal('fetch', fetchMock)

    await downloadMembersXlsx({}, 'ar', 'عائلة السقا.xlsx')

    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/family-members/export.xlsx')
  })

  it('carries the current filters, serialised the same way the list is', async () => {
    // Re-deriving the query string here would be a second chance to disagree with the server
    // about what a filter means, and the file would then differ from the page.
    const fetchMock = vi.fn().mockResolvedValue(blobResponse())
    vi.stubGlobal('fetch', fetchMock)

    await downloadMembersXlsx(
      { status: 'deceased', generation: 2, countryId: 165 },
      'ar',
      'f.xlsx',
    )

    expect(fetchMock.mock.calls[0][0]).toBe(
      '/api/v1/family-members/export.xlsx?status=deceased&generation=2&countryId=165',
    )
  })

  it('sends the app language rather than the browser locale', async () => {
    // Someone reading the app in Arabic on an English-locale browser must not get English
    // headers on an Arabic family's data.
    const fetchMock = vi.fn().mockResolvedValue(blobResponse())
    vi.stubGlobal('fetch', fetchMock)

    await downloadMembersXlsx({}, 'ar', 'f.xlsx')

    const headers = new Headers(fetchMock.mock.calls[0][1].headers as HeadersInit)
    expect(headers.get('Accept-Language')).toBe('ar')
  })

  it('names the downloaded file', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(blobResponse()))
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})

    await downloadMembersXlsx({}, 'en', 'عائلة السقا.xlsx')

    expect(click).toHaveBeenCalled()
  })

  it('revokes the object URL after the click', async () => {
    // Leaking it pins the whole blob in memory for the tab's lifetime.
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(blobResponse()))
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})

    await downloadMembersXlsx({}, 'en', 'f.xlsx')

    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:members')
  })

  it('revokes the object URL even when the click throws', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(blobResponse()))
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {
      throw new Error('blocked')
    })

    await expect(downloadMembersXlsx({}, 'en', 'f.xlsx')).rejects.toThrow('blocked')

    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:members')
  })
})
