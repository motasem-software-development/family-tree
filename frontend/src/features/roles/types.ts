export type Role = {
  id: string
  name: string
  description: string | null
  isSystem: boolean
  userCount: number
  permissions: string[]
}

/**
 * `description` is `string | null` and is null for every seeded permission today — the server
 * cannot carry a bilingual label, so human-readable text for permissions comes from the
 * `permissions` i18n namespace instead. `code` remains the source of truth for which
 * permissions exist.
 */
export type Permission = {
  code: string
  description: string | null
}
