import { describe, expect, it } from 'vitest'
import { ancestorIds, descendantCount, findNode, flattenTree, treeStats } from './flattenTree'
import type { FamilyTreeNode } from '../members/types'

const node = (
  id: string,
  name: string,
  generation: number,
  children: FamilyTreeNode[] = [],
  parentId: string | null = null,
): FamilyTreeNode => ({ id, name, parentId, generation, hasMoreChildren: false, children })

/** سليمان → فارس → محمود, plus a second first-generation member عمر. */
const tree = (): FamilyTreeNode[] => [
  node('s1', 'سليمان', 1, [node('f1', 'فارس', 2, [node('m1', 'محمود', 3, [], 'f1')], 's1')]),
  node('s2', 'عمر', 1),
]

const ALL_OPEN = { s1: true, f1: true, m1: true, s2: true }

describe('flattenTree', () => {
  it('returns nothing for an empty tree', () => {
    expect(flattenTree([], {})).toEqual([])
  })

  it('renders only the top level when nothing is expanded', () => {
    const rows = flattenTree(tree(), {})

    expect(rows.map((r) => r.name)).toEqual(['سليمان', 'عمر'])
  })

  it('renders a branch once it is expanded', () => {
    const rows = flattenTree(tree(), { s1: true })

    expect(rows.map((r) => r.name)).toEqual(['سليمان', 'فارس', 'عمر'])
  })

  it('indents each generation one level deeper', () => {
    const rows = flattenTree(tree(), ALL_OPEN)

    expect(rows.map((r) => [r.name, r.depth])).toEqual([
      ['سليمان', 0],
      ['فارس', 1],
      ['محمود', 2],
      ['عمر', 0],
    ])
  })

  it('carries the generation through from the server rather than deriving it', () => {
    // Depth is a render concern; generation is the family fact and is computed server-side.
    const rows = flattenTree(tree(), ALL_OPEN)

    expect(rows.find((r) => r.name === 'محمود')?.generation).toBe(3)
  })

  it('marks the last sibling so its connector can stop short', () => {
    const rows = flattenTree(tree(), ALL_OPEN)

    expect(rows.find((r) => r.name === 'سليمان')?.isLast).toBe(false)
    expect(rows.find((r) => r.name === 'عمر')?.isLast).toBe(true)
  })

  it('reports the child count so a collapsed row can show how many it hides', () => {
    const rows = flattenTree(tree(), {})

    expect(rows.find((r) => r.name === 'سليمان')?.childCount).toBe(1)
    expect(rows.find((r) => r.name === 'عمر')?.childCount).toBe(0)
  })

  it('dims non-matching rows instead of hiding them', () => {
    // Hiding would reshape the family on screen and orphan a matching child.
    const rows = flattenTree(tree(), ALL_OPEN, 'محمود')

    const mahmoud = rows.find((r) => r.name === 'محمود')
    const faris = rows.find((r) => r.name === 'فارس')
    expect(rows).toHaveLength(4)
    expect(mahmoud?.matched).toBe(true)
    expect(mahmoud?.dimmed).toBe(false)
    expect(faris?.matched).toBe(false)
    expect(faris?.dimmed).toBe(true)
  })

  it('marks nothing as dimmed when the search box is empty', () => {
    const rows = flattenTree(tree(), ALL_OPEN, '   ')

    expect(rows.every((r) => !r.dimmed && !r.matched)).toBe(true)
  })

  it('matches case-insensitively', () => {
    const latin = [node('a', 'Suleiman', 1)]

    expect(flattenTree(latin, {}, 'SULEIMAN')[0].matched).toBe(true)
  })
})

describe('descendantCount', () => {
  it('counts every level beneath a node, not just direct children', () => {
    const [suleiman] = tree()

    expect(suleiman.children).toHaveLength(1)
    expect(descendantCount(suleiman)).toBe(2)
  })

  it('is zero for a leaf', () => {
    expect(descendantCount(node('x', 'x', 1))).toBe(0)
  })
})

describe('ancestorIds', () => {
  it('walks upward from a deep node so its branches can be revealed', () => {
    expect(ancestorIds(tree(), 'm1')).toEqual(['f1', 's1'])
  })

  it('is empty for a first-generation member', () => {
    expect(ancestorIds(tree(), 's1')).toEqual([])
  })

  it('is empty for an unknown id rather than looping', () => {
    expect(ancestorIds(tree(), 'nope')).toEqual([])
  })
})

describe('treeStats', () => {
  it('counts every member and the deepest generation', () => {
    expect(treeStats(tree())).toEqual({ members: 4, generations: 3 })
  })

  it('is zero for an empty tree', () => {
    expect(treeStats([])).toEqual({ members: 0, generations: 0 })
  })
})

describe('findNode', () => {
  it('finds a node nested several levels deep', () => {
    expect(findNode(tree(), 'm1')?.name).toBe('محمود')
  })

  it('returns undefined for an unknown id', () => {
    expect(findNode(tree(), 'nope')).toBeUndefined()
  })
})

describe('flattenTree life details', () => {
  it('joins the life details in by id', () => {
    const rows = flattenTree(tree(), ALL_OPEN, '', new Map([
      ['f1', { dateOfBirth: '1940-01-05', dateOfDeath: '2010-06-30', isDeceased: true }],
    ]))

    expect(rows.find((r) => r.id === 'f1')?.life.isDeceased).toBe(true)
    expect(rows.find((r) => r.id === 'f1')?.life.dateOfBirth).toBe('1940-01-05')
  })

  it('falls back to living with no dates for a member the flat list has not caught up with', () => {
    // The tree and the flat list are two queries. Straight after a create the tree can hold a
    // member the list does not, and a blank row would be worse than an honest default.
    const rows = flattenTree(tree(), ALL_OPEN)

    expect(rows.every((r) => !r.life.isDeceased)).toBe(true)
    expect(rows.every((r) => r.life.dateOfBirth === null)).toBe(true)
  })
})
