import { useEffect, useState } from 'react'

/**
 * Holds a value back until it has stopped changing for `delayMs`.
 *
 * Search moved to the server in Phase 3, so every keystroke would otherwise be a request and a
 * recursive CTE. The timer is cleared on each change, so a fast typist issues one query rather
 * than one per character.
 */
export const useDebouncedValue = <T>(value: T, delayMs: number): T => {
  const [settled, setSettled] = useState<T>(value)

  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs])

  return settled
}
