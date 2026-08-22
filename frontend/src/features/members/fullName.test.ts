import { describe, expect, it } from 'vitest'
import { fullName, indexById, lineageName, nameParts, type NamedNode } from './fullName'

/** Five generations, root first: داوود ← محمود ← حسن ← سالم ← عمر. */
const chain: NamedNode[] = [
  { id: '1', name: 'داوود', parentId: null },
  { id: '2', name: 'محمود', parentId: '1' },
  { id: '3', name: 'حسن', parentId: '2' },
  { id: '4', name: 'سالم', parentId: '3' },
  { id: '5', name: 'عمر', parentId: '4' },
]

const byId = indexById(chain)
const at = (id: string): NamedNode => byId.get(id) as NamedNode

describe('nameParts', () => {
  it('gives a first-generation member their own name only', () => {
    expect(nameParts(at('1'), byId)).toEqual(['داوود'])
  })

  it('appends the ancestors it has when the tree is shallower than four', () => {
    expect(nameParts(at('3'), byId)).toEqual(['حسن', 'محمود', 'داوود'])
  })

  it('composes own name, father, grandfather and great-grandfather', () => {
    expect(nameParts(at('4'), byId)).toEqual(['سالم', 'حسن', 'محمود', 'داوود'])
  })

  it('stops at four parts however deep the tree goes', () => {
    expect(nameParts(at('5'), byId)).toEqual(['عمر', 'سالم', 'حسن', 'محمود'])
  })

  it('stops where the chain leaves the list rather than failing', () => {
    const orphan: NamedNode = { id: '9', name: 'فارس', parentId: 'missing' }
    expect(nameParts(orphan, indexById([orphan]))).toEqual(['فارس'])
  })

  it('terminates on a cyclic parentId', () => {
    const cycle: NamedNode[] = [
      { id: 'a', name: 'أ', parentId: 'b' },
      { id: 'b', name: 'ب', parentId: 'a' },
    ]
    expect(nameParts(cycle[0], indexById(cycle))).toEqual(['أ', 'ب', 'أ', 'ب'])
  })
})

describe('fullName', () => {
  it('joins the parts with a single space', () => {
    expect(fullName(at('4'), byId)).toBe('سالم حسن محمود داوود')
  })
})

describe('lineageName', () => {
  it('drops the given name', () => {
    expect(lineageName(at('1'), byId)).toBe('')
  })

  it('keeps everything after the given name', () => {
    expect(lineageName(at('4'), byId)).toBe('حسن محمود داوود')
  })
})
