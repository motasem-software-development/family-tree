import { apiFetch } from '../../services/apiClient'
import type { User } from './types'

const USERS = '/api/v1/users'

export const usersApi = {
  list: (): Promise<User[]> => apiFetch<User[]>(USERS),

  create: (email: string, password: string, roleIds: string[]): Promise<User> =>
    apiFetch<User>(USERS, {
      method: 'POST',
      body: JSON.stringify({ email, password, roleIds }),
    }),

  update: (id: string, email: string, roleIds: string[]): Promise<User> =>
    apiFetch<User>(`${USERS}/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ email, roleIds }),
    }),

  setActive: (id: string, isActive: boolean): Promise<User> =>
    apiFetch<User>(`${USERS}/${id}/${isActive ? 'activate' : 'deactivate'}`, { method: 'POST' }),

  resetPassword: (id: string, password: string): Promise<User> =>
    apiFetch<User>(`${USERS}/${id}/password`, {
      method: 'POST',
      body: JSON.stringify({ password }),
    }),
}
