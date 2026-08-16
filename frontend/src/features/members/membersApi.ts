import { apiFetch } from '../../services/apiClient'
import type { FamilyMember, FamilyTreeSummary, FamilyTreeView, TreeQueryParams } from './types'

const MEMBERS = '/api/v1/family-members'
const TREE = '/api/v1/family-tree'

const treePath = (params?: TreeQueryParams): string => {
  const query = new URLSearchParams()
  if (params?.rootId) query.set('rootId', params.rootId)
  if (params?.maxDepth !== undefined) query.set('maxDepth', String(params.maxDepth))
  const suffix = query.toString()
  return suffix ? `${TREE}/view?${suffix}` : `${TREE}/view`
}

export const membersApi = {
  list: (): Promise<FamilyMember[]> => apiFetch<FamilyMember[]>(MEMBERS),

  create: (name: string, parentId: string | null): Promise<FamilyMember> =>
    apiFetch<FamilyMember>(MEMBERS, {
      method: 'POST',
      body: JSON.stringify({ name, parentId }),
    }),

  /**
   * Sends only name and version. parentId is deliberately absent: the server rejects it
   * outright (design spec §4.6), and re-parenting is the Phase 5 move command.
   */
  update: (id: string, name: string, version: number): Promise<FamilyMember> =>
    apiFetch<FamilyMember>(`${MEMBERS}/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ name, version }),
    }),

  remove: (id: string): Promise<void> => apiFetch<void>(`${MEMBERS}/${id}`, { method: 'DELETE' }),

  summary: (): Promise<FamilyTreeSummary> => apiFetch<FamilyTreeSummary>(TREE),

  tree: (params?: TreeQueryParams): Promise<FamilyTreeView> =>
    apiFetch<FamilyTreeView>(treePath(params)),
}
