import { useState, type CSSProperties, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useRoleOptionsQuery } from './useRoleOptions'
import type { User } from './types'

interface UserFormProps {
  /** Present when editing; absent when adding. */
  user?: User
  isSaving: boolean
  onSubmit: (email: string, password: string, roleIds: string[]) => void
  onCancel: () => void
}

const labelStyle: CSSProperties = {
  display: 'block',
  marginBottom: 6,
  fontSize: 13,
  fontWeight: 500,
  color: 'var(--text-2)',
}

const controlStyle: CSSProperties = {
  width: '100%',
  height: 'var(--control-h-md)',
  padding: '0 12px',
  border: '1px solid var(--border-strong)',
  borderRadius: 'var(--r-md)',
  background: 'var(--surface)',
  color: 'var(--text-1)',
  fontFamily: 'inherit',
  fontSize: 14,
}

const multiSelectStyle: CSSProperties = {
  ...controlStyle,
  height: 'auto',
  minHeight: 96,
  padding: 6,
}

const buttonStyle = (primary: boolean, busy: boolean): CSSProperties => ({
  height: 38,
  padding: '0 16px',
  border: primary ? 'none' : '1px solid var(--border-strong)',
  borderRadius: 'var(--r-md)',
  background: primary ? 'var(--primary)' : 'var(--surface)',
  color: primary ? '#fff' : 'var(--text-1)',
  fontFamily: 'inherit',
  fontSize: 13,
  fontWeight: 500,
  cursor: busy ? 'wait' : 'pointer',
  opacity: busy ? 0.7 : 1,
})

export function UserForm({ user, isSaving, onSubmit, onCancel }: UserFormProps) {
  const { t } = useTranslation()
  const { data: roleOptions } = useRoleOptionsQuery()
  const [email, setEmail] = useState(user?.email ?? '')
  const [password, setPassword] = useState('')
  const [roleIds, setRoleIds] = useState<string[]>(user?.roles.map((role) => role.id) ?? [])

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault()
    onSubmit(email, password, roleIds)
  }

  const handleRolesChange = (event: React.ChangeEvent<HTMLSelectElement>) => {
    setRoleIds(Array.from(event.target.selectedOptions, (option) => option.value))
  }

  return (
    <form
      onSubmit={handleSubmit}
      style={{
        marginBottom: 'var(--space-5)',
        padding: 'var(--space-5)',
        background: 'var(--surface)',
        border: '1px solid var(--border)',
        borderRadius: 'var(--r-lg)',
        boxShadow: 'var(--shadow-low)',
        animation: 'fadeUp var(--motion-base) var(--ease-standard)',
      }}
    >
      <div style={{ marginBottom: 'var(--space-4)' }}>
        <label htmlFor="user-email" style={labelStyle}>
          {t('users.email')}
        </label>
        <input
          id="user-email"
          type="email"
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          required
          style={controlStyle}
        />
      </div>

      {/* The temporary password is only meaningful at creation: an existing user changes their
          own password, and an administrator resets it separately via the row action. */}
      {user === undefined && (
        <div style={{ marginBottom: 'var(--space-4)' }}>
          <label htmlFor="user-password" style={labelStyle}>
            {t('users.password')}
          </label>
          <input
            id="user-password"
            type="password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            required
            style={controlStyle}
          />
        </div>
      )}

      <div style={{ marginBottom: 'var(--space-4)' }}>
        <label htmlFor="user-roles" style={labelStyle}>
          {t('users.roles')}
        </label>
        <select
          id="user-roles"
          multiple
          value={roleIds}
          onChange={handleRolesChange}
          style={multiSelectStyle}
        >
          {(roleOptions ?? []).map((role) => (
            <option key={role.id} value={role.id}>
              {role.name}
            </option>
          ))}
        </select>
      </div>

      <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
        <button type="button" onClick={onCancel} style={buttonStyle(false, false)}>
          {t('users.cancel')}
        </button>
        <button type="submit" disabled={isSaving} style={buttonStyle(true, isSaving)}>
          {isSaving ? t('users.saving') : t('users.save')}
        </button>
      </div>
    </form>
  )
}
