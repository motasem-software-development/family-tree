import { useState, type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import { AppShell } from '../../app/AppShell'
import { useAuth } from '../auth/AuthContext'
import { ApiError } from '../../services/apiClient'
import { RoleForm } from './RoleForm'
import { useCreateRole, useDeleteRole, useRolesQuery, useUpdateRole } from './useRoles'
import type { Role } from './types'

type Editing = { mode: 'none' } | { mode: 'add' } | { mode: 'edit'; role: Role }

const codeOf = (error: unknown): string => (error instanceof ApiError ? error.code : 'UNKNOWN')

const cellStyle: CSSProperties = {
  padding: '11px 14px',
  fontSize: 14,
  textAlign: 'start',
  borderBottom: '1px solid var(--divider)',
}

const headCellStyle: CSSProperties = {
  ...cellStyle,
  fontSize: 12,
  fontWeight: 600,
  color: 'var(--text-3)',
  background: 'var(--sunken)',
  whiteSpace: 'nowrap',
}

const rowButtonStyle = (danger: boolean): CSSProperties => ({
  height: 'var(--control-h-sm)',
  padding: '0 12px',
  border: `1px solid ${danger ? 'var(--error)' : 'var(--border-strong)'}`,
  borderRadius: 'var(--r-md)',
  background: 'var(--surface)',
  color: danger ? 'var(--error)' : 'var(--text-1)',
  fontFamily: 'inherit',
  fontSize: 13,
  cursor: 'pointer',
})

const badgeStyle: CSSProperties = {
  display: 'inline-block',
  marginInlineStart: 8,
  padding: '2px 8px',
  borderRadius: 'var(--r-sm)',
  fontSize: 11,
  fontWeight: 600,
  background: 'var(--warning-subtle, #FDF0D5)',
  color: 'var(--warning, #9A6700)',
}

export function RolesPage() {
  const { t } = useTranslation()
  const { user, hasPermission } = useAuth()
  const { data: roles, isLoading } = useRolesQuery()
  const createRole = useCreateRole()
  const updateRole = useUpdateRole()
  const deleteRole = useDeleteRole()

  const [editing, setEditing] = useState<Editing>({ mode: 'none' })
  const [pendingDelete, setPendingDelete] = useState<Role | null>(null)
  const [errorCode, setErrorCode] = useState<string | null>(null)

  const close = () => setEditing({ mode: 'none' })

  const handleCreate = (name: string, description: string | null, permissions: string[]) => {
    setErrorCode(null)
    createRole.mutate(
      { name, description, permissions },
      { onSuccess: close, onError: (error) => setErrorCode(codeOf(error)) },
    )
  }

  const handleUpdate = (
    target: Role, name: string, description: string | null, permissions: string[],
  ) => {
    setErrorCode(null)
    updateRole.mutate(
      { id: target.id, name, description, permissions },
      { onSuccess: close, onError: (error) => setErrorCode(codeOf(error)) },
    )
  }

  const confirmDelete = () => {
    if (pendingDelete === null) return
    setErrorCode(null)
    deleteRole.mutate(pendingDelete.id, {
      onSettled: () => setPendingDelete(null),
      onError: (error) => setErrorCode(codeOf(error)),
    })
  }

  const all = roles ?? []
  const familyName = user?.familyTreeName ?? ''

  return (
    <AppShell familyName={familyName} statLine={t('roles.count', { count: all.length })}>
      <div style={{ flex: 1, minWidth: 0, overflow: 'auto', padding: 'var(--space-8)' }}>
        <div style={{ maxWidth: 900, margin: '0 auto' }}>
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: 'var(--space-4)',
              marginBottom: 'var(--space-6)',
            }}
          >
            <h1 style={{ margin: 0, fontSize: 22, fontWeight: 700 }}>{t('roles.title')}</h1>
            {hasPermission('Role.Create') && editing.mode === 'none' && (
              <button
                type="button"
                onClick={() => setEditing({ mode: 'add' })}
                style={{
                  height: 'var(--control-h-md)',
                  padding: '0 16px',
                  border: 'none',
                  borderRadius: 'var(--r-md)',
                  background: 'var(--primary)',
                  color: '#fff',
                  fontFamily: 'inherit',
                  fontSize: 13,
                  fontWeight: 600,
                  cursor: 'pointer',
                }}
              >
                {t('roles.add')}
              </button>
            )}
          </div>

          {/* Error text comes from the stable server code, never from the server's message —
              the UI is bilingual and message text is not part of the contract. */}
          {errorCode !== null && (
            <p
              role="alert"
              style={{
                margin: '0 0 var(--space-5)',
                padding: '10px 12px',
                borderRadius: 'var(--r-md)',
                background: 'var(--error-subtle)',
                color: 'var(--error)',
                fontSize: 13,
              }}
            >
              {t(`errors.${errorCode}`, t('errors.UNKNOWN'))}
            </p>
          )}

          {editing.mode === 'add' && (
            <RoleForm isSaving={createRole.isPending} onSubmit={handleCreate} onCancel={close} />
          )}

          {editing.mode === 'edit' && (
            <RoleForm
              role={editing.role}
              isSaving={updateRole.isPending}
              onSubmit={(name, description, permissions) =>
                handleUpdate(editing.role, name, description, permissions)}
              onCancel={close}
            />
          )}

          {isLoading ? (
            <p style={{ color: 'var(--text-3)' }}>{t('roles.loading')}</p>
          ) : all.length === 0 ? (
            <div
              style={{
                padding: 'var(--space-12) var(--space-6)',
                textAlign: 'center',
                color: 'var(--text-3)',
                background: 'var(--surface)',
                border: '1px solid var(--border)',
                borderRadius: 'var(--r-lg)',
              }}
            >
              {t('roles.empty')}
            </div>
          ) : (
            <div
              style={{
                background: 'var(--surface)',
                border: '1px solid var(--border)',
                borderRadius: 'var(--r-lg)',
                overflow: 'hidden',
              }}
            >
              <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                <thead>
                  <tr>
                    <th style={headCellStyle}>{t('roles.name')}</th>
                    <th style={headCellStyle}>{t('roles.description')}</th>
                    <th style={headCellStyle}>{t('roles.members')}</th>
                    <th style={headCellStyle} />
                  </tr>
                </thead>
                <tbody>
                  {all.map((current) => (
                    <tr key={current.id}>
                      <td style={{ ...cellStyle, fontWeight: 500 }}>
                        {current.name}
                        {current.isSystem && <span style={badgeStyle}>{t('roles.systemRole')}</span>}
                      </td>
                      <td style={{ ...cellStyle, color: 'var(--text-3)' }}>
                        {current.description ?? ''}
                      </td>
                      <td style={cellStyle}>{current.userCount}</td>
                      <td style={{ ...cellStyle, whiteSpace: 'nowrap' }}>
                        {current.isSystem ? (
                          <span style={{ fontSize: 12, color: 'var(--text-3)' }}>
                            {t('roles.systemRoleHint')}
                          </span>
                        ) : (
                          <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
                            {hasPermission('Role.Edit') && (
                              <button
                                type="button"
                                onClick={() => setEditing({ mode: 'edit', role: current })}
                                style={rowButtonStyle(false)}
                              >
                                {t('roles.edit')}
                              </button>
                            )}
                            {hasPermission('Role.Delete') && (
                              <button
                                type="button"
                                onClick={() => setPendingDelete(current)}
                                style={rowButtonStyle(true)}
                              >
                                {t('roles.delete')}
                              </button>
                            )}
                          </div>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>

      {/* An in-app dialog rather than window.confirm: the native one cannot be styled, ignores
          the app's direction, and renders in the browser's language, not the app's. */}
      {pendingDelete !== null && (
        <div
          style={{
            position: 'fixed',
            inset: 0,
            background: 'rgba(21,24,27,.36)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            zIndex: 400,
            padding: 24,
          }}
        >
          <div
            role="dialog"
            aria-modal="true"
            aria-label={t('roles.confirmDelete', { name: pendingDelete.name })}
            style={{
              width: '100%',
              maxWidth: 420,
              padding: 'var(--space-6)',
              background: 'var(--surface)',
              borderRadius: 'var(--r-lg)',
              boxShadow: 'var(--shadow-high)',
              animation: 'fadeUp var(--motion-base) var(--ease-standard)',
            }}
          >
            <div style={{ fontSize: 17, fontWeight: 600, marginBottom: 8 }}>
              {t('roles.confirmDelete', { name: pendingDelete.name })}
            </div>
            <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
              <button
                type="button"
                onClick={() => setPendingDelete(null)}
                style={{
                  height: 38,
                  padding: '0 16px',
                  border: '1px solid var(--border-strong)',
                  borderRadius: 'var(--r-md)',
                  background: 'var(--surface)',
                  fontFamily: 'inherit',
                  fontSize: 13,
                  fontWeight: 500,
                  cursor: 'pointer',
                }}
              >
                {t('roles.cancel')}
              </button>
              <button
                type="button"
                onClick={confirmDelete}
                disabled={deleteRole.isPending}
                style={{
                  height: 38,
                  padding: '0 16px',
                  border: 'none',
                  borderRadius: 'var(--r-md)',
                  background: 'var(--error)',
                  color: '#fff',
                  fontFamily: 'inherit',
                  fontSize: 13,
                  fontWeight: 500,
                  cursor: deleteRole.isPending ? 'wait' : 'pointer',
                  opacity: deleteRole.isPending ? 0.7 : 1,
                }}
              >
                {t('roles.delete')}
              </button>
            </div>
          </div>
        </div>
      )}
    </AppShell>
  )
}
