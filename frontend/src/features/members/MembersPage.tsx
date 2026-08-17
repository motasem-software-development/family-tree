import { useState, type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import { useQueryClient } from '@tanstack/react-query'
import { AppShell } from '../../app/AppShell'
import { useAuth } from '../auth/AuthContext'
import { ApiError } from '../../services/apiClient'
import { MemberForm } from './MemberForm'
import {
  memberKeys,
  useCreateMember,
  useDeleteMember,
  useMembersQuery,
  useUpdateMember,
} from './useMembers'
import type { FamilyMember } from './types'

type Editing = { mode: 'none' } | { mode: 'add' } | { mode: 'edit'; member: FamilyMember }

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

export function MembersPage() {
  const { t } = useTranslation()
  const { user, hasPermission } = useAuth()
  const queryClient = useQueryClient()
  const { data: members, isLoading } = useMembersQuery()
  const createMember = useCreateMember()
  const updateMember = useUpdateMember()
  const deleteMember = useDeleteMember()

  const [editing, setEditing] = useState<Editing>({ mode: 'none' })
  const [pendingDelete, setPendingDelete] = useState<FamilyMember | null>(null)
  const [errorCode, setErrorCode] = useState<string | null>(null)

  const close = () => setEditing({ mode: 'none' })

  const handleCreate = (name: string, parentId: string | null) => {
    setErrorCode(null)
    createMember.mutate(
      { name, parentId },
      { onSuccess: close, onError: (error) => setErrorCode(codeOf(error)) },
    )
  }

  const handleUpdate = (target: FamilyMember, name: string) => {
    setErrorCode(null)
    updateMember.mutate(
      { id: target.id, name, version: target.version },
      {
        onSuccess: close,
        onError: (error) => {
          const code = codeOf(error)
          setErrorCode(code)
          // A CONCURRENCY_CONFLICT means the form is holding a stale version — retrying
          // against it just reproduces the same 409, so refetch and close so the next open
          // gets the current version. Any other error (e.g. a validation failure) means the
          // form is holding input the user still needs to fix — closing it would discard
          // their edit for no benefit, so leave it open.
          if (code === 'CONCURRENCY_CONFLICT') {
            void queryClient.invalidateQueries({ queryKey: memberKeys.all })
            close()
          }
        },
      },
    )
  }

  const confirmDelete = () => {
    if (pendingDelete === null) return
    setErrorCode(null)
    deleteMember.mutate(pendingDelete.id, {
      onSettled: () => setPendingDelete(null),
      onError: (error) => setErrorCode(codeOf(error)),
    })
  }

  const all = members ?? []
  const nameById = new Map(all.map((current) => [current.id, current.name]))
  const familyName = user?.familyTreeName ?? ''

  return (
    <AppShell familyName={familyName} statLine={t('tree.membersCount', { count: all.length })}>
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
            <h1 style={{ margin: 0, fontSize: 22, fontWeight: 700 }}>{t('members.title')}</h1>
            {hasPermission('Member.Create') && editing.mode === 'none' && (
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
                {t('members.add')}
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
            <MemberForm
              parents={all}
              isSaving={createMember.isPending}
              onSubmit={handleCreate}
              onCancel={close}
            />
          )}

          {editing.mode === 'edit' && (
            <MemberForm
              member={editing.member}
              parents={all.filter((candidate) => candidate.id !== editing.member.id)}
              isSaving={updateMember.isPending}
              onSubmit={(name) => handleUpdate(editing.member, name)}
              onCancel={close}
            />
          )}

          {isLoading ? (
            <p style={{ color: 'var(--text-3)' }}>{t('members.loading')}</p>
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
              {t('members.empty')}
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
                    <th style={headCellStyle}>{t('members.name')}</th>
                    <th style={headCellStyle}>{t('members.parent')}</th>
                    <th style={headCellStyle} />
                  </tr>
                </thead>
                <tbody>
                  {all.map((current) => (
                    <tr key={current.id}>
                      <td style={{ ...cellStyle, fontWeight: 500 }}>{current.name}</td>
                      <td style={{ ...cellStyle, color: 'var(--text-3)' }}>
                        {current.parentId === null
                          ? t('members.noParent')
                          : (nameById.get(current.parentId) ?? '—')}
                      </td>
                      <td style={{ ...cellStyle, whiteSpace: 'nowrap' }}>
                        <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
                          {hasPermission('Member.Edit') && (
                            <button
                              type="button"
                              onClick={() => setEditing({ mode: 'edit', member: current })}
                              style={rowButtonStyle(false)}
                            >
                              {t('members.edit')}
                            </button>
                          )}
                          {hasPermission('Member.Delete') && (
                            <button
                              type="button"
                              onClick={() => setPendingDelete(current)}
                              style={rowButtonStyle(true)}
                            >
                              {t('members.delete')}
                            </button>
                          )}
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
            aria-label={t('members.confirmDelete', { name: pendingDelete.name })}
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
              {t('members.confirmDelete', { name: pendingDelete.name })}
            </div>
            <div style={{ fontSize: 14, color: 'var(--text-2)', marginBottom: 'var(--space-6)' }}>
              {t('modal.deleteBody')}
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
                {t('modal.cancel')}
              </button>
              <button
                type="button"
                onClick={confirmDelete}
                disabled={deleteMember.isPending}
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
                  cursor: deleteMember.isPending ? 'wait' : 'pointer',
                  opacity: deleteMember.isPending ? 0.7 : 1,
                }}
              >
                {deleteMember.isPending ? t('members.deleting') : t('modal.confirmDelete')}
              </button>
            </div>
          </div>
        </div>
      )}
    </AppShell>
  )
}
