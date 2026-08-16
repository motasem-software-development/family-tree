export interface Tokens {
  accessToken: string
  refreshToken: string
}

const STORAGE_KEY = 'familytree.tokens'

/**
 * sessionStorage rather than localStorage: tokens die with the tab, which limits the
 * blast radius on a shared machine. Hardening this further (httpOnly cookies) is a
 * Phase 7 decision that would change the API surface, so it is not made here.
 */
export const tokenStorage = {
  read(): Tokens | null {
    const raw = sessionStorage.getItem(STORAGE_KEY)
    if (!raw) return null
    try {
      return JSON.parse(raw) as Tokens
    } catch {
      return null
    }
  },

  write(tokens: Tokens): void {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(tokens))
  },

  clear(): void {
    sessionStorage.removeItem(STORAGE_KEY)
  },
}
