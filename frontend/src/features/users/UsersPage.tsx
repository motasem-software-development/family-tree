import { useState, type CSSProperties, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { AppShell } from '../../app/AppShell'
import { useAuth } from '../auth/AuthContext'
import { ApiError } from '../../services/apiClient'
import { UserForm } from './UserForm'
import {
  useCreateUser,
  useResetUserPassword,
  useSetUserActive,
  useUpdateUser,
  useUsersQuery,
} from './useUsers'
import type { User } from './types'

type Editing = { mode: 'none' } | { mode: 'add' } | { mode: 'edit'; user: User }

const codeOf = (error: unknown): string => (error instanceof ApiError ? error.code : 'UNKNOWN')

/**
 * Dates arrive as ISO strings. Rendered with the active locale so Arabic gets Arabic-Indic
 * numerals, per the design system's numeral rule — matches MemberPanel's formatDate.
 */
const formatDate = (iso: string, locale: string): string => {
  const parsed = new Date(iso)
  if (Number.isNaN(parsed.getTime())) return iso
  return parsed.toLocaleDateString(locale, { day: 'numeric', month: 'short', year: 'numeric' })
}

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

export function UsersPage() {
  const { t, i18n } = useTranslation()
  const { user: currentUser, hasPermission } = useAuth()
  const { data: users, isLoading } = useUsersQuery()
  const createUser = useCreateUser()
  const updateUser = useUpdateUser()
  const setUserActive = useSetUserActive()
  const resetUserPassword = useResetUserPassword()

  const [editing, setEditing] = useState<Editing>({ mode: 'none' })
  const [pendingDeactivate, setPendingDeactivate] = useState<User | null>(null)
  const [resettingPasswordFor, setResettingPasswordFor] = useState<User | null>(null)
  const [newPassword, setNewPassword] = useState('')
  const [errorCode, setErrorCode] = useState<string | null>(null)

  const close = () => setEditing({ mode: 'none' })

  const handleCreate = (email: string, password: string, roleIds: string[]) => {
    setErrorCode(null)
    createUser.mutate(
      { email, password, roleIds },
      { onSuccess: close, onError: (error) => setErrorCode(codeOf(error)) },
    )
  }

  const handleUpdate = (target: User, email: string, roleIds: string[]) => {
    setErrorCode(null)
    updateUser.mutate(
      { id: target.id, email, roleIds },
      { onSuccess: close, onError: (error) => setErrorCode(codeOf(error)) },
    )
  }

  const handleActivate = (target: User) => {
    setErrorCode(null)
    setUserActive.mutate(
      { id: target.id, isActive: true },
      { onError: (error) => setErrorCode(codeOf(error)) },
    )
  }

  const confirmDeactivate = () => {
    if (pendingDeactivate === null) return
    setErrorCode(null)
    setUserActive.mutate(
      { id: pendingDeactivate.id, isActive: false },
      {
        onSettled: () => setPendingDeactivate(null),
        onError: (error) => setErrorCode(codeOf(error)),
      },
    )
  }

  const closeResetPassword = () => {
    setResettingPasswordFor(null)
    setNewPassword('')
  }

  const submitResetPassword = (event: FormEvent) => {
    event.preventDefault()
    if (resettingPasswordFor === null) return
    setErrorCode(null)
    resetUserPassword.mutate(
      { id: resettingPasswordFor.id, password: newPassword },
      { onSuccess: closeResetPassword, onError: (error) => setErrorCode(codeOf(error)) },
    )
  }

  const all = users ?? []
  const familyName = currentUser?.familyTreeName ?? ''

  return (
    <AppShell familyName={familyName} statLine={String(all.length)}>
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
            <h1 style={{ margin: 0, fontSize: 22, fontWeight: 700 }}>{t('users.title')}</h1>
            {hasPermission('User.Create') && editing.mode === 'none' && (
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
                {t('users.add')}
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
            <UserForm isSaving={createUser.isPending} onSubmit={handleCreate} onCancel={close} />
          )}

          {editing.mode === 'edit' && (
            <UserForm
              user={editing.user}
              isSaving={updateUser.isPending}
              onSubmit={(email, _password, roleIds) => handleUpdate(editing.user, email, roleIds)}
              onCancel={close}
            />
          )}

          {isLoading ? (
            <p style={{ color: 'var(--text-3)' }}>{t('users.loading')}</p>
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
              {t('users.empty')}
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
                    <th style={headCellStyle}>{t('users.email')}</th>
                    <th style={headCellStyle}>{t('users.roles')}</th>
                    <th style={headCellStyle}>{t('users.status')}</th>
                    <th style={headCellStyle}>{t('users.lastLogin')}</th>
                    <th style={headCellStyle} />
                  </tr>
                </thead>
                <tbody>
                  {all.map((current) => (
                    <tr key={current.id}>
                      <td style={{ ...cellStyle, fontWeight: 500 }}>{current.email}</td>
                      <td style={{ ...cellStyle, color: 'var(--text-3)' }}>
                        {current.roles.map((role) => role.name).join(', ')}
                      </td>
                      <td style={cellStyle}>
                        {current.isActive ? t('users.active') : t('users.inactive')}
                        {current.mustChangePassword && (
                          <span style={badgeStyle}>{t('users.pendingPasswordChange')}</span>
                        )}
                      </td>
                      <td style={{ ...cellStyle, color: 'var(--text-3)' }}>
                        {current.lastLoginAt === null
                          ? t('users.neverSignedIn')
                          : formatDate(current.lastLoginAt, i18n.language)}
                      </td>
                      <td style={{ ...cellStyle, whiteSpace: 'nowrap' }}>
                        <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
                          {hasPermission('User.Edit') && (
                            <button
                              type="button"
                              onClick={() => setEditing({ mode: 'edit', user: current })}
                              style={rowButtonStyle(false)}
                            >
                              {t('users.edit')}
                            </button>
                          )}
                          {hasPermission('User.Edit') && (
                            <button
                              type="button"
                              onClick={() => setResettingPasswordFor(current)}
                              style={rowButtonStyle(false)}
                            >
                              {t('users.resetPassword')}
                            </button>
                          )}
                          {hasPermission('User.Deactivate') &&
                            (current.isActive ? (
                              <button
                                type="button"
                                onClick={() => setPendingDeactivate(current)}
                                style={rowButtonStyle(true)}
                              >
                                {t('users.deactivate')}
                              </button>
                            ) : (
                              <button
                                type="button"
                                onClick={() => handleActivate(current)}
                                style={rowButtonStyle(false)}
                              >
                                {t('users.activate')}
                              </button>
                            ))}
                        </div>
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
      {pendingDeactivate !== null && (
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
            aria-label={t('users.confirmDeactivate', { email: pendingDeactivate.email })}
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
              {t('users.confirmDeactivate', { email: pendingDeactivate.email })}
            </div>
            <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
              <button
                type="button"
                onClick={() => setPendingDeactivate(null)}
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
                {t('users.cancel')}
              </button>
              <button
                type="button"
                onClick={confirmDeactivate}
                disabled={setUserActive.isPending}
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
                  cursor: setUserActive.isPending ? 'wait' : 'pointer',
                  opacity: setUserActive.isPending ? 0.7 : 1,
                }}
              >
                {t('users.deactivate')}
              </button>
            </div>
          </div>
        </div>
      )}

      {resettingPasswordFor !== null && (
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
          <form
            onSubmit={submitResetPassword}
            role="dialog"
            aria-modal="true"
            aria-label={t('users.resetPassword')}
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
            <div style={{ fontSize: 17, fontWeight: 600, marginBottom: 12 }}>
              {t('users.resetPassword')}
            </div>
            <label htmlFor="reset-password" style={{ display: 'block', marginBottom: 6, fontSize: 13 }}>
              {t('users.password')}
            </label>
            <input
              id="reset-password"
              type="password"
              value={newPassword}
              onChange={(event) => setNewPassword(event.target.value)}
              required
              style={{
                width: '100%',
                height: 'var(--control-h-md)',
                padding: '0 12px',
                border: '1px solid var(--border-strong)',
                borderRadius: 'var(--r-md)',
                background: 'var(--surface)',
                color: 'var(--text-1)',
                fontFamily: 'inherit',
                fontSize: 14,
                marginBottom: 'var(--space-5)',
              }}
            />
            <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
              <button
                type="button"
                onClick={closeResetPassword}
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
                {t('users.cancel')}
              </button>
              <button
                type="submit"
                disabled={resetUserPassword.isPending}
                style={{
                  height: 38,
                  padding: '0 16px',
                  border: 'none',
                  borderRadius: 'var(--r-md)',
                  background: 'var(--primary)',
                  color: '#fff',
                  fontFamily: 'inherit',
                  fontSize: 13,
                  fontWeight: 500,
                  cursor: resetUserPassword.isPending ? 'wait' : 'pointer',
                  opacity: resetUserPassword.isPending ? 0.7 : 1,
                }}
              >
                {resetUserPassword.isPending ? t('users.saving') : t('users.save')}
              </button>
            </div>
          </form>
        </div>
      )}
    </AppShell>
  )
}
