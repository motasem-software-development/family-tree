import { useState, type CSSProperties, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { LifeDetailsFields } from './LifeDetailsFields'
import { EMPTY_LIFE_DETAILS, lifeDetailsOf, type LifeDetails } from './lifeDetails'
import type { FamilyMember } from './types'

interface MemberFormProps {
  /** Present when editing; absent when adding. */
  member?: FamilyMember
  /** Candidate parents — excludes the member being edited. */
  parents: FamilyMember[]
  isSaving: boolean
  onSubmit: (name: string, parentId: string | null, life: LifeDetails) => void
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

export function MemberForm({ member, parents, isSaving, onSubmit, onCancel }: MemberFormProps) {
  const { t } = useTranslation()
  const [name, setName] = useState(member?.name ?? '')
  const [parentId, setParentId] = useState(member?.parentId ?? '')
  const [life, setLife] = useState<LifeDetails>(
    member === undefined ? EMPTY_LIFE_DETAILS : lifeDetailsOf(member),
  )

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault()
    onSubmit(name, parentId === '' ? null : parentId, life)
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
        <label htmlFor="member-name" style={labelStyle}>
          {t('members.name')}
        </label>
        <input
          id="member-name"
          value={name}
          onChange={(event) => setName(event.target.value)}
          maxLength={200}
          required
          style={controlStyle}
        />
      </div>

      {/* Parent is fixed at creation: the server rejects a parent change on update, and
          re-parenting is the Phase 5 move command. */}
      {member === undefined && (
        <div style={{ marginBottom: 'var(--space-4)' }}>
          <label htmlFor="member-parent" style={labelStyle}>
            {t('members.parent')}
          </label>
          <select
            id="member-parent"
            value={parentId}
            onChange={(event) => setParentId(event.target.value)}
            style={controlStyle}
          >
            <option value="">{t('members.noParent')}</option>
            {parents.map((candidate) => (
              <option key={candidate.id} value={candidate.id}>
                {candidate.name}
              </option>
            ))}
          </select>
        </div>
      )}

      <LifeDetailsFields
        idPrefix="member"
        value={life}
        onChange={setLife}
        labelStyle={labelStyle}
        controlStyle={controlStyle}
      />

      <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
        <button type="button" onClick={onCancel} style={buttonStyle(false, false)}>
          {t('members.cancel')}
        </button>
        <button type="submit" disabled={isSaving} style={buttonStyle(true, isSaving)}>
          {isSaving ? t('members.saving') : t('members.save')}
        </button>
      </div>
    </form>
  )
}
