import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { ContactDetails } from './contactDetails'
import type { LifeDetails } from './lifeDetails'
import type { MemberFilters } from '../filters/filterParams'
import { membersApi } from './membersApi'
import type { Branch, FamilyMemberListItem, FamilyTreeView, TreeQueryParams } from './types'

export const memberKeys = {
  all: ['members'] as const,
  // The filters are part of the key: two filtered views are two different results, and caching
  // them under one key would show the previous filter's rows while the new ones load.
  list: (filters?: MemberFilters) => ['members', 'list', filters ?? {}] as const,
  tree: (filters?: MemberFilters, params?: TreeQueryParams) =>
    ['members', 'tree', filters ?? {}, params ?? {}] as const,
  // Nested under 'members' so a create/edit/delete invalidation refreshes search results too.
  search: (query: string, limit: number) => ['members', 'search', query, limit] as const,
  // Likewise nested: a move changes the tree's shape, and with it which members are branches
  // and how deep the tree goes.
  branches: (rootId?: string) => ['members', 'branches', rootId ?? null] as const,
  generations: (rootId?: string) => ['members', 'generations', rootId ?? null] as const,
}

export const useMembersQuery = (filters?: MemberFilters) =>
  useQuery<FamilyMemberListItem[]>({
    queryKey: memberKeys.list(filters),
    queryFn: () => membersApi.list(filters),
  })

export const useTreeQuery = (filters?: MemberFilters, params?: TreeQueryParams) =>
  useQuery<FamilyTreeView>({
    queryKey: memberKeys.tree(filters, params),
    queryFn: () => membersApi.tree(filters, params),
  })

export const useBranchesQuery = (rootId?: string) =>
  useQuery<Branch[]>({
    queryKey: memberKeys.branches(rootId),
    queryFn: () => membersApi.branches(rootId),
  })

export const useGenerationsQuery = (rootId?: string) =>
  useQuery<number[]>({
    queryKey: memberKeys.generations(rootId),
    queryFn: () => membersApi.generations(rootId),
  })

/**
 * Every mutation invalidates the whole members namespace. A create or delete changes both the
 * flat list and the nested tree, and an update changes the version every other view holds —
 * so partial invalidation would leave stale versions that fail the next write with a spurious
 * CONCURRENCY_CONFLICT.
 */
const useInvalidateMembers = () => {
  const queryClient = useQueryClient()
  return () => {
    void queryClient.invalidateQueries({ queryKey: memberKeys.all })
  }
}

export const useCreateMember = () => {
  const invalidate = useInvalidateMembers()
  return useMutation({
    mutationFn: ({
      name,
      parentId,
      life,
      contact,
    }: {
      name: string
      parentId: string | null
      life: LifeDetails
      contact: ContactDetails
    }) => membersApi.create(name, parentId, life, contact),
    onSuccess: invalidate,
  })
}

export const useUpdateMember = () => {
  const invalidate = useInvalidateMembers()
  return useMutation({
    mutationFn: ({
      id,
      name,
      version,
      life,
      contact,
    }: {
      id: string
      name: string
      version: number
      life: LifeDetails
      contact: ContactDetails
    }) => membersApi.update(id, name, version, life, contact),
    onSuccess: invalidate,
  })
}

export const useMoveMember = () => {
  const invalidate = useInvalidateMembers()
  return useMutation({
    mutationFn: ({
      id,
      parentId,
      version,
    }: {
      id: string
      parentId: string | null
      version: number
    }) => membersApi.move(id, parentId, version),
    // The whole members namespace, as every other mutation does: a move changes the tree's
    // shape, the moved member's version, and every ancestor path the search results carry.
    onSuccess: invalidate,
  })
}

export const useDeleteMember = () => {
  const invalidate = useInvalidateMembers()
  return useMutation({
    mutationFn: (id: string) => membersApi.remove(id),
    onSuccess: invalidate,
  })
}
