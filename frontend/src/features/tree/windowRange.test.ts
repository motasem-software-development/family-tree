import { describe, expect, it } from 'vitest'
import { ROW_HEIGHT, windowRange } from './windowRange'

describe('windowRange', () => {
  it('renders every row when the viewport has not been measured', () => {
    // jsdom reports 0 for every height, and so does the very first paint before layout. Both
    // must degrade to "render everything" — a windowing bug that hid all rows in tests would
    // otherwise be invisible until a human opened the app.
    const range = windowRange(0, 0, ROW_HEIGHT, 349)

    expect(range).toEqual({ startIndex: 0, endIndex: 349, padStart: 0, padEnd: 0 })
  })

  it('renders every row when there are none to window', () => {
    expect(windowRange(0, 800, ROW_HEIGHT, 0)).toEqual({
      startIndex: 0,
      endIndex: 0,
      padStart: 0,
      padEnd: 0,
    })
  })

  it('covers the viewport plus overscan at the top of the list', () => {
    const range = windowRange(0, 440, ROW_HEIGHT, 349, 6)

    expect(range.startIndex).toBe(0)
    // 440/44 = 10 visible, plus 6 overscan each way.
    expect(range.endIndex).toBe(22)
    expect(range.padStart).toBe(0)
    expect(range.padEnd).toBe((349 - 22) * ROW_HEIGHT)
  })

  it('moves the window and pads the space it left behind', () => {
    const range = windowRange(44 * 100, 440, ROW_HEIGHT, 349, 6)

    expect(range.startIndex).toBe(94)
    expect(range.endIndex).toBe(116)
    expect(range.padStart).toBe(94 * ROW_HEIGHT)
    expect(range.padEnd).toBe((349 - 116) * ROW_HEIGHT)
  })

  it('never overruns the end of the list', () => {
    const range = windowRange(44 * 348, 440, ROW_HEIGHT, 349, 6)

    expect(range.endIndex).toBe(349)
    expect(range.padEnd).toBe(0)
  })

  it('treats a negative scroll position as the top', () => {
    // Overscroll bounce on macOS and iOS reports a negative scroll offset.
    const range = windowRange(-120, 440, ROW_HEIGHT, 349, 6)

    expect(range.startIndex).toBe(0)
    expect(range.padStart).toBe(0)
  })

  it('keeps padStart + rendered height + padEnd equal to the full list height', () => {
    // The invariant that stops the scrollbar jumping as the window moves.
    const count = 349
    for (const scrollTop of [0, 500, 5000, 15000]) {
      const range = windowRange(scrollTop, 600, ROW_HEIGHT, count, 6)
      const rendered = (range.endIndex - range.startIndex) * ROW_HEIGHT

      expect(range.padStart + rendered + range.padEnd).toBe(count * ROW_HEIGHT)
    }
  })
})
