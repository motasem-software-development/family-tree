import { useCallback, useEffect, useState, type RefObject } from 'react'
import { ROW_HEIGHT, windowRange, type WindowRange } from './windowRange'

const allRows = (count: number): WindowRange => ({
  startIndex: 0,
  endIndex: count,
  padStart: 0,
  padEnd: 0,
})

/**
 * Tracks which rows intersect the scroll viewport.
 *
 * Measurement uses getBoundingClientRect on both elements rather than scrollTop and offsetTop,
 * because the content is CSS-zoomed: offsetTop is reported in the zoomed element's own
 * coordinate space while scrollTop is in the scroll container's, and mixing the two drifts
 * further off the more the user zooms. Two rects are in one space by construction.
 *
 * Both measurements are then divided by `zoom` to convert visual pixels into layout pixels,
 * which is the only unit `windowRange` and the spacer divs understand.
 */
export const useVisibleRange = (
  scrollRef: RefObject<HTMLDivElement | null>,
  listRef: RefObject<HTMLDivElement | null>,
  count: number,
  zoom: number,
): WindowRange => {
  const [range, setRange] = useState<WindowRange>(() => allRows(count))

  const measure = useCallback(() => {
    const scroller = scrollRef.current
    const list = listRef.current
    if (scroller === null || list === null || zoom <= 0) {
      setRange(allRows(count))
      return
    }

    const scrollerRect = scroller.getBoundingClientRect()
    const listRect = list.getBoundingClientRect()

    // How far the list's top has travelled above the viewport's top. Negative while the list
    // is still below the fold, which windowRange clamps to the start of the list.
    const scrolledPast = (scrollerRect.top - listRect.top) / zoom
    const viewportHeight = scrollerRect.height / zoom

    setRange(windowRange(scrolledPast, viewportHeight, ROW_HEIGHT, count))
  }, [scrollRef, listRef, count, zoom])

  useEffect(() => {
    measure()

    const scroller = scrollRef.current
    if (scroller === null) return

    scroller.addEventListener('scroll', measure, { passive: true })
    const observer = new ResizeObserver(measure)
    observer.observe(scroller)

    return () => {
      scroller.removeEventListener('scroll', measure)
      observer.disconnect()
    }
  }, [measure, scrollRef])

  return range
}
