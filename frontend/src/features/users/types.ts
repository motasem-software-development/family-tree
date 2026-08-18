export type UserRoleSummary = {
  id: string
  name: string
}

export type User = {
  id: string
  email: string
  isActive: boolean
  mustChangePassword: boolean
  lastLoginAt: string | null
  roles: UserRoleSummary[]
}
