import { useCallback, useSyncExternalStore } from 'react'

/**
 * Where the filter controls stop fitting on one row.
 *
 * Five controls at their natural widths plus a Reset button need roughly 680px; 720 leaves the
 * page its own padding. One breakpoint, defined once (design spec §6.2) — a second one would
 * eventually disagree with this one about what "narrow" means.
 */
export const COMPACT_MAX_WIDTH = 720

const QUERY = `(max-width: ${COMPACT_MAX_WIDTH}px)`

/**
 * True when the viewport is too narrow for the inline filter bar, so the controls collapse into
 * a sheet instead.
 *
 * `useSyncExternalStore` rather than an effect: it needs no render-then-correct pass, cannot
 * tear between the value read and the value rendered, and gets the subscribe/unsubscribe pairing
 * right by construction.
 */
export const useIsCompact = (): boolean => {
  const subscribe = useCallback((onChange: () => void) => {
    const media = window.matchMedia?.(QUERY)
    // No matchMedia is not an error — the desktop layout is the safe fallback, and a page that
    // throws here would render nothing at all.
    if (media === undefined) return () => {}

    media.addEventListener('change', onChange)
    return () => media.removeEventListener('change', onChange)
  }, [])

  const getSnapshot = useCallback(() => window.matchMedia?.(QUERY).matches ?? false, [])

  // The server snapshot is the same fallback: there is no viewport to measure before hydration.
  return useSyncExternalStore(subscribe, getSnapshot, () => false)
}
