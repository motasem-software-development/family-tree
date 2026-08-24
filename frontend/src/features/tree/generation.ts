import type { FamilyTreeNode } from '../members/types'

/**
 * Generation numbering has two schemes, and design spec §1.2 keeps both.
 *
 * `FamilyTreeNodeResponse.Generation` is **absolute** and 1-based: a parentless member is
 * generation 1. The reports page and the PDF caption read it, and both are tree-wide with no
 * selected root, so it stays as it is.
 *
 * The generation **filter** is **root-relative**: the selected root reads 0, matching
 * specification §21's table. The two display sites on the tree page follow the filter rather
 * than the field, so that a page cannot contradict its own filter.
 */

/**
 * The absolute generation the current view is rooted at.
 *
 * Read off the view rather than assumed. Spec §1.2 justifies the change with "the two schemes
 * differ by exactly one", which is true of this data — one parentless member — but not of the
 * rule: with a root selected, subtracting one would be silently wrong. Deriving the offset costs
 * nothing and stays right.
 *
 * Falls back to 1 for an empty or still-loading view, so nothing renders a negative generation
 * in the gap before the tree arrives.
 */
export const rootGenerationOf = (roots: readonly FamilyTreeNode[]): number =>
  roots[0]?.generation ?? 1

/** The generation as the filter counts it: 0 at the root of the current view. */
export const rootRelative = (absolute: number, rootGeneration: number): number =>
  absolute - rootGeneration
