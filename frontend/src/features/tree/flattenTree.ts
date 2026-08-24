import { EMPTY_LIFE_DETAILS, type LifeDetails } from '../members/lifeDetails'
import type { FamilyTreeNode } from '../members/types'

/** One rendered line of the outline. Depth is 0-based; generation is the server's 1-based value. */
export interface TreeRow {
  id: string
  name: string
  parentId: string | null
  generation: number
  depth: number
  /** Last among its siblings — the connector stops half-way instead of continuing down. */
  isLast: boolean
  childCount: number
  hasMoreChildren: boolean
  isOpen: boolean
  /** Matches the active search term. */
  matched: boolean
  /**
   * Rendered faded and non-selectable rather than hidden. Two independent reasons, ORed
   * together: the outline's own text search is active and this row is not a hit, or the server's
   * filter kept this member only to hold up a matching descendant (design spec §4.2). Either way
   * the row is context, not an answer, and both look the same on screen.
   */
  dimmed: boolean
  /**
   * Joined in from the flat members list, not the tree endpoint — the tree DTO carries
   * structure only, and the page already holds both (see the detailById map in TreePage).
   */
  life: LifeDetails
}

/** Life details by member id, as joined from the flat list. Missing ids fall back to living. */
export type LifeDetailsMap = ReadonlyMap<string, LifeDetails>

export type ExpandedMap = Readonly<Record<string, boolean>>

const normalize = (value: string): string => value.trim().toLowerCase()

/**
 * Walks the nested tree into a flat, render-ready row list, following the expanded map.
 * Collapsed branches contribute no rows, so cost tracks what is visible rather than tree size.
 *
 * Search dims rather than filters: hiding non-matches would silently reshape the family, and a
 * parent whose own name does not match still has to be rendered for its matching child to hang off.
 * The server's filter arrives already pruned and reaches the same dimming through `node.matches`.
 */
export const flattenTree = (
  rootMembers: readonly FamilyTreeNode[],
  expanded: ExpandedMap,
  query = '',
  lifeById: LifeDetailsMap = new Map(),
): TreeRow[] => {
  const term = normalize(query)
  const rows: TreeRow[] = []

  const walk = (siblings: readonly FamilyTreeNode[], depth: number): void => {
    siblings.forEach((node, index) => {
      const childCount = node.children.length
      const isOpen = expanded[node.id] === true
      const matched = term.length > 0 && normalize(node.name).includes(term)

      rows.push({
        id: node.id,
        name: node.name,
        parentId: node.parentId,
        generation: node.generation,
        depth,
        isLast: index === siblings.length - 1,
        childCount,
        hasMoreChildren: node.hasMoreChildren,
        isOpen,
        matched,
        // Either reason dims. ORed rather than overwritten: a row can fail the server's filter
        // and the outline's search at once, and neither may cancel the other out.
        dimmed: (term.length > 0 && !matched) || !node.matches,
        // The flat list can lag the tree by one fetch after a create. Defaulting to "living,
        // dates unknown" shows the new member plainly rather than blanking their row.
        life: lifeById.get(node.id) ?? EMPTY_LIFE_DETAILS,
      })

      if (isOpen && childCount > 0) walk(node.children, depth + 1)
    })
  }

  walk(rootMembers, 0)
  return rows
}

/** Every node in the tree, depth-first, regardless of what is expanded. */
export const allNodes = (rootMembers: readonly FamilyTreeNode[]): FamilyTreeNode[] =>
  rootMembers.flatMap((node) => [node, ...allNodes(node.children)])

export const findNode = (
  rootMembers: readonly FamilyTreeNode[],
  id: string,
): FamilyTreeNode | undefined => allNodes(rootMembers).find((node) => node.id === id)

/** Total descendants beneath a node. The API does not return this, so it is derived here. */
export const descendantCount = (node: FamilyTreeNode): number =>
  node.children.reduce((total, child) => total + 1 + descendantCount(child), 0)

/**
 * Every descendant of `id`, so the move dialog can grey out the targets the server would
 * refuse. A courtesy, not the rule: the server's cycle CTE remains the only authority, and a
 * client working from a stale tree still gets a translated 409.
 *
 * The member themselves is excluded — they are disabled too, but for a reason the dialog
 * states differently.
 */
export const descendantIds = (
  rootMembers: readonly FamilyTreeNode[],
  id: string,
): Set<string> => {
  const subject = findNode(rootMembers, id)
  if (subject === undefined) return new Set()
  return new Set(allNodes(subject.children).map((node) => node.id))
}

/**
 * Ids of every ancestor of `id`, so revealing a search hit can open the branches above it.
 * Returns an empty array when the id is not in the tree.
 */
export const ancestorIds = (rootMembers: readonly FamilyTreeNode[], id: string): string[] => {
  const parentOf = new Map<string, string | null>()
  allNodes(rootMembers).forEach((node) => parentOf.set(node.id, node.parentId))
  if (!parentOf.has(id)) return []

  const chain: string[] = []
  let parent = parentOf.get(id) ?? null
  while (parent !== null) {
    chain.push(parent)
    parent = parentOf.get(parent) ?? null
  }
  return chain
}

export interface TreeStats {
  members: number
  generations: number
}

export const treeStats = (rootMembers: readonly FamilyTreeNode[]): TreeStats => {
  const nodes = allNodes(rootMembers)
  return {
    members: nodes.length,
    generations: nodes.reduce((deepest, node) => Math.max(deepest, node.generation), 0),
  }
}
