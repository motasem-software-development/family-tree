export interface FamilyMember {
  id: string
  name: string
  parentId: string | null
  /** Optimistic concurrency token. Echo it back on update or the server rejects the write. */
  version: number
  createdAt: string
  updatedAt: string
}

export interface FamilyTreeNode {
  id: string
  name: string
  parentId: string | null
  generation: number
  /** True when children exist but were not returned because of a depth limit. */
  hasMoreChildren: boolean
  children: FamilyTreeNode[]
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

export interface TreeQueryParams {
  rootId?: string
  maxDepth?: number
}
