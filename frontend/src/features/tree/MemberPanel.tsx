import type { CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import type { FamilyTreeNode } from '../members/types'
import { descendantCount } from './flattenTree'

export interface MemberPermissions {
  canCreate: boolean
  canEdit: boolean
  canDelete: boolean
}

interface MemberPanelProps {
  member: FamilyTreeNode
  parentName: string
  /**
   * ISO timestamps from the flat members list. The tree endpoint returns structure only, so
   * the page joins the two by id rather than widening the tree DTO for two display fields.
   */
  createdAt: string | undefined
  updatedAt: string | undefined
  permissions: MemberPermissions
  onClose: () => void
  onAdd: () => void
  onEdit: () => void
  onDelete: () => void
}

const Row = ({ label, value, last }: { label: string; value: string; last?: boolean }) => (
  <div
    style={{
      display: 'flex',
      justifyContent: 'space-between',
      gap: 12,
      padding: '11px 13px',
      borderBottom: last === true ? 'none' : '1px solid var(--divider)',
    }}
  >
    <span style={{ fontSize: 13, color: 'var(--text-3)' }}>{label}</span>
    <span style={{ fontSize: 13, fontWeight: 500, textAlign: 'end' }}>{value}</span>
  </div>
)

const actionStyle = (enabled: boolean, danger = false): CSSProperties => ({
  height: 36,
  padding: '0 14px',
  borderRadius: 'var(--r-md)',
  fontFamily: 'inherit',
  fontSize: 13,
  fontWeight: 500,
  cursor: enabled ? 'pointer' : 'not-allowed',
  border: `1px solid ${enabled ? (danger ? '#E0B3AC' : 'var(--border-strong)') : 'var(--border)'}`,
  background: 'var(--surface)',
  color: enabled ? (danger ? 'var(--error)' : 'var(--text-1)') : 'var(--text-disabled)',
})

/**
 * Dates arrive as ISO strings. Rendered with the active locale so Arabic gets Arabic-Indic
 * numerals, per the design system's numeral rule.
 */
const formatDate = (iso: string | undefined, locale: string): string => {
  if (iso === undefined) return '—'
  const parsed = new Date(iso)
  if (Number.isNaN(parsed.getTime())) return iso
  return parsed.toLocaleDateString(locale, { day: 'numeric', month: 'short', year: 'numeric' })
}

export const MemberPanel = ({
  member,
  parentName,
  createdAt,
  updatedAt,
  permissions,
  onClose,
  onAdd,
  onEdit,
  onDelete,
}: MemberPanelProps) => {
  const { t, i18n } = useTranslation()

  const missing: string[] = []
  if (!permissions.canCreate) missing.push('Member.Create')
  if (!permissions.canEdit) missing.push('Member.Edit')
  if (!permissions.canDelete) missing.push('Member.Delete')

  return (
    <aside
      aria-label={member.name}
      style={{
        width: 320,
        flex: '0 0 320px',
        background: 'var(--surface)',
        borderInlineStart: '1px solid var(--border)',
        padding: 20,
        overflowY: 'auto',
        animation: 'fadeUp var(--motion-base) var(--ease-standard)',
      }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'flex-start',
          justifyContent: 'space-between',
          gap: 12,
          marginBottom: 18,
        }}
      >
        <div style={{ minWidth: 0 }}>
          <div style={{ fontSize: 19, fontWeight: 600, lineHeight: 1.35, wordBreak: 'break-word' }}>
            {member.name}
          </div>
          <div style={{ fontSize: 12, color: 'var(--text-3)', marginTop: 4 }}>
            {t('tree.gen')} {member.generation}
          </div>
        </div>
        <button
          type="button"
          onClick={onClose}
          aria-label={t('panel.close')}
          style={{
            width: 28,
            height: 28,
            flex: '0 0 28px',
            border: 'none',
            background: 'transparent',
            color: 'var(--text-3)',
            fontSize: 15,
            cursor: 'pointer',
            borderRadius: 'var(--r-sm)',
          }}
        >
          ✕
        </button>
      </div>

      <div
        style={{
          border: '1px solid var(--border)',
          borderRadius: 'var(--r-md)',
          overflow: 'hidden',
          marginBottom: 20,
        }}
      >
        <Row label={t('panel.parent')} value={parentName} />
        <Row label={t('panel.children')} value={String(member.children.length)} />
        <Row label={t('panel.descendants')} value={String(descendantCount(member))} />
        <Row label={t('panel.created')} value={formatDate(createdAt, i18n.language)} />
        <Row label={t('panel.updated')} value={formatDate(updatedAt, i18n.language)} last />
      </div>

      <div
        style={{
          fontSize: 11,
          fontWeight: 600,
          textTransform: 'uppercase',
          letterSpacing: '.07em',
          color: 'var(--text-3)',
          marginBottom: 10,
        }}
      >
        {t('tree.actions')}
      </div>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
        <button
          type="button"
          onClick={onAdd}
          disabled={!permissions.canCreate}
          style={actionStyle(permissions.canCreate)}
        >
          {t('tree.addChild')}
        </button>
        <button
          type="button"
          onClick={onEdit}
          disabled={!permissions.canEdit}
          style={actionStyle(permissions.canEdit)}
        >
          {t('tree.edit')}
        </button>
        {/* Move needs the Phase 5 command with cycle detection; disabled rather than dead. */}
        <button type="button" disabled title={t('tree.movePhase')} style={actionStyle(false)}>
          {t('tree.move')}
        </button>
        <button
          type="button"
          onClick={onDelete}
          disabled={!permissions.canDelete}
          style={actionStyle(permissions.canDelete, true)}
        >
          {t('tree.delete')}
        </button>
      </div>

      <div style={{ fontSize: 12, lineHeight: 1.6, color: 'var(--text-3)', marginTop: 12 }}>
        {missing.length > 0 ? `${t('tree.needPerm')} ${missing.join(' · ')}` : t('tree.movePhase')}
      </div>
    </aside>
  )
}
