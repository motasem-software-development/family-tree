import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { membersApi } from './membersApi'
import type { FamilyMember, FamilyTreeView, TreeQueryParams } from './types'

export const memberKeys = {
  all: ['members'] as const,
  tree: (params?: TreeQueryParams) => ['members', 'tree', params ?? {}] as const,
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
    mutationFn: ({ name, parentId }: { name: string; parentId: string | null }) =>
      membersApi.create(name, parentId),
    onSuccess: invalidate,
  })
}

export const useUpdateMember = () => {
  const invalidate = useInvalidateMembers()
  return useMutation({
    mutationFn: ({ id, name, version }: { id: string; name: string; version: number }) =>
      membersApi.update(id, name, version),
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
