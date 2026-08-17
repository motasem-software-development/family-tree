import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useDebouncedValue } from './useDebouncedValue'

describe('useDebouncedValue', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('returns the initial value immediately', () => {
    const { result } = renderHook(() => useDebouncedValue('محمد', 250))

    expect(result.current).toBe('محمد')
  })

  it('withholds a new value until the delay has elapsed', () => {
    const { result, rerender } = renderHook(({ value }) => useDebouncedValue(value, 250), {
      initialProps: { value: 'م' },
    })

    rerender({ value: 'محمد' })
    expect(result.current).toBe('م')

    act(() => vi.advanceTimersByTime(250))
    expect(result.current).toBe('محمد')
  })

  it('restarts the delay on every keystroke, so only the last value lands', () => {
    const { result, rerender } = renderHook(({ value }) => useDebouncedValue(value, 250), {
      initialProps: { value: 'م' },
    })

    rerender({ value: 'مح' })
    act(() => vi.advanceTimersByTime(200))
    rerender({ value: 'محم' })
    act(() => vi.advanceTimersByTime(200))

    // 400ms have passed but never 250 consecutive on one value.
    expect(result.current).toBe('م')

    act(() => vi.advanceTimersByTime(50))
    expect(result.current).toBe('محم')
  })
})
