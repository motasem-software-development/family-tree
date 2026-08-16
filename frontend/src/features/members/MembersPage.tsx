import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../auth/AuthContext'
import { ApiError } from '../../services/apiClient'
import { MemberForm } from './MemberForm'
import { memberKeys, useCreateMember, useDeleteMember, useMembersQuery, useUpdateMember } from './useMembers'
import type { FamilyMember } from './types'

type Editing = { mode: 'none' } | { mode: 'add' } | { mode: 'edit'; member: FamilyMember }

const codeOf = (error: unknown): string => (error instanceof ApiError ? error.code : 'UNKNOWN')

export function MembersPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()
  const { data: members, isLoading } = useMembersQuery()
  const createMember = useCreateMember()
  const updateMember = useUpdateMember()
  const deleteMember = useDeleteMember()

  const [editing, setEditing] = useState<Editing>({ mode: 'none' })
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
          setErrorCode(codeOf(error))
          // A CONCURRENCY_CONFLICT means the form is holding a stale version — retrying
          // against it just reproduces the same 409. Refetch so the next open gets the
          // current version, and close the form so the user re-opens it against fresh data.
          void queryClient.invalidateQueries({ queryKey: memberKeys.all })
          close()
        },
      },
    )
  }

  const handleDelete = (target: FamilyMember) => {
    if (!window.confirm(t('members.confirmDelete', { name: target.name }))) return
    setErrorCode(null)
    deleteMember.mutate(target.id, { onError: (error) => setErrorCode(codeOf(error)) })
  }

  if (isLoading) return <p>{t('members.loading')}</p>

  const all = members ?? []

  return (
    <section>
      <h1>{t('members.title')}</h1>

      {/* Error text comes from the stable server code, never from the server's message —
          the UI is bilingual and message text is not part of the contract. */}
      {errorCode !== null && <p role="alert">{t(`errors.${errorCode}`, t('errors.UNKNOWN'))}</p>}

      {hasPermission('Member.Create') && editing.mode === 'none' && (
        <button type="button" onClick={() => setEditing({ mode: 'add' })}>
          {t('members.add')}
        </button>
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

      {all.length === 0 ? (
        <p>{t('members.empty')}</p>
      ) : (
        <ul>
          {all.map((current) => (
            <li key={current.id}>
              <span>{current.name}</span>
              {hasPermission('Member.Edit') && (
                <button type="button" onClick={() => setEditing({ mode: 'edit', member: current })}>
                  {t('members.edit')}
                </button>
              )}
              {hasPermission('Member.Delete') && (
                <button type="button" onClick={() => handleDelete(current)}>
                  {deleteMember.isPending ? t('members.deleting') : t('members.delete')}
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
