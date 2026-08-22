import { apiFetch } from '../../services/apiClient'
import type { LifeDetails } from './lifeDetails'
import type {
  FamilyMember,
  FamilyTreeSummary,
  FamilyTreeView,
  MemberSearchPage,
  TreeQueryParams,
} from './types'

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

  create: (name: string, parentId: string | null, life: LifeDetails): Promise<FamilyMember> =>
    apiFetch<FamilyMember>(MEMBERS, {
      method: 'POST',
      body: JSON.stringify({ name, parentId, ...life }),
    }),

  /**
   * Sends name, version, and the life details. parentId is deliberately absent: the server
   * rejects it outright (design spec §4.6), and re-parenting is the Phase 5 move command.
   *
   * The life details are replace-semantics on the server, so they are always sent in full —
   * omitting a cleared date would leave the old value in place and make an unmarked death
   * record impossible to correct.
   */
  update: (id: string, name: string, version: number, life: LifeDetails): Promise<FamilyMember> =>
    apiFetch<FamilyMember>(`${MEMBERS}/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ name, version, ...life }),
    }),

  remove: (id: string): Promise<void> => apiFetch<void>(`${MEMBERS}/${id}`, { method: 'DELETE' }),

  /**
   * `URLSearchParams` rather than string concatenation: Arabic queries need percent-encoding,
   * and a name containing `&` would otherwise split into two parameters.
   */
  search: (query: string, limit: number): Promise<MemberSearchPage> => {
    const params = new URLSearchParams({ q: query, limit: String(limit) })
    return apiFetch<MemberSearchPage>(`${MEMBERS}/search?${params}`)
  },

  summary: (): Promise<FamilyTreeSummary> => apiFetch<FamilyTreeSummary>(TREE),

  tree: (params?: TreeQueryParams): Promise<FamilyTreeView> =>
    apiFetch<FamilyTreeView>(treePath(params)),
}
