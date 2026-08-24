import { apiFetch } from '../../services/apiClient'
import { toFilterParams, type MemberFilters } from '../filters/filterParams'
import type { ContactDetails } from './contactDetails'
import type { LifeDetails } from './lifeDetails'
import type {
  Branch,
  FamilyMember,
  FamilyMemberListItem,
  FamilyTreeSummary,
  FamilyTreeView,
  MemberSearchPage,
  TreeQueryParams,
} from './types'

const MEMBERS = '/api/v1/family-members'
const TREE = '/api/v1/family-tree'

/** Appends a query string only when there is one, so an unfiltered URL stays clean. */
const withQuery = (path: string, query: URLSearchParams): string => {
  const suffix = query.toString()
  return suffix ? `${path}?${suffix}` : path
}

const treePath = (filters?: MemberFilters, params?: TreeQueryParams): string => {
  const query = toFilterParams(filters ?? {})
  if (params?.maxDepth !== undefined) query.set('maxDepth', String(params.maxDepth))
  return withQuery(`${TREE}/view`, query)
}

/**
 * The reference lists take only the root: they answer what is available to filter by, so
 * narrowing them by the current filter would build a dropdown that erases its own options.
 */
const rootQuery = (rootId?: string): URLSearchParams => {
  const query = new URLSearchParams()
  if (rootId) query.set('rootId', rootId)
  return query
}

export const membersApi = {
  /**
   * The members list, filtered server-side. Rows carry branch and generation, which the
   * single-member endpoints do not — they have no selected root to measure from.
   */
  list: (filters?: MemberFilters): Promise<FamilyMemberListItem[]> =>
    apiFetch<FamilyMemberListItem[]>(withQuery(MEMBERS, toFilterParams(filters ?? {}))),

  create: (
    name: string,
    parentId: string | null,
    life: LifeDetails,
    contact: ContactDetails,
  ): Promise<FamilyMember> =>
    apiFetch<FamilyMember>(MEMBERS, {
      method: 'POST',
      body: JSON.stringify({ name, parentId, ...life, ...contact }),
    }),

  /**
   * Sends name, version, and the life details. parentId is deliberately absent: the server
   * rejects it outright (design spec §4.6); re-parenting goes through `membersApi.move` instead.
   *
   * The life details are replace-semantics on the server, so they are always sent in full —
   * omitting a cleared date would leave the old value in place and make an unmarked death
   * record impossible to correct. The contact details are replace-semantics for the same
   * reason — omitting a cleared phone number would leave the old one in place.
   */
  update: (
    id: string,
    name: string,
    version: number,
    life: LifeDetails,
    contact: ContactDetails,
  ): Promise<FamilyMember> =>
    apiFetch<FamilyMember>(`${MEMBERS}/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ name, version, ...life, ...contact }),
    }),

  /**
   * Re-parents a member; a null parentId promotes them to the first generation. A dedicated
   * command, not a field on update: the server rejects parentId on PUT outright (design spec
   * §4.6), because a move carries a rule no other edit does.
   */
  move: (id: string, parentId: string | null, version: number): Promise<FamilyMember> =>
    apiFetch<FamilyMember>(`${MEMBERS}/${id}/move`, {
      method: 'POST',
      body: JSON.stringify({ parentId, version }),
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

  tree: (filters?: MemberFilters, params?: TreeQueryParams): Promise<FamilyTreeView> =>
    apiFetch<FamilyTreeView>(treePath(filters, params)),

  branches: (rootId?: string): Promise<Branch[]> =>
    apiFetch<Branch[]>(withQuery(`${TREE}/branches`, rootQuery(rootId))),

  generations: (rootId?: string): Promise<number[]> =>
    apiFetch<number[]>(withQuery(`${TREE}/generations`, rootQuery(rootId))),
}
