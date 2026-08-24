import { act, render, screen } from '@testing-library/react'
import { MemoryRouter, useSearchParams } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { useMemberFilters } from './useMemberFilters'

/**
 * Renders the hook and exposes both its return value and the live query string, so a test can
 * assert what the user sees and what the URL says in the same breath.
 */
let latest: ReturnType<typeof useMemberFilters>

const Probe = () => {
  latest = useMemberFilters()
  const [params] = useSearchParams()
  return <output data-testid="query">{params.toString()}</output>
}

const renderAt = (path: string) =>
  render(
    <MemoryRouter initialEntries={[path]}>
      <Probe />
    </MemoryRouter>,
  )

const query = () => screen.getByTestId('query').textContent

describe('useMemberFilters', () => {
  it('reads no filters from a bare URL', () => {
    renderAt('/members')

    expect(latest.filters).toEqual({})
    expect(latest.activeCount).toBe(0)
  })

  it('reads the filters out of the URL', () => {
    renderAt('/members?status=alive&generation=2')

    expect(latest.filters).toEqual({ status: 'alive', generation: 2 })
    expect(latest.activeCount).toBe(2)
  })

  it('writes one filter into the URL and nothing else', () => {
    renderAt('/members')

    act(() => latest.setFilter('status', 'alive'))

    expect(query()).toBe('status=alive')
  })

  it('clears a filter when set to undefined', () => {
    renderAt('/members?status=alive')

    act(() => latest.setFilter('status', undefined))

    expect(query()).toBe('')
    expect(latest.filters).toEqual({})
  })

  it('preserves the other filters when one changes', () => {
    renderAt('/members?status=alive&countryId=165')

    act(() => latest.setFilter('generation', 2))

    expect(latest.filters).toEqual({ status: 'alive', countryId: 165, generation: 2 })
  })

  it('preserves a parameter it does not own', () => {
    // ?memberId= is already part of TreePage's URL contract. A filter change that dropped it
    // would close the user's panel as a side effect.
    renderAt('/tree?memberId=m1')

    act(() => latest.setFilter('status', 'alive'))

    expect(query()).toContain('memberId=m1')
    expect(query()).toContain('status=alive')
  })

  it('keeps a parameter it does not own through a reset', () => {
    // setSearchParams({}) — what spec §6.1 literally suggests — would also clear memberId.
    renderAt('/tree?memberId=m1&status=alive&generation=2')

    act(() => latest.reset())

    expect(query()).toBe('memberId=m1')
    expect(latest.filters).toEqual({})
  })

  it('clears every filter on reset', () => {
    renderAt('/members?search=فارس&status=alive&branchId=b1&generation=2&countryId=165')

    act(() => latest.reset())

    expect(query()).toBe('')
    expect(latest.activeCount).toBe(0)
  })

  it('does not count the root as a filter', () => {
    // rootId selects what branch and generation are measured from; it removes nobody.
    renderAt('/tree?rootId=r1')

    expect(latest.filters).toEqual({ rootId: 'r1' })
    expect(latest.activeCount).toBe(0)
  })

  it('keeps the root through a reset', () => {
    renderAt('/tree?rootId=r1&status=alive')

    act(() => latest.reset())

    expect(latest.filters).toEqual({ rootId: 'r1' })
    expect(query()).toBe('rootId=r1')
  })

  it('lands both of two changes made in one tick', () => {
    // Reading the current filters out of a stale render and writing them back wholesale would
    // lose the first of the two.
    renderAt('/members')

    act(() => {
      latest.setFilter('status', 'alive')
      latest.setFilter('generation', 2)
    })

    expect(latest.filters).toEqual({ status: 'alive', generation: 2 })
  })

  it('counts generation zero as an active filter', () => {
    renderAt('/tree?generation=0')

    expect(latest.filters).toEqual({ generation: 0 })
    expect(latest.activeCount).toBe(1)
  })
})
