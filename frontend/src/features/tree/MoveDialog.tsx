import { useEffect, useRef, useState, type CSSProperties, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { FamilyTreeNode } from '../members/types'
import { MIN_QUERY_LENGTH, useSearch } from './useSearch'

export interface MoveDialogProps {
  member: FamilyTreeNode
  familyName: string
  /** The member and everyone beneath them: the targets the server would refuse. */
  blockedIds: ReadonlySet<string>
  errorCode: string | null
  isSaving: boolean
  onCancel: () => void
  /** null means "promote to first generation". */
  onConfirm: (parentId: string | null) => void
}

/**
 * The chosen target. `null` is a real choice — the family tree — so "nothing chosen yet" needs
 * its own value rather than being spelled null.
 */
type Choice = { kind: 'none' } | { kind: 'root' } | { kind: 'member'; id: string }

/* Styling copied from MemberActions.tsx's MemberModal so this dialog reads as the same visual
   language as the add/edit/delete modals rather than a bespoke one. */

const LABEL_STYLE: CSSProperties = {
  display: 'block',
  fontSize: 13,
  fontWeight: 500,
  marginBottom: 6,
  color: 'var(--text-1)',
}

const INPUT_STYLE: CSSProperties = {
  width: '100%',
  height: 40,
  border: '1px solid var(--border-strong)',
  borderRadius: 'var(--r-md)',
  padding: '0 12px',
  fontFamily: 'inherit',
  fontSize: 14,
  outline: 'none',
  background: 'var(--surface)',
  color: 'var(--text-1)',
}

const optionButtonStyle = (disabled: boolean, pressed: boolean): CSSProperties => ({
  display: 'block',
  width: '100%',
  textAlign: 'start',
  height: 40,
  border: `1px solid ${pressed ? 'var(--primary)' : 'var(--border)'}`,
  borderRadius: 'var(--r-md)',
  padding: '0 12px',
  fontFamily: 'inherit',
  fontSize: 14,
  background: pressed ? 'var(--primary-subtle)' : 'var(--surface)',
  color: disabled ? 'var(--text-disabled)' : 'var(--text-1)',
  cursor: disabled ? 'not-allowed' : 'pointer',
  marginTop: 8,
})

const CANCEL_BUTTON_STYLE: CSSProperties = {
  height: 38,
  padding: '0 16px',
  border: '1px solid var(--border-strong)',
  borderRadius: 'var(--r-md)',
  background: 'var(--surface)',
  fontFamily: 'inherit',
  fontSize: 13,
  fontWeight: 500,
  cursor: 'pointer',
}

const confirmButtonStyle = (isSaving: boolean): CSSProperties => ({
  height: 38,
  padding: '0 16px',
  border: 'none',
  borderRadius: 'var(--r-md)',
  background: 'var(--primary)',
  color: '#fff',
  fontFamily: 'inherit',
  fontSize: 13,
  fontWeight: 500,
  cursor: isSaving ? 'wait' : 'pointer',
  opacity: isSaving ? 0.7 : 1,
})

export const MoveDialog = ({
  member,
  familyName,
  blockedIds,
  errorCode,
  isSaving,
  onCancel,
  onConfirm,
}: MoveDialogProps) => {
  const { t } = useTranslation()
  const [query, setQuery] = useState('')
  const [choice, setChoice] = useState<Choice>({ kind: 'none' })
  const { page } = useSearch(query)
  const inputRef = useRef<HTMLInputElement>(null)

  // Mirrors MemberModal (MemberActions.tsx): the search field is this dialog's equivalent of the
  // name field, so it gets the same autofocus-on-mount treatment the sibling modals give theirs.
  useEffect(() => {
    inputRef.current?.focus()
  }, [])

  // Mirrors MemberModal and ContextMenu (MemberActions.tsx): every other overlay on this screen
  // closes on Escape, so Move should too rather than requiring the mouse to dismiss.
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onCancel()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onCancel])

  const reasonFor = (id: string): string | null => {
    if (id === member.id) return t('move.self')
    if (blockedIds.has(id)) return t('move.descendant')
    return null
  }

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (choice.kind === 'none') return
    onConfirm(choice.kind === 'root' ? null : choice.id)
  }

  const searched = query.trim().length >= MIN_QUERY_LENGTH

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
        aria-label={t('move.title')}
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
        <div style={{ padding: '22px 24px 0', maxHeight: '70vh', overflowY: 'auto' }}>
          <div style={{ fontSize: 17, fontWeight: 600, lineHeight: 1.35 }}>{t('move.title')}</div>
          <div style={{ fontSize: 14, lineHeight: 1.65, color: 'var(--text-2)', marginTop: 8 }}>
            {t('move.body', { name: member.name })}
          </div>

          <div style={{ marginTop: 18 }}>
            <label htmlFor="move-search" style={LABEL_STYLE}>
              {t('move.searchPlaceholder')}
            </label>
            <input
              id="move-search"
              ref={inputRef}
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder={t('move.searchPlaceholder')}
              style={INPUT_STYLE}
            />
          </div>

          <div style={{ marginTop: 4 }}>
            {/* Always offered, and always legal: promoting to first generation cannot close a
                loop. Listed first because it is the one target search can never return — the
                family tree is not a member. */}
            <button
              type="button"
              aria-pressed={choice.kind === 'root'}
              onClick={() => setChoice({ kind: 'root' })}
              style={optionButtonStyle(false, choice.kind === 'root')}
            >
              {t('move.rootOption', { family: familyName })}
            </button>

            {page.items.map((hit) => {
              const reason = reasonFor(hit.id)
              const pressed = choice.kind === 'member' && choice.id === hit.id
              return (
                <div key={hit.id}>
                  <button
                    type="button"
                    disabled={reason !== null}
                    aria-pressed={pressed}
                    aria-describedby={
                      hit.ancestors.length > 0 ? `move-hit-${hit.id}-path` : undefined
                    }
                    onClick={() => setChoice({ kind: 'member', id: hit.id })}
                    style={optionButtonStyle(reason !== null, pressed)}
                  >
                    {hit.name}
                  </button>
                  {/* The ancestor path is what tells the many repeated names apart — the reason
                      design spec §5.4 asked the search endpoint for it. It is a *description* of
                      the option, not part of its name — but nesting it inside the <button> makes
                      it part of the name regardless of aria-describedby (accname computation walks
                      the whole subtree; describedby only adds a description, it does not remove
                      descendant text from the name), so every option whose ancestor list contains
                      e.g. "سليمان" would still match a search for "سليمان" too, defeating the very
                      disambiguation the path exists to provide. Keeping the span a *sibling* of the
                      button — referenced by aria-describedby, the pattern MemberActions.tsx uses
                      for its name-field error — is what actually keeps it out of the name while
                      still exposing it: a screen reader announces the name, then (via the
                      description) the path, so a listener choosing between two same-named people
                      is not left with two indistinguishable options. */}
                  {hit.ancestors.length > 0 && (
                    <span
                      id={`move-hit-${hit.id}-path`}
                      style={{ display: 'block', color: 'var(--text-2)', fontSize: 12, marginTop: 2 }}
                    >
                      {hit.ancestors.map((ancestor) => ancestor.name).join(' ‹ ')}
                    </span>
                  )}
                  {reason !== null && (
                    <div style={{ fontSize: 12, color: 'var(--text-2)', marginTop: 2 }}>
                      {reason}
                    </div>
                  )}
                </div>
              )
            })}

            {searched && page.items.length === 0 && (
              <div style={{ fontSize: 13, color: 'var(--text-2)', marginTop: 8 }}>
                {t('move.noResults')}
              </div>
            )}
          </div>

          {/* Codes are the contract, the text is not. A raw code must never reach a reader: it
              would be English-only in an Arabic UI. */}
          {errorCode !== null && (
            <div role="alert" style={{ marginTop: 14, fontSize: 13, color: 'var(--error)' }}>
              {t(`errors.${errorCode}`, t('errors.UNKNOWN'))}
            </div>
          )}
        </div>

        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, padding: '22px 24px' }}>
          <button type="button" onClick={onCancel} style={CANCEL_BUTTON_STYLE}>
            {t('modal.cancel')}
          </button>
          <button
            type="submit"
            disabled={choice.kind === 'none' || isSaving}
            style={confirmButtonStyle(isSaving)}
          >
            {isSaving ? t('modal.saving') : t('move.confirm')}
          </button>
        </div>
      </form>
    </div>
  )
}
