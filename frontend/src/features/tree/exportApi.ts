import { apiFetchBlob } from '../../services/apiClient'

export type ExportStyle = 'xmind' | 'clean'
export type ExportPage = 'sheet' | 'a4'

export interface ExportOptions {
  rootId?: string
  style: ExportStyle
  page: ExportPage
  /** Language for the PDF's caption. The diagram itself is never translated. */
  language: string
}

/**
 * Fetches the PDF and hands it to the browser as a download. The object URL is revoked
 * immediately after the click: leaking it pins the whole blob in memory for the tab's
 * lifetime, and these documents are large.
 */
export const downloadTreePdf = async (
  options: ExportOptions,
  fileName: string,
): Promise<void> => {
  const query = new URLSearchParams({ style: options.style, page: options.page })
  if (options.rootId) query.set('rootId', options.rootId)

  // The caption's language comes from the app's own language toggle, not the browser's locale.
  // Without this header the server falls back to Accept-Language, so someone reading the app in
  // Arabic on an English-locale browser would get an English caption on an Arabic tree.
  const blob = await apiFetchBlob(`/api/v1/family-tree/export.pdf?${query.toString()}`, {
    headers: { 'Accept-Language': options.language },
  })
  const url = URL.createObjectURL(blob)

  try {
    const link = document.createElement('a')
    link.href = url
    link.download = fileName
    document.body.appendChild(link)
    link.click()
    link.remove()
  } finally {
    URL.revokeObjectURL(url)
  }
}
