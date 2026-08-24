import { apiFetchBlob } from '../../services/apiClient'
import { toFilterParams, type MemberFilters } from '../filters/filterParams'

/**
 * Downloads the filtered members list as a workbook and hands it to the browser.
 *
 * The sibling of `downloadTreePdf` in `features/tree/exportApi.ts`, deliberately shaped the same
 * way: same blob-and-revoke dance, same reason for the language header.
 */
export const downloadMembersXlsx = async (
  filters: MemberFilters,
  language: string,
  fileName: string,
): Promise<void> => {
  // The very same serialisation the list uses (design spec §6.1). Re-deriving the query string
  // here would be a second chance to disagree with the server about what a filter means, and
  // the export would then quietly return a different set than the page is showing.
  const query = toFilterParams(filters)
  const suffix = query.toString()
  const path = `/api/v1/family-members/export.xlsx${suffix ? `?${suffix}` : ''}`

  // From the app's own language toggle, not the browser's locale. Without this the server falls
  // back to Accept-Language, so someone reading the app in Arabic on an English-locale browser
  // would get English headers on an Arabic family's data.
  const blob = await apiFetchBlob(path, { headers: { 'Accept-Language': language } })
  const url = URL.createObjectURL(blob)

  try {
    const link = document.createElement('a')
    link.href = url
    link.download = fileName
    document.body.appendChild(link)
    link.click()
    link.remove()
  } finally {
    // Revoked immediately: leaking it pins the whole blob in memory for the tab's lifetime.
    URL.revokeObjectURL(url)
  }
}
