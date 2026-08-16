import { useEffect, useRef, type CSSProperties, type FormEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { MemberPermissions } from './MemberPanel'

export type ModalKind = 'add' | 'edit' | 'delete' | 'blocked'

/* ------------------------------------------------------------------ context menu */

export interface MenuAnchor {
  id: string
  top: number
  inlineStart: number
}

interface ContextMenuProps {
  anchor: MenuAnchor
  permissions: MemberPermissions
  onClose: () => void
  onViewDetails: () => void
  onAdd: () => void
  onEdit: () => void
  onDelete: () => void
}

const menuItemStyle = (enabled: boolean): CSSProperties => ({
  display: 'block',
  width: '100%',
  textAlign: 'start',
  padding: '9px 13px',
  border: 'none',
  background: 'transparent',
  fontFamily: 'inherit',
  fontSize: 13,
  cursor: enabled ? 'pointer' : 'not-allowed',
  color: enabled ? 'var(--text-1)' : 'var(--text-disabled)',
})

export const ContextMenu = ({
  anchor,
  permissions,
  onClose,
  onViewDetails,
  onAdd,
  onEdit,
  onDelete,
}: ContextMenuProps) => {
  const { t } = useTranslation()

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div onClick={onClose} role="presentation" style={{ position: 'fixed', inset: 0, zIndex: 500 }}>
      <div
        role="menu"
        onClick={(event) => event.stopPropagation()}
        style={{
          position: 'absolute',
          top: anchor.top,
          insetInlineStart: anchor.inlineStart,
          width: 190,
          background: 'var(--surface)',
          border: '1px solid var(--border)',
          borderRadius: 'var(--r-md)',
          boxShadow: 'var(--shadow-high)',
          overflow: 'hidden',
          animation: 'fadeUp var(--motion-fast) var(--ease-standard)',
        }}
      >
        <button type="button" role="menuitem" onClick={onViewDetails} style={menuItemStyle(true)}>
          {t('tree.viewDetails')}
        </button>
        <button
          type="button"
          role="menuitem"
          onClick={onAdd}
          disabled={!permissions.canCreate}
          style={menuItemStyle(permissions.canCreate)}
        >
          {t('tree.addChild')}
        </button>
        <button
          type="button"
          role="menuitem"
          onClick={onEdit}
          disabled={!permissions.canEdit}
          style={menuItemStyle(permissions.canEdit)}
        >
          {t('tree.edit')}
        </button>
        <button
          type="button"
          role="menuitem"
          disabled
          title={t('tree.movePhase')}
          style={menuItemStyle(false)}
        >
          {t('tree.move')}
        </button>
        <div style={{ height: 1, background: 'var(--divider)' }} />
        <button
          type="button"
          role="menuitem"
          onClick={onDelete}
          disabled={!permissions.canDelete}
          style={menuItemStyle(permissions.canDelete)}
        >
          {t('tree.delete')}
        </button>
      </div>
    </div>
  )
}

/* ------------------------------------------------------------------------ modal */

interface MemberModalProps {
  kind: ModalKind
  /** The member being edited or deleted; when adding, the prospective parent. */
  subjectName: string
  parentName: string
  childNames: readonly string[]
  nameValue: string
  errorCode: string | null
  isSaving: boolean
  onNameChange: (value: string) => void
  onCancel: () => void
  onConfirm: () => void
}

const ICONS: Record<ModalKind, ReactNode> = {
  add: <path d="M12 5v14M5 12h14" />,
  edit: <path d="M4 20h4l10-10-4-4L4 16v4zM14 6l4 4" />,
  delete: <path d="M4 7h16M9 7V5h6v2M6 7l1 13h10l1-13" />,
  blocked: (
    <g>
      <rect x="5" y="11" width="14" height="9" rx="2" />
      <path d="M8 11V8a4 4 0 0 1 8 0v3" />
    </g>
  ),
}

const ICON_TONE: Record<ModalKind, { bg: string; stroke: string }> = {
  add: { bg: 'var(--primary-subtle)', stroke: '#0B4A4E' },
  edit: { bg: 'var(--primary-subtle)', stroke: '#0B4A4E' },
  delete: { bg: 'var(--error-subtle)', stroke: '#AF3A2E' },
  blocked: { bg: 'var(--warning-subtle)', stroke: '#8A5D06' },
}

export const MemberModal = ({
  kind,
  subjectName,
  parentName,
  childNames,
  nameValue,
  errorCode,
  isSaving,
  onNameChange,
  onCancel,
  onConfirm,
}: MemberModalProps) => {
  const { t } = useTranslation()
  const inputRef = useRef<HTMLInputElement>(null)
  const showNameField = kind === 'add' || kind === 'edit'

  useEffect(() => {
    if (showNameField) inputRef.current?.focus()
  }, [showNameField])

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onCancel()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onCancel])

  const title =
    kind === 'add'
      ? t('modal.addTitle')
      : kind === 'edit'
        ? t('modal.editTitle')
        : kind === 'delete'
          ? `${t('modal.deleteTitle')} — ${subjectName}`
          : t('modal.blockedTitle')

  const body =
    kind === 'add'
      ? t('modal.addBody')
      : kind === 'edit'
        ? t('modal.editBody')
        : kind === 'delete'
          ? t('modal.deleteBody')
          : t('modal.blockedBody', { name: subjectName, count: childNames.length })

  const confirmLabel =
    kind === 'add' ? t('modal.add') : kind === 'edit' ? t('modal.save') : t('modal.confirmDelete')

  const tone = ICON_TONE[kind]

  const submit = (event: FormEvent) => {
    event.preventDefault()
    onConfirm()
  }

  return (
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
        onSubmit={submit}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        style={{
          width: '100%',
          maxWidth: 440,
          background: 'var(--surface)',
          borderRadius: 'var(--r-lg)',
          boxShadow: 'var(--shadow-high)',
          overflow: 'hidden',
          animation: 'fadeUp var(--motion-base) var(--ease-standard)',
        }}
      >
        <div style={{ padding: '22px 24px 0' }}>
          <div style={{ display: 'flex', gap: 13, alignItems: 'flex-start' }}>
            <div
              style={{
                width: 36,
                height: 36,
                flex: '0 0 36px',
                borderRadius: 'var(--r-md)',
                background: tone.bg,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}
            >
              <svg
                width="19"
                height="19"
                viewBox="0 0 24 24"
                fill="none"
                stroke={tone.stroke}
                strokeWidth="1.8"
                strokeLinecap="round"
                strokeLinejoin="round"
                aria-hidden="true"
              >
                {ICONS[kind]}
              </svg>
            </div>
            <div style={{ minWidth: 0, flex: 1 }}>
              <div style={{ fontSize: 17, fontWeight: 600, lineHeight: 1.35 }}>{title}</div>
              <div style={{ fontSize: 14, lineHeight: 1.65, color: 'var(--text-2)', marginTop: 8 }}>
                {body}
              </div>
            </div>
          </div>

          {showNameField && (
            <div style={{ marginTop: 18 }}>
              <label
                htmlFor="member-name"
                style={{ display: 'block', fontSize: 13, fontWeight: 500, marginBottom: 6 }}
              >
                {t('modal.nameLabel')}
              </label>
              <input
                id="member-name"
                ref={inputRef}
                value={nameValue}
                onChange={(event) => onNameChange(event.target.value)}
                placeholder={t('modal.namePlaceholder')}
                maxLength={200}
                aria-invalid={errorCode !== null}
                aria-describedby="member-name-error"
                style={{
                  width: '100%',
                  height: 40,
                  border: `1px solid ${errorCode !== null ? 'var(--error)' : 'var(--border-strong)'}`,
                  borderRadius: 'var(--r-md)',
                  padding: '0 12px',
                  fontFamily: 'inherit',
                  fontSize: 14,
                  outline: 'none',
                  background: 'var(--surface)',
                  color: 'var(--text-1)',
                }}
              />
              <div
                id="member-name-error"
                role={errorCode !== null ? 'alert' : undefined}
                style={{ fontSize: 12, color: 'var(--error)', marginTop: 6, minHeight: 16 }}
              >
                {errorCode !== null ? t(`errors.${errorCode}`, t('errors.UNKNOWN')) : ''}
              </div>
              <div style={{ marginTop: 14 }}>
                <div style={{ fontSize: 13, fontWeight: 500, marginBottom: 6 }}>
                  {t('modal.parentLabel')}
                </div>
                <div
                  style={{
                    height: 40,
                    border: '1px solid var(--border)',
                    borderRadius: 'var(--r-md)',
                    background: 'var(--sunken)',
                    display: 'flex',
                    alignItems: 'center',
                    padding: '0 12px',
                    fontSize: 14,
                    color: 'var(--text-2)',
                  }}
                >
                  {parentName}
                </div>
              </div>
            </div>
          )}

          {kind === 'blocked' && childNames.length > 0 && (
            <div
              style={{
                marginTop: 16,
                border: '1px solid var(--border)',
                borderRadius: 'var(--r-md)',
                padding: '11px 13px',
                background: 'var(--sunken)',
                fontSize: 13,
                lineHeight: 1.9,
                color: 'var(--text-2)',
              }}
            >
              {childNames.join(' · ')}
            </div>
          )}

          {!showNameField && errorCode !== null && (
            <div role="alert" style={{ marginTop: 14, fontSize: 13, color: 'var(--error)' }}>
              {t(`errors.${errorCode}`, t('errors.UNKNOWN'))}
            </div>
          )}
        </div>

        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, padding: '22px 24px' }}>
          <button
            type="button"
            onClick={onCancel}
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
            {kind === 'blocked' ? t('modal.close') : t('modal.cancel')}
          </button>
          {/* A blocked delete has nothing to confirm — Move, its designed way out, is Phase 5. */}
          {kind !== 'blocked' && (
            <button
              type="submit"
              disabled={isSaving}
              style={{
                height: 38,
                padding: '0 16px',
                border: 'none',
                borderRadius: 'var(--r-md)',
                background: kind === 'delete' ? 'var(--error)' : 'var(--primary)',
                color: '#fff',
                fontFamily: 'inherit',
                fontSize: 13,
                fontWeight: 500,
                cursor: isSaving ? 'wait' : 'pointer',
                opacity: isSaving ? 0.7 : 1,
              }}
            >
              {isSaving ? t('modal.saving') : confirmLabel}
            </button>
          )}
        </div>
      </form>
    </div>
  )
}

/* ------------------------------------------------------------------------ toast */

export const Toast = ({ message }: { message: string }) => (
  <div
    role="status"
    aria-live="polite"
    style={{
      position: 'fixed',
      bottom: 24,
      insetInlineEnd: 24,
      zIndex: 600,
      background: 'var(--surface)',
      border: '1px solid var(--border)',
      borderRadius: 'var(--r-md)',
      boxShadow: 'var(--shadow-high)',
      padding: '13px 15px',
      display: 'flex',
      gap: 11,
      alignItems: 'flex-start',
      maxWidth: 340,
      animation: 'fadeUp var(--motion-base) var(--ease-standard)',
    }}
  >
    <svg
      width="18"
      height="18"
      viewBox="0 0 24 24"
      fill="none"
      stroke="#1C7A4D"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      style={{ flex: '0 0 18px', marginTop: 1 }}
      aria-hidden="true"
    >
      <circle cx="12" cy="12" r="8" />
      <path d="M8.5 12.2l2.4 2.4 4.6-4.9" />
    </svg>
    <div style={{ fontSize: 13, lineHeight: 1.6, color: 'var(--text-1)' }}>{message}</div>
  </div>
)
