import { apiFetch } from '../../services/apiClient'
import type { Permission, Role } from './types'

const ROLES = '/api/v1/roles'

export const rolesApi = {
  list: (): Promise<Role[]> => apiFetch<Role[]>(ROLES),

  permissions: (): Promise<Permission[]> => apiFetch<Permission[]>('/api/v1/permissions'),

  create: (name: string, description: string | null, permissions: string[]): Promise<Role> =>
    apiFetch<Role>(ROLES, {
      method: 'POST',
      body: JSON.stringify({ name, description, permissions }),
    }),

  update: (
    id: string, name: string, description: string | null, permissions: string[],
  ): Promise<Role> =>
    apiFetch<Role>(`${ROLES}/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ name, description, permissions }),
    }),

  remove: (id: string): Promise<void> => apiFetch<void>(`${ROLES}/${id}`, { method: 'DELETE' }),
}
