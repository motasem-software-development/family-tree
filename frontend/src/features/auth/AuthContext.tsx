import { createContext, useCallback, useContext, useMemo, type PropsWithChildren } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch } from '../../services/apiClient'
import { tokenStorage } from '../../services/tokenStorage'

export interface CurrentUser {
  id: string
  email: string
  tenantId: string
  familyTreeName: string
  permissions: string[]
}

interface AuthContextValue {
  user: CurrentUser | null
  isLoading: boolean
  hasPermission: (permission: string) => boolean
  login: (email: string, password: string) => Promise<void>
  logout: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

export const AuthProvider = ({ children }: PropsWithChildren) => {
  const queryClient = useQueryClient()

  const { data, isLoading } = useQuery({
    queryKey: ['me'],
    queryFn: () => apiFetch<CurrentUser>('/api/v1/me'),
    enabled: tokenStorage.read() !== null,
    retry: false,
  })

  const loginMutation = useMutation({
    mutationFn: async ({ email, password }: { email: string; password: string }) => {
      const tokens = await apiFetch<{ accessToken: string; refreshToken: string }>(
        '/api/v1/auth/login',
        { method: 'POST', body: JSON.stringify({ email, password }) },
      )
      tokenStorage.write(tokens)
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['me'] }),
  })

  const login = useCallback(
    async (email: string, password: string) => {
      await loginMutation.mutateAsync({ email, password })
    },
    [loginMutation],
  )

  const logout = useCallback(async () => {
    const tokens = tokenStorage.read()
    if (tokens) {
      // Best-effort revocation; the local session ends either way.
      await apiFetch('/api/v1/auth/logout', {
        method: 'POST',
        body: JSON.stringify({ refreshToken: tokens.refreshToken }),
      }).catch(() => undefined)
    }
    tokenStorage.clear()
    queryClient.clear()
  }, [queryClient])

  const value = useMemo<AuthContextValue>(
    () => ({
      user: data ?? null,
      isLoading: tokenStorage.read() !== null && isLoading,
      hasPermission: (permission) => data?.permissions.includes(permission) ?? false,
      login,
      logout,
    }),
    [data, isLoading, login, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export const useAuth = (): AuthContextValue => {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside AuthProvider')
  return context
}
