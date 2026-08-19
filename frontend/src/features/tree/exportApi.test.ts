import { afterEach, describe, expect, it, vi } from 'vitest'
import { apiFetchBlob } from '../../services/apiClient'
import { tokenStorage } from '../../services/tokenStorage'
import { downloadTreePdf } from './exportApi'

describe('apiFetchBlob', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    tokenStorage.clear()
  })

  it('returns the response body as a blob', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(new Blob(['%PDF-1.4']), {
        status: 200,
        headers: { 'Content-Type': 'application/pdf' },
      }),
    )

    const blob = await apiFetchBlob('/api/v1/family-tree/export.pdf')

    expect(blob.size).toBeGreaterThan(0)
  })

  it('does not force a JSON content type on the request', async () => {
    const fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(new Response(new Blob(['%PDF-1.4']), { status: 200 }))

    await apiFetchBlob('/api/v1/family-tree/export.pdf')

    const init = fetchSpy.mock.calls[0][1] as RequestInit
    expect(new Headers(init.headers).get('Content-Type')).toBeNull()
  })

  it('surfaces a coded ApiError when the export is refused', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ code: 'EXPORT_TREE_TOO_LARGE', reason: 'sheet-overflow' }), {
        status: 413,
        headers: { 'Content-Type': 'application/problem+json' },
      }),
    )

    await expect(apiFetchBlob('/api/v1/family-tree/export.pdf')).rejects.toMatchObject({
      code: 'EXPORT_TREE_TOO_LARGE',
      status: 413,
    })
  })
})

describe('downloadTreePdf', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    tokenStorage.clear()
  })

  // The server falls back to the browser's own Accept-Language, which is not the language the
  // reader chose in the app. Without this header an Arabic reader on an English-locale browser
  // gets an English caption on an Arabic tree.
  it('asks for the caption in the language the app is displaying', async () => {
    const fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(new Response(new Blob(['%PDF-1.4']), { status: 200 }))
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:stub')
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {})

    await downloadTreePdf({ style: 'xmind', page: 'sheet', language: 'ar' }, 'tree.pdf')

    const init = fetchSpy.mock.calls[0][1] as RequestInit
    expect(new Headers(init.headers).get('Accept-Language')).toBe('ar')
  })
})
