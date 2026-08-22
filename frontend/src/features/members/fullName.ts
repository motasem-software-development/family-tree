/**
 * A member record stores one given name (`داوود`, `طالب`); the rest of the name is the
 * lineage, which the tree already holds as the `parentId` chain. Composing the two is what
 * turns a row into the name a family actually uses.
 */

/** Just enough of a member to walk the chain — keeps the helper usable from any list shape. */
export interface NamedNode {
  id: string
  name: string
  parentId: string | null
}

/**
 * Own name, father, grandfather, great-grandfather. Four is the customary length of an Arabic
 * name, not a limit of the data: the walk stops there even when the tree goes deeper.
 */
export const NAME_PART_COUNT = 4

/**
 * The name parts, own name first. Shorter than four near the root — a first-generation member
 * has no lineage to append, and padding it would invent ancestors.
 *
 * The walk also stops on a missing lookup rather than throwing: a member whose parent is absent
 * from the list (a mid-flight delete, a partial response) still has a name worth showing.
 * Bounded by NAME_PART_COUNT, so a cyclic parentId cannot loop here.
 */
export const nameParts = (member: NamedNode, byId: Map<string, NamedNode>): string[] => {
  const parts = [member.name]
  let current = member

  while (parts.length < NAME_PART_COUNT && current.parentId !== null) {
    const parent = byId.get(current.parentId)
    if (parent === undefined) break
    parts.push(parent.name)
    current = parent
  }

  return parts
}

/** The composed name as one string — for text that cannot carry the styled split (dialogs, aria). */
export const fullName = (member: NamedNode, byId: Map<string, NamedNode>): string =>
  nameParts(member, byId).join(' ')

/** The lineage alone, i.e. everything after the given name. Empty for a first-generation member. */
export const lineageName = (member: NamedNode, byId: Map<string, NamedNode>): string =>
  nameParts(member, byId).slice(1).join(' ')

export const indexById = <T extends NamedNode>(members: readonly T[]): Map<string, T> =>
  new Map(members.map((member) => [member.id, member]))
