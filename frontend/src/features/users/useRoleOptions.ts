import { useQuery } from '@tanstack/react-query'
import { apiFetch } from '../../services/apiClient'

export type RoleOption = { id: string; name: string }

/**
 * Temporary: replaced by useRolesQuery in the roles feature. Same query key, so the cache
 * entry is shared and the swap is invisible to consumers.
 */
export const useRoleOptionsQuery = () =>
  useQuery<RoleOption[]>({
    queryKey: ['roles'],
    queryFn: () => apiFetch<RoleOption[]>('/api/v1/roles'),
  })
