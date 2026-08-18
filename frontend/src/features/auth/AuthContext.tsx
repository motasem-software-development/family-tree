import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type PropsWithChildren,
} from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiFetch, onSessionEnded } from '../../services/apiClient'
import { tokenStorage } from '../../services/tokenStorage'

export interface CurrentUser {
  id: string
  email: string
  tenantId: string
  familyTreeName: string
  permissions: string[]
  mustChangePassword: boolean
}

interface AuthContextValue {
  user: CurrentUser | null
  isLoading: boolean
  mustChangePassword: boolean
  hasPermission: (permission: string) => boolean
  login: (email: string, password: string) => Promise<void>
  logout: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

export const AuthProvider = ({ children }: PropsWithChildren) => {
  const queryClient = useQueryClient()

  // Sign-out must take effect the instant it happens, not whenever the ['me'] query observer
  // next happens to notice tokenStorage.clear() / queryClient.clear() — neither of those is
  // guaranteed to re-render this provider on its own. This flag is the one thing that reliably
  // does: setting it forces the state update React re-renders on, so `user` below flips to
  // null in the same render pass that handles the click, on every screen that reads it.
  const [isSignedOut, setIsSignedOut] = useState(false)

  const { data, isLoading } = useQuery({
    queryKey: ['me'],
    queryFn: () => apiFetch<CurrentUser>('/api/v1/me'),
    enabled: !isSignedOut && tokenStorage.read() !== null,
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
      // Re-arm the query: a fresh sign-in (including the re-login ChangePasswordPage performs
      // once the flag clears) must be able to fetch ['me'] again.
      setIsSignedOut(false)
    },
    [loginMutation],
  )

  // The single "this session is over" path. Both triggers run it: the sign-out button below,
  // and apiClient discovering that a token refresh failed. Keeping one implementation is the
  // point — a refresh failure that only cleared tokenStorage left `user` non-null forever, so
  // ProtectedRoute never redirected and the app stayed rendered over a dead session.
  const endSession = useCallback(() => {
    tokenStorage.clear()
    queryClient.clear()
    setIsSignedOut(true)
  }, [queryClient])

  useEffect(() => onSessionEnded(endSession), [endSession])

  const logout = useCallback(async () => {
    const tokens = tokenStorage.read()
    if (tokens) {
      // Best-effort revocation; the local session ends either way.
      await apiFetch('/api/v1/auth/logout', {
        method: 'POST',
        body: JSON.stringify({ refreshToken: tokens.refreshToken }),
      }).catch(() => undefined)
    }
    endSession()
  }, [endSession])

  const value = useMemo<AuthContextValue>(
    () => ({
      // isSignedOut wins outright: once it is set, stale query data must never leak back into
      // `user` for even one render, no matter what the query observer still holds.
      user: isSignedOut ? null : (data ?? null),
      isLoading: !isSignedOut && tokenStorage.read() !== null && isLoading,
      // Default false, never true: while the query is pending there is no signal yet, and
      // defaulting true would flash the change-password screen on every page load.
      mustChangePassword: isSignedOut ? false : (data?.mustChangePassword ?? false),
      hasPermission: (permission) =>
        !isSignedOut && (data?.permissions.includes(permission) ?? false),
      login,
      logout,
    }),
    [data, isLoading, isSignedOut, login, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export const useAuth = (): AuthContextValue => {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside AuthProvider')
  return context
}
