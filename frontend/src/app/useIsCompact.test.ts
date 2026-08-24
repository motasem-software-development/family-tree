import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { COMPACT_MAX_WIDTH, useIsCompact } from './useIsCompact'

/**
 * A controllable matchMedia stub. jsdom ships one that never matches and never changes, which
 * would let a broken subscription pass every assertion.
 */
const stubMatchMedia = (initial: boolean) => {
  const listeners = new Set<() => void>()
  let matches = initial

  const media = {
    get matches() {
      return matches
    },
    addEventListener: (_: string, listener: () => void) => listeners.add(listener),
    removeEventListener: (_: string, listener: () => void) => listeners.delete(listener),
  }

  const matchMedia = vi.fn(() => media)
  vi.stubGlobal('matchMedia', matchMedia)

  return {
    matchMedia,
    listenerCount: () => listeners.size,
    resize: (next: boolean) => {
      matches = next
      listeners.forEach((listener) => listener())
    },
  }
}

afterEach(() => vi.unstubAllGlobals())

describe('useIsCompact', () => {
  it('is true below the breakpoint', () => {
    stubMatchMedia(true)

    expect(renderHook(() => useIsCompact()).result.current).toBe(true)
  })

  it('is false above the breakpoint', () => {
    stubMatchMedia(false)

    expect(renderHook(() => useIsCompact()).result.current).toBe(false)
  })

  it('queries the breakpoint as a max-width', () => {
    const media = stubMatchMedia(false)

    renderHook(() => useIsCompact())

    expect(media.matchMedia).toHaveBeenCalledWith(`(max-width: ${COMPACT_MAX_WIDTH}px)`)
  })

  it('follows a resize', () => {
    const media = stubMatchMedia(false)
    const { result } = renderHook(() => useIsCompact())

    act(() => media.resize(true))

    expect(result.current).toBe(true)
  })

  it('unsubscribes on unmount', () => {
    // A listener left on matchMedia outlives the component and calls setState on a dead one.
    const media = stubMatchMedia(false)
    const { unmount } = renderHook(() => useIsCompact())
    expect(media.listenerCount()).toBe(1)

    unmount()

    expect(media.listenerCount()).toBe(0)
  })

  it('falls back to the desktop layout when matchMedia is missing', () => {
    // jsdom provides it, but a missing API must degrade to a usable page rather than a blank one.
    vi.stubGlobal('matchMedia', undefined)

    expect(renderHook(() => useIsCompact()).result.current).toBe(false)
  })
})
