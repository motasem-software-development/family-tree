import { useSyncExternalStore } from 'react'

/**
 * The single breakpoint the app folds at. Below it the sidebar becomes a drawer, the member
 * panel becomes an overlay, the header stacks its search onto a second row, and the filter
 * controls collapse into a sheet (design spec §6.2).
 *
 * One breakpoint, deliberately: this app's layout has exactly two states, and a per-component
 * breakpoint would let them drift out of step with each other at some intermediate width.
 *
 * 768 rather than the filter bar's own 720: the filter row still fits between the two, but the
 * shell does not — a 248px sidebar beside a 300px search leaves nothing at 730px. Collapsing
 * the filters 48px early costs a sheet that was not strictly needed; the other direction leaves
 * the shell broken across a band nothing was verified at.
 */
export const COMPACT_MAX_WIDTH = 768

const QUERY = `(max-width: ${COMPACT_MAX_WIDTH}px)`

const supported = (): boolean =>
  typeof window !== 'undefined' && typeof window.matchMedia === 'function'

const subscribe = (onChange: () => void): (() => void) => {
  if (!supported()) return () => {}
  const list = window.matchMedia(QUERY)
  list.addEventListener('change', onChange)
  return () => list.removeEventListener('change', onChange)
}

// jsdom has no matchMedia. Falling back to `false` rather than throwing keeps the test suite
// exercising the desktop layout it was written against, with no per-file stub.
const getSnapshot = (): boolean => supported() && window.matchMedia(QUERY).matches

/** True while the viewport is at or below the compact breakpoint. */
export const useIsCompact = (): boolean =>
  useSyncExternalStore(subscribe, getSnapshot, () => false)
