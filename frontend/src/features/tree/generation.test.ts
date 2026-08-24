import { describe, expect, it } from 'vitest'
import type { FamilyTreeNode } from '../members/types'
import { rootGenerationOf, rootRelative } from './generation'

const node = (id: string, generation: number): FamilyTreeNode => ({
  id,
  name: id,
  parentId: null,
  generation,
  hasMoreChildren: false,
  matches: true,
  children: [],
})

describe('rootGenerationOf', () => {
  it('reads the absolute generation the view is rooted at', () => {
    expect(rootGenerationOf([node('s1', 1)])).toBe(1)
  })

  it('follows a subtree root deeper in the family', () => {
    expect(rootGenerationOf([node('f1', 3)])).toBe(3)
  })

  it('falls back to one while the view is still loading', () => {
    // Nothing may render a negative generation in the gap before the tree arrives.
    expect(rootGenerationOf([])).toBe(1)
  })
})

describe('rootRelative', () => {
  it('numbers the root zero', () => {
    // Specification §21's table: the root person reads 0, not 1.
    expect(rootRelative(1, 1)).toBe(0)
  })

  it('numbers the root children one', () => {
    expect(rootRelative(2, 1)).toBe(1)
  })

  it('measures from a subtree root rather than from the family', () => {
    // A view rooted at an absolute generation-2 member: their grandchild reads 2, not 3.
    expect(rootRelative(4, 2)).toBe(2)
  })

  it('is not simply one less than the absolute number', () => {
    // The shortcut spec §1.2's wording suggests would pass every case above and fail here.
    expect(rootRelative(4, 2)).not.toBe(3)
    expect(rootRelative(3, 3)).toBe(0)
  })
})
