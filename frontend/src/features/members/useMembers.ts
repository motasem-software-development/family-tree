import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { ContactDetails } from './contactDetails'
import type { LifeDetails } from './lifeDetails'
import { membersApi } from './membersApi'
import type { FamilyMember, FamilyTreeView, TreeQueryParams } from './types'

export const memberKeys = {
  all: ['members'] as const,
  tree: (params?: TreeQueryParams) => ['members', 'tree', params ?? {}] as const,
  // Nested under 'members' so a create/edit/delete invalidation refreshes search results too.
  search: (query: string, limit: number) => ['members', 'search', query, limit] as const,
}

export const useMembersQuery = () =>
  useQuery<FamilyMember[]>({ queryKey: memberKeys.all, queryFn: () => membersApi.list() })

export const useTreeQuery = (params?: TreeQueryParams) =>
  useQuery<FamilyTreeView>({
    queryKey: memberKeys.tree(params),
    queryFn: () => membersApi.tree(params),
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
