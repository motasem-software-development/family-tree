export interface FamilyMember {
  id: string
  name: string
  parentId: string | null
  /** Optimistic concurrency token. Echo it back on update or the server rejects the write. */
  version: number
  createdAt: string
  updatedAt: string
  /** ISO `yyyy-MM-dd`, Gregorian. Null when unknown — the norm for the imported tree. */
  dateOfBirth: string | null
  /** ISO `yyyy-MM-dd`, Gregorian. Null when unknown, including for a member known to have died. */
  dateOfDeath: string | null
  /** Explicit, not derived from `dateOfDeath`: "died, date unknown" is a real record. */
  isDeceased: boolean
  /** Exactly 9 digits, or null when not recorded. Text, so a leading zero survives. */
  nationalId: string | null
  /** Normalized E.164, dialing code included. Null when not recorded. */
  mobileNumber: string | null
  /** Normalized E.164. Independent of `mobileNumber` — they are often different numbers. */
  whatsAppNumber: string | null
  countryId: number | null
  /** ISO alpha-2 for `countryId`, so a row can render a flag without loading the country list. */
  countryCode: string | null
}

/**
 * One row of the filtered members list. Superset of `FamilyMember`: the single-member endpoints
 * have no selected root to measure from, so they return the narrower shape.
 */
export interface FamilyMemberListItem extends FamilyMember {
  /** Null for the root member, which specification §21 renders as "Root". */
  branchId: string | null
  branchName: string | null
  /** Root-relative — the selected root reads 0 (design spec §1.2). */
  generation: number
}

export interface FamilyTreeNode {
  id: string
  name: string
  parentId: string | null
  /** Absolute and 1-based, even under a root-relative generation filter (design spec §1.2). */
  generation: number
  /** True when children exist but were not returned because of a depth limit. */
  hasMoreChildren: boolean
  /**
   * False when this member is present only to hold up a matching descendant: they are rendered
   * dimmed and non-selectable. Always true with no filter applied (design spec §4.2).
   */
  matches: boolean
  children: FamilyTreeNode[]
}

/** One direct child of the selected root — a value the branch filter can take. */
export interface Branch {
  id: string
  name: string
}

export interface FamilyTreeView {
  id: string
  name: string
  rootMembers: FamilyTreeNode[]
}

export interface FamilyTreeSummary {
  id: string
  name: string
  memberCount: number
}

/**
 * `maxDepth` is a transport concern — how much of the tree to ship — and stays outside the
 * filter set it travels beside (design spec §5.1).
 */
export interface TreeQueryParams {
  maxDepth?: number
}

export interface MemberAncestor {
  id: string
  name: string
}

export interface MemberSearchHit {
  id: string
  name: string
  generation: number
  /** Root first, excluding the hit itself. Empty for a first-generation member. */
  ancestors: MemberAncestor[]
}

export interface MemberSearchPage {
  /** Every match on the server, not the length of `items`. */
  total: number
  items: MemberSearchHit[]
}
