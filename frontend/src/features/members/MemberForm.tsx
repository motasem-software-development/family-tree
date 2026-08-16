import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { FamilyMember } from './types'

interface MemberFormProps {
  /** Present when editing; absent when adding. */
  member?: FamilyMember
  /** Candidate parents — excludes the member being edited. */
  parents: FamilyMember[]
  isSaving: boolean
  onSubmit: (name: string, parentId: string | null) => void
  onCancel: () => void
}

export function MemberForm({ member, parents, isSaving, onSubmit, onCancel }: MemberFormProps) {
  const { t } = useTranslation()
  const [name, setName] = useState(member?.name ?? '')
  const [parentId, setParentId] = useState(member?.parentId ?? '')

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault()
    onSubmit(name, parentId === '' ? null : parentId)
  }

  return (
    <form onSubmit={handleSubmit}>
      <label htmlFor="member-name">{t('members.name')}</label>
      <input
        id="member-name"
        value={name}
        onChange={(event) => setName(event.target.value)}
        maxLength={200}
        required
      />

      {/* Parent is fixed at creation: the server rejects a parent change on update, and
          re-parenting is the Phase 5 move command. */}
      {member === undefined && (
        <>
          <label htmlFor="member-parent">{t('members.parent')}</label>
          <select
            id="member-parent"
            value={parentId}
            onChange={(event) => setParentId(event.target.value)}
          >
            <option value="">{t('members.noParent')}</option>
            {parents.map((candidate) => (
              <option key={candidate.id} value={candidate.id}>
                {candidate.name}
              </option>
            ))}
          </select>
        </>
      )}

      <button type="submit" disabled={isSaving}>
        {isSaving ? t('members.saving') : t('members.save')}
      </button>
      <button type="button" onClick={onCancel}>
        {t('members.cancel')}
      </button>
    </form>
  )
}
