import { useEffect, useRef, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { Direction } from '../../i18n/useDirection'
import type { TreeRow } from './flattenTree'
import { useVisibleRange } from './useVisibleRange'
import { ROW_HEIGHT } from './windowRange'

/** One indent level. The rail gradient repeats at exactly this pitch. */
const INDENT = 28

const FamilyIcon = ({ size = 16 }: { size?: number }) => (
  <svg
    width={size}
    height={size}
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.8"
    strokeLinecap="round"
    aria-hidden="true"
  >
    <rect x="9" y="3" width="6" height="4" rx="1" />
    <path d="M12 7v5M6 20v-4M18 20v-4M6 16h12" />
  </svg>
)

interface TreeCanvasProps {
  familyName: string
  rootCount: number
  rootOpen: boolean
  rows: readonly TreeRow[]
  selectedId: string | null
  direction: Direction
  zoom: number
  isLoading: boolean
  /** A search hit to scroll to once its branch is open. Cleared via onRevealed. */
  revealId: string | null
  onRevealed: () => void
  onToggleRoot: () => void
  onToggle: (id: string) => void
  onSelect: (id: string) => void
  onMenu: (id: string, anchor: DOMRect) => void
  onZoomIn: () => void
  onZoomOut: () => void
  onZoomReset: () => void
  onCollapseAll: () => void
  onAddFirst: () => void
}

const ZoomButton = ({
  label,
  last,
  onClick,
  children,
}: {
  label: string
  last?: boolean
  onClick: () => void
  children: ReactNode
}) => (
  <button
    type="button"
    onClick={onClick}
    title={label}
    aria-label={label}
    style={{
      width: 38,
      height: 38,
      border: 'none',
      borderBottom: last === true ? 'none' : '1px solid var(--divider)',
      background: 'var(--surface)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      cursor: 'pointer',
      color: 'var(--text-2)',
    }}
  >
    {children}
  </button>
)

export const TreeCanvas = ({
  familyName,
  rootCount,
  rootOpen,
  rows,
  selectedId,
  direction,
  zoom,
  isLoading,
  revealId,
  onRevealed,
  onToggleRoot,
  onToggle,
  onSelect,
  onMenu,
  onZoomIn,
  onZoomOut,
  onZoomReset,
  onCollapseAll,
  onAddFirst,
}: TreeCanvasProps) => {
  const { t } = useTranslation()
  const rtl = direction === 'rtl'

  const scrollRef = useRef<HTMLDivElement>(null)
  const listRef = useRef<HTMLDivElement>(null)
  const range = useVisibleRange(scrollRef, listRef, rows.length, zoom)

  useEffect(() => {
    if (revealId === null) return

    const index = rows.findIndex((row) => row.id === revealId)
    const scroller = scrollRef.current
    const list = listRef.current

    // The row is not in the outline — a still-collapsed ancestor, or a hit from a result set
    // that has since changed. Clear the request rather than leaving it pending forever.
    if (index === -1 || scroller === null || list === null) {
      onRevealed()
      return
    }

    // The row's offset from the list's top in layout pixels, converted to the scroll
    // container's visual pixels — the same coordinate mapping useVisibleRange performs, in
    // reverse. Centred rather than top-aligned: a family member is only meaningful next to
    // their siblings and parent.
    const listTop = list.getBoundingClientRect().top - scroller.getBoundingClientRect().top
    const rowTop = scroller.scrollTop + listTop + index * ROW_HEIGHT * zoom
    scroller.scrollTo({ top: Math.max(0, rowTop - scroller.clientHeight / 2), behavior: 'smooth' })

    onRevealed()
  }, [revealId, rows, zoom, onRevealed])

  return (
    <div
      ref={scrollRef}
      style={{
        flex: 1,
        minWidth: 0,
        position: 'relative',
        background: 'var(--tree-canvas)',
        overflow: 'auto',
      }}
    >
      {isLoading && (
        <div
          style={{
            position: 'absolute',
            inset: 0,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            background: 'var(--tree-canvas)',
            zIndex: 20,
          }}
        >
          <div style={{ textAlign: 'center' }}>
            <div
              style={{
                width: 26,
                height: 26,
                border: '2.5px solid var(--border)',
                borderTopColor: 'var(--primary)',
                borderRadius: '50%',
                margin: '0 auto 14px',
                animation: 'spin 800ms linear infinite',
              }}
            />
            <div style={{ fontSize: 13, color: 'var(--text-2)' }}>{t('tree.loading')}</div>
          </div>
        </div>
      )}

      <div
        style={{
          padding: '36px 40px 120px',
          minHeight: '100%',
          // CSS `zoom`, not `transform: scale()`. A transform is purely visual: it never grows
          // the scroll container's scrollHeight, so zooming past 1.0 pushed content outside the
          // scrollable area with no way to reach it. `zoom` participates in layout, so the
          // scrollbar tracks the scaled content.
          //
          // The cost is the transition the design handoff had on `transform` — `zoom` is not
          // reliably animatable across browsers, so zoom steps are now instant. An unreachable
          // bottom of the tree is the worse defect.
          zoom,
        }}
      >
        <button
          type="button"
          onClick={onToggleRoot}
          aria-expanded={rootOpen}
          style={{
            height: 44,
            minWidth: 200,
            background: 'var(--primary)',
            border: 'none',
            borderRadius: 'var(--r-md)',
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 16,
            padding: '0 16px',
            fontFamily: 'inherit',
            fontSize: 15,
            fontWeight: 600,
            color: '#fff',
            cursor: 'pointer',
            boxShadow: 'var(--shadow-low)',
          }}
        >
          <span style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <FamilyIcon />
            {familyName}
          </span>
          <span style={{ fontFamily: 'var(--mono)', fontSize: 11, opacity: 0.75 }}>{rootCount}</span>
        </button>

        <div style={{ height: 10 }} />

        <div role="tree" aria-label={t('tree.treeLabel')} ref={listRef}>
          {/* Spacers stand in for the rows outside the window, so the scrollbar reflects the
              whole outline rather than only what is rendered. */}
          <div aria-hidden="true" style={{ height: range.padStart }} />
          {rows.slice(range.startIndex, range.endIndex).map((row, offset) => (
            <TreeRowView
              key={row.id}
              row={row}
              rtl={rtl}
              selected={selectedId === row.id}
              // Windowing removes rows from the DOM; without these a screen reader would
              // announce the size of the window instead of the size of the family.
              setSize={rows.length}
              posInSet={range.startIndex + offset + 1}
              onToggle={onToggle}
              onSelect={onSelect}
              onMenu={onMenu}
            />
          ))}
          <div aria-hidden="true" style={{ height: range.padEnd }} />
        </div>

        {!isLoading && rows.length === 0 && rootOpen && (
          <div style={{ maxWidth: 380, margin: '60px auto', textAlign: 'center' }}>
            <div
              style={{
                width: 48,
                height: 48,
                borderRadius: 'var(--r-md)',
                background: 'var(--sunken)',
                margin: '0 auto 16px',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                color: 'var(--text-3)',
              }}
            >
              <FamilyIcon size={24} />
            </div>
            <div style={{ fontSize: 17, fontWeight: 600, marginBottom: 8 }}>
              {t('tree.emptyTitle')}
            </div>
            <div style={{ fontSize: 14, lineHeight: 1.65, color: 'var(--text-2)', marginBottom: 20 }}>
              {t('tree.emptyBody')}
            </div>
            <button
              type="button"
              onClick={onAddFirst}
              style={{
                height: 40,
                padding: '0 18px',
                border: 'none',
                borderRadius: 'var(--r-md)',
                background: 'var(--primary)',
                color: '#fff',
                fontSize: 14,
                fontWeight: 500,
                cursor: 'pointer',
              }}
            >
              {t('tree.emptyCta')}
            </button>
          </div>
        )}
      </div>

      <div
        style={{
          position: 'sticky',
          bottom: 24,
          width: 40,
          marginInlineStart: 24,
          display: 'inline-flex',
          flexDirection: 'column',
          background: 'var(--surface)',
          border: '1px solid var(--border)',
          borderRadius: 'var(--r-md)',
          boxShadow: 'var(--shadow-med)',
          overflow: 'hidden',
          zIndex: 15,
        }}
      >
        <ZoomButton label={t('tree.zoomIn')} onClick={onZoomIn}>
          <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round">
            <path d="M12 5v14M5 12h14" />
          </svg>
        </ZoomButton>
        <ZoomButton label={t('tree.zoomOut')} onClick={onZoomOut}>
          <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round">
            <path d="M5 12h14" />
          </svg>
        </ZoomButton>
        <ZoomButton label={t('tree.fit')} onClick={onZoomReset}>
          <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
            <path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5" />
          </svg>
        </ZoomButton>
        <ZoomButton label={t('tree.collapseAll')} last onClick={onCollapseAll}>
          <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
            <path d="M8 9l4-4 4 4M8 15l4 4 4-4" />
          </svg>
        </ZoomButton>
      </div>
    </div>
  )
}

interface TreeRowViewProps {
  row: TreeRow
  rtl: boolean
  selected: boolean
  setSize: number
  posInSet: number
  onToggle: (id: string) => void
  onSelect: (id: string) => void
  onMenu: (id: string, anchor: DOMRect) => void
}

const TreeRowView = ({
  row,
  rtl,
  selected,
  setSize,
  posInSet,
  onToggle,
  onSelect,
  onMenu,
}: TreeRowViewProps) => {
  const { t } = useTranslation()
  const hasChildren = row.childCount > 0 || row.hasMoreChildren

  // Node appearance is a small state machine: selected beats matched beats default.
  let background = 'var(--surface)'
  let border = '1px solid #DCE1E5'
  let color = 'var(--text-1)'
  let fontWeight = 500
  let boxShadow = 'none'
  if (row.matched) {
    background = 'var(--tree-highlight)'
    border = '1px solid #D9B36A'
    color = '#5E410A'
    fontWeight = 600
  }
  if (selected) {
    background = 'var(--primary-subtle)'
    border = '1.5px solid var(--primary)'
    color = 'var(--primary-active)'
    fontWeight = 600
    boxShadow = 'var(--shadow-med)'
  }

  const caret = row.isOpen ? '⌄' : rtl ? '‹' : '›'

  return (
    <div
      role="treeitem"
      aria-level={row.generation}
      aria-expanded={hasChildren ? row.isOpen : undefined}
      aria-selected={selected}
      aria-setsize={setSize}
      aria-posinset={posInSet}
      style={{ display: 'flex', alignItems: 'center', height: 44 }}
    >
      {/* Depth rail: one hairline per ancestor level, repeated at the indent pitch. */}
      <div
        aria-hidden="true"
        style={{
          flex: '0 0 auto',
          height: 44,
          width: row.depth * INDENT,
          backgroundRepeat: 'repeat-x',
          backgroundImage:
            row.depth > 0
              ? `repeating-linear-gradient(to ${rtl ? 'left' : 'right'}, var(--tree-connector) 0 1.5px, transparent 1.5px ${INDENT}px)`
              : 'none',
          backgroundPosition: rtl ? 'right 10px top 0' : 'left 10px top 0',
        }}
      />
      <div aria-hidden="true" style={{ flex: '0 0 22px', height: 44, position: 'relative' }}>
        <div
          style={{
            position: 'absolute',
            insetInlineStart: 10,
            top: 0,
            width: 1.5,
            background: 'var(--tree-connector)',
            // A last child's line stops at the elbow; a middle child's continues to the next row.
            height: row.isLast ? 22 : 44,
          }}
        />
        <div
          style={{
            position: 'absolute',
            insetInlineStart: 10,
            top: 21,
            height: 1.5,
            width: 12,
            background: 'var(--tree-connector)',
          }}
        />
      </div>

      <button
        type="button"
        onClick={() => hasChildren && onToggle(row.id)}
        title={row.isOpen ? t('tree.collapse') : t('tree.expand')}
        aria-label={row.isOpen ? t('tree.collapse') : t('tree.expand')}
        aria-hidden={!hasChildren}
        tabIndex={hasChildren ? 0 : -1}
        style={{
          width: 22,
          height: 22,
          flex: '0 0 22px',
          border: 'none',
          background: 'transparent',
          color: 'var(--text-3)',
          fontSize: 13,
          lineHeight: 1,
          cursor: hasChildren ? 'pointer' : 'default',
          borderRadius: 'var(--r-sm)',
          opacity: hasChildren ? 1 : 0,
        }}
      >
        {hasChildren ? caret : ''}
      </button>

      <button
        type="button"
        onClick={() => onSelect(row.id)}
        style={{
          height: 36,
          minWidth: 140,
          maxWidth: 240,
          background,
          border,
          borderRadius: 'var(--r-md)',
          boxShadow,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: 10,
          padding: '0 12px',
          fontFamily: 'inherit',
          fontSize: 14,
          fontWeight,
          color,
          cursor: 'pointer',
          opacity: row.dimmed ? 0.35 : 1,
          transition: 'all var(--motion-fast) var(--ease-standard)',
        }}
      >
        <span
          style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
          title={row.name}
        >
          {row.name}
        </span>
        {row.childCount > 0 && (
          <span
            style={{
              height: 19,
              minWidth: 19,
              padding: '0 6px',
              borderRadius: 'var(--r-pill)',
              background: row.isOpen ? 'var(--sunken)' : 'var(--primary-subtle)',
              color: row.isOpen ? 'var(--text-2)' : 'var(--primary-hover)',
              fontFamily: 'var(--mono)',
              fontSize: 10,
              display: 'inline-flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            {row.isOpen ? row.childCount : `+${row.childCount}`}
          </span>
        )}
      </button>

      <button
        type="button"
        onClick={(event) => onMenu(row.id, event.currentTarget.getBoundingClientRect())}
        title={t('tree.actions')}
        aria-label={`${t('tree.actions')} — ${row.name}`}
        style={{
          width: 26,
          height: 26,
          marginInlineStart: 6,
          border: 'none',
          background: 'transparent',
          color: 'var(--text-3)',
          fontSize: 15,
          lineHeight: 1,
          cursor: 'pointer',
          borderRadius: 'var(--r-sm)',
          opacity: selected ? 1 : 0.45,
        }}
      >
        ⋮
      </button>
    </div>
  )
}
