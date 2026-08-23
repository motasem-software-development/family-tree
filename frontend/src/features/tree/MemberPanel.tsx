import type { CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import { useIsCompact } from '../../app/useIsCompact'
import { LifeStatusDot } from '../members/LifeStatusDot'
import { EMPTY_LIFE_DETAILS, type LifeDetails } from '../members/lifeDetails'
import type { FamilyTreeNode } from '../members/types'
import { rootRelative } from './generation'
import { descendantCount } from './flattenTree'

export interface MemberPermissions {
  canCreate: boolean
  canEdit: boolean
  canDelete: boolean
  canMove: boolean
}

interface MemberPanelProps {
  member: FamilyTreeNode
  parentName: string
  /**
   * Father, grandfather, great-grandfather — composed by the page from the parent chain, since
   * the panel only ever holds the one node. Empty for a first-generation member.
   */
  lineage: string
  /**
   * The absolute generation the current view is rooted at, so the panel can show the
   * root-relative number the generation filter uses (design spec §1.2). Passed in rather than
   * derived: the panel holds one node and has no view to read it from.
   */
  rootGeneration: number
  /**
   * ISO timestamps from the flat members list. The tree endpoint returns structure only, so
   * the page joins the two by id rather than widening the tree DTO for two display fields.
   */
  createdAt: string | undefined
  updatedAt: string | undefined
  /** From the same join. Absent while the flat list is still loading — treated as living. */
  life: LifeDetails | undefined
  permissions: MemberPermissions
  onClose: () => void
  onAdd: () => void
  onEdit: () => void
  onMove: () => void
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
const formatDate = (iso: string | null | undefined, locale: string): string => {
  if (iso === undefined || iso === null) return '—'
  const parsed = new Date(iso)
  if (Number.isNaN(parsed.getTime())) return iso
  return parsed.toLocaleDateString(locale, { day: 'numeric', month: 'short', year: 'numeric' })
}

export const MemberPanel = ({
  member,
  parentName,
  lineage,
  rootGeneration,
  createdAt,
  updatedAt,
  life = EMPTY_LIFE_DETAILS,
  permissions,
  onClose,
  onAdd,
  onEdit,
  onMove,
  onDelete,
}: MemberPanelProps) => {
  const { t, i18n } = useTranslation()
  // Beside the canvas there is room for a 320px column; below the breakpoint there is not, so
  // the same panel is re-hung as an overlay over the canvas rather than squeezing it to nothing.
  const isCompact = useIsCompact()

  // The accessible name has to be the whole name the heading shows: a bare given name
  // identifies nobody once two cousins share it.
  const composed = lineage === '' ? member.name : `${member.name} ${lineage}`

  const missing: string[] = []
  if (!permissions.canCreate) missing.push('Member.Create')
  if (!permissions.canEdit) missing.push('Member.Edit')
  if (!permissions.canMove) missing.push('Member.Move')
  if (!permissions.canDelete) missing.push('Member.Delete')

  const asideStyle: CSSProperties = isCompact
    ? {
        // Above the canvas and its sticky zoom toolbar, below --z-modal (400) so the move and
        // delete dialogs this panel opens still cover it.
        position: 'fixed',
        insetBlock: 0,
        insetInlineEnd: 0,
        width: 'min(360px, 100vw)',
        zIndex: 301,
        background: 'var(--surface)',
        borderInlineStart: '1px solid var(--border)',
        boxShadow: 'var(--shadow-high)',
        padding: 20,
        overflowY: 'auto',
        animation: 'fadeUp var(--motion-base) var(--ease-standard)',
      }
    : {
        width: 320,
        flex: '0 0 320px',
        background: 'var(--surface)',
        borderInlineStart: '1px solid var(--border)',
        padding: 20,
        overflowY: 'auto',
        animation: 'fadeUp var(--motion-base) var(--ease-standard)',
      }

  return (
    <>
      {/* Only when overlaid: a panel sitting on top of the canvas needs the tap-outside way
          out that a panel sitting beside it never did. */}
      {isCompact && (
        <div
          role="presentation"
          onClick={onClose}
          style={{ position: 'fixed', inset: 0, background: 'rgba(21,24,27,.36)', zIndex: 300 }}
        />
      )}
      <aside aria-label={composed} style={asideStyle}>
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
              {/* Muted, as on a members row: the lineage is context, not four names of equal
                  weight, and the given name still has to be what the eye lands on. */}
              {lineage !== '' && (
                <span style={{ fontWeight: 400, color: 'var(--text-3)' }}> {lineage}</span>
              )}
            </div>
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 7,
                fontSize: 12,
                color: 'var(--text-3)',
                marginTop: 6,
              }}
            >
              <LifeStatusDot deceased={life.isDeceased} />
              {/* Spelled out, not left to the dot's colour: the status has to survive a
                  greyscale print and a screen reader alike. */}
              <span>{t(life.isDeceased ? 'members.deceased' : 'members.living')}</span>
              <span aria-hidden="true">·</span>
              <span>
                {/* Root-relative, matching the generation filter — a page must not contradict
                    its own filter (design spec §1.2). member.generation stays absolute because
                    the reports page and the PDF caption read that same field. */}
                {t('tree.gen')} {rootRelative(member.generation, rootGeneration)}
              </span>
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
          <Row label={t('panel.dateOfBirth')} value={formatDate(life.dateOfBirth, i18n.language)} />
          <Row label={t('panel.dateOfDeath')} value={formatDate(life.dateOfDeath, i18n.language)} />
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
          <button
            type="button"
            onClick={onMove}
            disabled={!permissions.canMove}
            style={actionStyle(permissions.canMove)}
          >
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

        {/* Only rendered when something is actually missing: Move is a real action now, so there
            is no permanent "not available yet" filler to fall back to when nothing is missing. */}
        {missing.length > 0 && (
          <div style={{ fontSize: 12, lineHeight: 1.6, color: 'var(--text-3)', marginTop: 12 }}>
            {t('tree.needPerm')} {missing.join(' · ')}
          </div>
        )}
      </aside>
    </>
  )
}
