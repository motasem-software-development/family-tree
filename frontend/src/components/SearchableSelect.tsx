import {
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type KeyboardEvent,
} from 'react'
import { createPortal } from 'react-dom'
import { matches } from './searchMatch'

export interface SelectOption {
  /** The value handed back to onChange. The empty string is reserved for "not recorded". */
  value: string
  /** What the row reads as. Also searched. */
  label: string
  /** Extra searchable text that the label does not show — an ISO code, a dialing code. */
  keywords?: readonly string[]
}

interface SearchableSelectProps {
  id: string
  ariaLabel: string
  value: string
  options: readonly SelectOption[]
  /** The row that clears the value. Omitted entirely when undefined. */
  emptyLabel?: string
  placeholder?: string
  disabled?: boolean
  /** Shown in place of the list when the query matches nothing. */
  noResultsLabel: string
  onChange: (value: string) => void
  controlStyle: CSSProperties
}

const LIST_MAX_HEIGHT = 264

/**
 * A select you can type into. The native <select> it replaces was fine for twenty-two
 * countries and unusable for two hundred and thirty-nine: the browser offers only a
 * first-letter jump, which in Arabic lands on الـ for a third of the list.
 *
 * The list renders through a portal rather than inline. It has to outlive its container's
 * bounds — the tree's member dialog scrolls its own body, and an absolutely positioned list
 * inside a scrolling box gets clipped at the fold.
 */
export function SearchableSelect({
  id,
  ariaLabel,
  value,
  options,
  emptyLabel,
  placeholder,
  disabled = false,
  noResultsLabel,
  onChange,
  controlStyle,
}: SearchableSelectProps) {
  const [open, setOpen] = useState(false)
  // null means "not searching" — the input shows the current selection instead of a query.
  const [query, setQuery] = useState<string | null>(null)
  const [active, setActive] = useState(0)
  const [rect, setRect] = useState<DOMRect | null>(null)

  const inputRef = useRef<HTMLInputElement>(null)
  const listRef = useRef<HTMLUListElement>(null)

  const rows = useMemo(
    () =>
      emptyLabel === undefined
        ? options
        : [{ value: '', label: emptyLabel } as SelectOption, ...options],
    [options, emptyLabel],
  )

  const selectedLabel = rows.find((row) => row.value === value)?.label ?? ''

  const filtered = useMemo(
    () =>
      query === null
        ? rows
        : rows.filter((row) => matches(query, [row.label, ...(row.keywords ?? [])])),
    [rows, query],
  )

  // The highlight is an index into a list that changes on every keystroke. Pinning it back to
  // the top keeps Enter honest: it always takes the row the user can see highlighted.
  useEffect(() => setActive(0), [query])

  useLayoutEffect(() => {
    if (!open) return

    const place = () => {
      const input = inputRef.current
      if (input !== null) setRect(input.getBoundingClientRect())
    }

    place()
    // Capture phase: the dialog scrolls its own body, and that scroll never bubbles to window.
    window.addEventListener('scroll', place, true)
    window.addEventListener('resize', place)
    return () => {
      window.removeEventListener('scroll', place, true)
      window.removeEventListener('resize', place)
    }
  }, [open])

  useEffect(() => {
    if (!open) return
    const row = listRef.current?.querySelector('[data-active="true"]')
    // Keeping the highlight in view is a nicety, not a behaviour: jsdom has no layout and so
    // no scrollIntoView, and a missing nicety must not take the component down with it.
    if (row instanceof HTMLElement && typeof row.scrollIntoView === 'function')
      row.scrollIntoView({ block: 'nearest' })
  }, [open, active])

  /**
   * Opening hands the user an empty box to type into. Leaving the selected label in place
   * would mean the first keystroke lands on the end of it — type "japan" with Palestine
   * selected and the query reads "فلسطينjapan", which matches nothing. The selection is not
   * lost, just demoted to the placeholder until the field closes again.
   */
  const start = () => {
    setOpen(true)
    setQuery((current) => current ?? '')
  }

  const close = () => {
    setOpen(false)
    setQuery(null)
  }

  const commit = (row: SelectOption) => {
    onChange(row.value)
    close()
  }

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault()
      if (!open) {
        start()
        return
      }
      const step = event.key === 'ArrowDown' ? 1 : -1
      setActive((current) =>
        filtered.length === 0 ? 0 : (current + step + filtered.length) % filtered.length,
      )
      return
    }

    if (event.key === 'Enter') {
      // Always swallowed while open: this lives inside a <form>, and letting Enter through
      // would submit the member instead of picking the country the user is looking at.
      if (!open) return
      event.preventDefault()
      const row = filtered[active]
      if (row !== undefined) commit(row)
      return
    }

    if (event.key === 'Escape') {
      if (!open) return
      event.preventDefault()
      close()
      return
    }

    if (event.key === 'Tab') close()
  }

  const listboxId = `${id}-listbox`

  return (
    <>
      <input
        id={id}
        ref={inputRef}
        role="combobox"
        aria-label={ariaLabel}
        aria-expanded={open}
        aria-controls={listboxId}
        aria-autocomplete="list"
        aria-activedescendant={
          open && filtered[active] !== undefined ? `${id}-option-${active}` : undefined
        }
        autoComplete="off"
        disabled={disabled}
        placeholder={query !== null && selectedLabel !== '' ? selectedLabel : placeholder}
        value={query ?? selectedLabel}
        onChange={(event) => {
          setQuery(event.target.value)
          setOpen(true)
        }}
        onFocus={start}
        onClick={start}
        onBlur={close}
        onKeyDown={onKeyDown}
        style={controlStyle}
      />

      {open &&
        rect !== null &&
        createPortal(
          <ul
            id={listboxId}
            ref={listRef}
            role="listbox"
            aria-label={ariaLabel}
            // The input's blur fires before a click resolves, which would close the list out
            // from under the pointer. Holding focus on mousedown lets the click land.
            onMouseDown={(event) => event.preventDefault()}
            style={{
              position: 'fixed',
              top: rect.bottom + 4,
              left: rect.left,
              width: rect.width,
              maxHeight: LIST_MAX_HEIGHT,
              overflowY: 'auto',
              margin: 0,
              padding: 4,
              listStyle: 'none',
              background: 'var(--surface)',
              border: '1px solid var(--border-strong)',
              borderRadius: 'var(--r-md)',
              boxShadow: 'var(--shadow-high)',
              zIndex: 900,
            }}
          >
            {filtered.length === 0 && (
              <li
                role="presentation"
                style={{ padding: '9px 11px', fontSize: 13, color: 'var(--text-3)' }}
              >
                {noResultsLabel}
              </li>
            )}

            {filtered.map((row, index) => (
              <li
                key={row.value === '' ? '__empty__' : row.value}
                id={`${id}-option-${index}`}
                role="option"
                aria-selected={row.value === value}
                data-active={index === active}
                onMouseEnter={() => setActive(index)}
                onClick={() => commit(row)}
                style={{
                  padding: '9px 11px',
                  fontSize: 14,
                  borderRadius: 'var(--r-sm)',
                  cursor: 'pointer',
                  background: index === active ? 'var(--primary-subtle)' : 'transparent',
                  fontWeight: row.value === value ? 600 : 400,
                  color: row.value === '' ? 'var(--text-3)' : 'var(--text-1)',
                }}
              >
                {row.label}
              </li>
            ))}
          </ul>,
          document.body,
        )}
    </>
  )
}
