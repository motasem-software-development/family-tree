import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { usersApi } from './usersApi'
import type { User } from './types'

export const userKeys = {
  all: ['users'] as const,
}

export const useUsersQuery = () =>
  useQuery<User[]>({ queryKey: userKeys.all, queryFn: () => usersApi.list() })

/**
 * Invalidates roles too: a role's userCount changes whenever an assignment changes, so a
 * cached roles list would show a stale count immediately after editing a user.
 */
const useInvalidateUsers = () => {
  const queryClient = useQueryClient()
  return () => {
    void queryClient.invalidateQueries({ queryKey: userKeys.all })
    void queryClient.invalidateQueries({ queryKey: ['roles'] })
  }
}

export const useCreateUser = () => {
  const invalidate = useInvalidateUsers()
  return useMutation({
    mutationFn: ({ email, password, roleIds }: {
      email: string; password: string; roleIds: string[]
    }) => usersApi.create(email, password, roleIds),
    onSuccess: invalidate,
  })
}

export const useUpdateUser = () => {
  const invalidate = useInvalidateUsers()
  return useMutation({
    mutationFn: ({ id, email, roleIds }: { id: string; email: string; roleIds: string[] }) =>
      usersApi.update(id, email, roleIds),
    onSuccess: invalidate,
  })
}

export const useSetUserActive = () => {
  const invalidate = useInvalidateUsers()
  return useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) =>
      usersApi.setActive(id, isActive),
    onSuccess: invalidate,
  })
}

export const useResetUserPassword = () => {
  const invalidate = useInvalidateUsers()
  return useMutation({
    mutationFn: ({ id, password }: { id: string; password: string }) =>
      usersApi.resetPassword(id, password),
    onSuccess: invalidate,
  })
}
