import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FilterBar } from './FilterBar'
import type { MemberFilters } from './filterParams'
import { useIsCompact } from './useIsCompact'

interface FilterControlsProps {
  filters: MemberFilters
  activeCount: number
  onChange: <K extends keyof MemberFilters>(key: K, value: MemberFilters[K] | undefined) => void
  onReset: () => void
}

/**
 * The filter controls, in whichever form the viewport can hold (design spec §6.2).
 *
 * Five dropdowns fit a desktop row and cannot fit 320px, so below the breakpoint they collapse
 * behind one Filters button. The button carries an active-count badge, because the failure mode
 * of a hidden filter panel is a user staring at a short list with no idea why.
 */
export function FilterControls({ filters, activeCount, onChange, onReset }: FilterControlsProps) {
  const { t } = useTranslation()
  const isCompact = useIsCompact()
  const [open, setOpen] = useState(false)

  const triggerRef = useRef<HTMLButtonElement>(null)
  const sheetRef = useRef<HTMLDivElement>(null)

  // Widening the window past the breakpoint puts the controls back on the page; leaving the
  // sheet flagged open would reopen it on the next narrowing, over a page that already shows
  // everything it holds.
  useEffect(() => {
    if (!isCompact) setOpen(false)
  }, [isCompact])

  // Mirrors MoveDialog and MemberModal: every overlay in this app closes on Escape rather than
  // requiring the mouse.
  useEffect(() => {
    if (!open) return

    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false)
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [open])

  // Whether the sheet has ever been opened. Without it the effect below fires on mount with
  // open=false and pulls focus (and the scroll position) onto the Filters button, so simply
  // loading either page on a narrow screen moved the user away from wherever they were.
  const hasOpened = useRef(false)

  // Focus follows the sheet in and back out again, so a keyboard user is not left on a control
  // that has just been covered over.
  useEffect(() => {
    if (open) {
      hasOpened.current = true
      sheetRef.current?.focus()
      return
    }
    if (hasOpened.current) triggerRef.current?.focus()
  }, [open])

  if (!isCompact) {
    return (
      <FilterBar
        filters={filters}
        activeCount={activeCount}
        onChange={onChange}
        onReset={onReset}
        layout="inline"
      />
    )
  }

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        onClick={() => setOpen(true)}
        aria-expanded={open}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: 8,
          height: 'var(--control-h-md)',
          padding: '0 16px',
          border: '1px solid var(--border-strong)',
          borderRadius: 'var(--r-md)',
          background: 'var(--surface)',
          color: 'var(--text-1)',
          fontFamily: 'inherit',
          fontSize: 13,
          fontWeight: 600,
          cursor: 'pointer',
        }}
      >
        {t('filters.open')}
        {activeCount > 0 && (
          <span
            // The count is spelled out for a screen reader; the pill shows the digit alone,
            // which is all a sighted user needs beside a button already labelled "Filters".
            aria-label={t('filters.activeCount', { count: activeCount })}
            style={{
              minWidth: 20,
              height: 20,
              padding: '0 6px',
              borderRadius: 'var(--r-pill)',
              background: 'var(--primary)',
              color: '#fff',
              fontSize: 12,
              fontWeight: 700,
              lineHeight: '20px',
              textAlign: 'center',
            }}
          >
            {activeCount}
          </span>
        )}
      </button>

      {open && (
        <div
          style={{
            position: 'fixed',
            inset: 0,
            background: 'rgba(21,24,27,.36)',
            display: 'flex',
            alignItems: 'flex-end',
            zIndex: 400,
          }}
        >
          <div
            ref={sheetRef}
            role="dialog"
            aria-modal="true"
            aria-label={t('filters.title')}
            tabIndex={-1}
            style={{
              width: '100%',
              maxHeight: '80vh',
              overflowY: 'auto',
              padding: 'var(--space-5)',
              background: 'var(--surface)',
              borderTopLeftRadius: 'var(--r-lg)',
              borderTopRightRadius: 'var(--r-lg)',
              boxShadow: 'var(--shadow-high)',
            }}
          >
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                marginBottom: 'var(--space-4)',
              }}
            >
              <h2 style={{ margin: 0, fontSize: 16, fontWeight: 700 }}>{t('filters.title')}</h2>
              <button
                type="button"
                onClick={() => setOpen(false)}
                aria-label={t('filters.close')}
                style={{
                  width: 28,
                  height: 28,
                  border: '1px solid var(--border)',
                  borderRadius: 'var(--r-md)',
                  background: 'var(--surface)',
                  color: 'var(--text-2)',
                  fontFamily: 'inherit',
                  fontSize: 16,
                  lineHeight: 1,
                  cursor: 'pointer',
                }}
              >
                ×
              </button>
            </div>

            {/* Changing a filter here does not close the sheet: the user is usually setting
                more than one, and closing after each would make the second a second trip. */}
            <FilterBar
              filters={filters}
              activeCount={activeCount}
              onChange={onChange}
              onReset={onReset}
              layout="stacked"
            />
          </div>
        </div>
      )}
    </>
  )
}
