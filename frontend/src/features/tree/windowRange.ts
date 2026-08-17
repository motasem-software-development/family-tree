/** Row height fixed by the design handoff. The rail gradient and elbow geometry assume it. */
export const ROW_HEIGHT = 44

/** Rows kept rendered beyond each edge, so a flick does not expose blank space mid-scroll. */
export const OVERSCAN = 6

export interface WindowRange {
  startIndex: number
  /** Exclusive. */
  endIndex: number
  /** Spacer height above the window, in layout pixels. */
  padStart: number
  /** Spacer height below the window, in layout pixels. */
  padEnd: number
}

/**
 * Which slice of a uniform-height list intersects the viewport (design spec §5.4).
 *
 * Pure and DOM-free by design: jsdom gives every element a height of 0, so a component test
 * cannot tell a correct window from a broken one. All measurement lives in `useVisibleRange`,
 * and every unit here is a LAYOUT pixel — the caller divides out any CSS zoom before calling.
 *
 * An unmeasured viewport (height 0) renders everything rather than nothing. Failing toward
 * "too many rows" costs a slow first paint; failing toward "no rows" is a blank screen.
 */
export const windowRange = (
  scrollTop: number,
  viewportHeight: number,
  rowHeight: number,
  count: number,
  overscan: number = OVERSCAN,
): WindowRange => {
  if (count === 0) return { startIndex: 0, endIndex: 0, padStart: 0, padEnd: 0 }
  if (viewportHeight <= 0 || rowHeight <= 0) {
    return { startIndex: 0, endIndex: count, padStart: 0, padEnd: 0 }
  }

  const firstVisible = Math.floor(Math.max(0, scrollTop) / rowHeight)
  const startIndex = Math.max(0, firstVisible - overscan)
  const spanned = Math.ceil(viewportHeight / rowHeight) + overscan * 2
  const endIndex = Math.min(count, startIndex + spanned)

  return {
    startIndex,
    endIndex,
    padStart: startIndex * rowHeight,
    padEnd: (count - endIndex) * rowHeight,
  }
}
