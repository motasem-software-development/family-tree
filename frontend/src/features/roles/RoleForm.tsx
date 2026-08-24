import { useState, type CSSProperties, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { usePermissionsQuery } from './useRoles'
import type { Permission, Role } from './types'

interface RoleFormProps {
  /** Present when editing; absent when adding. Editing a system role is never offered, so this
      form does not need to special-case isSystem — RolesPage keeps it out of reach. */
  role?: Role
  isSaving: boolean
  onSubmit: (name: string, description: string | null, permissions: string[]) => void
  onCancel: () => void
}

/** Permission codes are `Group.Action` — the prefix drives both grouping and fallback labels. */
const GROUP_ORDER = ['FamilyTree', 'Member', 'User', 'Role', 'Audit', 'PublicLink']

const groupOf = (code: string): string => code.split('.')[0] ?? code

const groupPermissions = (permissions: Permission[]): [string, Permission[]][] => {
  const byGroup = new Map<string, Permission[]>()
  for (const permission of permissions) {
    const group = groupOf(permission.code)
    const bucket = byGroup.get(group) ?? []
    byGroup.set(group, [...bucket, permission])
  }
  const orderedGroups = [
    ...GROUP_ORDER.filter((group) => byGroup.has(group)),
    ...[...byGroup.keys()].filter((group) => !GROUP_ORDER.includes(group)),
  ]
  return orderedGroups.map((group) => [group, byGroup.get(group) ?? []])
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

const groupHeadingStyle: CSSProperties = {
  fontSize: 13,
  fontWeight: 600,
  color: 'var(--text-2)',
  margin: '0 0 8px',
}

const checkboxRowStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  fontSize: 13,
  padding: '4px 0',
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

export function RoleForm({ role, isSaving, onSubmit, onCancel }: RoleFormProps) {
  const { t } = useTranslation()
  const { data: catalog } = usePermissionsQuery()
  const [name, setName] = useState(role?.name ?? '')
  const [description, setDescription] = useState(role?.description ?? '')
  const [permissions, setPermissions] = useState<string[]>(role?.permissions ?? [])

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault()
    onSubmit(name, description.trim() === '' ? null : description, permissions)
  }

  const togglePermission = (code: string) => {
    setPermissions((current) =>
      current.includes(code) ? current.filter((existing) => existing !== code) : [...current, code],
    )
  }

  const groups = groupPermissions(catalog ?? [])

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
        <label htmlFor="role-name" style={labelStyle}>
          {t('roles.name')}
        </label>
        <input
          id="role-name"
          value={name}
          onChange={(event) => setName(event.target.value)}
          maxLength={100}
          required
          style={controlStyle}
        />
      </div>

      <div style={{ marginBottom: 'var(--space-4)' }}>
        <label htmlFor="role-description" style={labelStyle}>
          {t('roles.description')}
        </label>
        <input
          id="role-description"
          value={description}
          onChange={(event) => setDescription(event.target.value)}
          style={controlStyle}
        />
      </div>

      <div style={{ marginBottom: 'var(--space-4)' }}>
        <span style={labelStyle}>{t('roles.permissions')}</span>
        <div
          style={{
            display: 'grid',
            // min() rather than a bare 220px: below that width the track would otherwise
            // overflow its own container instead of collapsing to a single column.
            gridTemplateColumns: 'repeat(auto-fit, minmax(min(220px, 100%), 1fr))',
            gap: 'var(--space-4)',
          }}
        >
          {groups.map(([group, groupPerms]) => (
            <div key={group}>
              <p style={groupHeadingStyle}>{t(`permissionGroups.${group}`, group)}</p>
              {groupPerms.map((permission) => {
                const inputId = `permission-${permission.code}`
                return (
                  <div key={permission.code} style={checkboxRowStyle}>
                    <input
                      id={inputId}
                      type="checkbox"
                      checked={permissions.includes(permission.code)}
                      onChange={() => togglePermission(permission.code)}
                    />
                    <label htmlFor={inputId}>
                      {t(`permissions.${permission.code}`, permission.code)}
                    </label>
                  </div>
                )
              })}
            </div>
          ))}
        </div>
      </div>

      <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
        <button type="button" onClick={onCancel} style={buttonStyle(false, false)}>
          {t('roles.cancel')}
        </button>
        <button type="submit" disabled={isSaving} style={buttonStyle(true, isSaving)}>
          {isSaving ? t('roles.saving') : t('roles.save')}
        </button>
      </div>
    </form>
  )
}
