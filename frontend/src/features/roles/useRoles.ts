import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { rolesApi } from './rolesApi'
import type { Permission, Role } from './types'

export const roleKeys = {
  all: ['roles'] as const,
  permissions: ['permissions'] as const,
}

export const useRolesQuery = () =>
  useQuery<Role[]>({ queryKey: roleKeys.all, queryFn: () => rolesApi.list() })

/** The catalog is fixed for the life of a deployment, so it never needs refetching. */
export const usePermissionsQuery = () =>
  useQuery<Permission[]>({
    queryKey: roleKeys.permissions,
    queryFn: () => rolesApi.permissions(),
    staleTime: Infinity,
  })

/**
 * Invalidates users as well: changing a role's permissions changes what its members can do,
 * and renaming or deleting one changes what the users list displays.
 */
const useInvalidateRoles = () => {
  const queryClient = useQueryClient()
  return () => {
    void queryClient.invalidateQueries({ queryKey: roleKeys.all })
    void queryClient.invalidateQueries({ queryKey: ['users'] })
  }
}

export const useCreateRole = () => {
  const invalidate = useInvalidateRoles()
  return useMutation({
    mutationFn: ({ name, description, permissions }: {
      name: string; description: string | null; permissions: string[]
    }) => rolesApi.create(name, description, permissions),
    onSuccess: invalidate,
  })
}

export const useUpdateRole = () => {
  const invalidate = useInvalidateRoles()
  return useMutation({
    mutationFn: ({ id, name, description, permissions }: {
      id: string; name: string; description: string | null; permissions: string[]
    }) => rolesApi.update(id, name, description, permissions),
    onSuccess: invalidate,
  })
}

export const useDeleteRole = () => {
  const invalidate = useInvalidateRoles()
  return useMutation({
    mutationFn: (id: string) => rolesApi.remove(id),
    onSuccess: invalidate,
  })
}
