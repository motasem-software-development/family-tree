import { render, screen } from '@testing-library/react'
import { I18nextProvider } from 'react-i18next'
import { afterEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n'
import { EMPTY_LIFE_DETAILS } from '../members/lifeDetails'
import type { TreeRow } from './flattenTree'
import { TreeCanvas } from './TreeCanvas'
import { ROW_HEIGHT } from './windowRange'

/** Enough rows that a 440px viewport only covers a slice of them. */
const ROW_COUNT = 100

const buildRows = (count: number): TreeRow[] =>
  Array.from({ length: count }, (_, index) => ({
    id: `m${index}`,
    name: `Member ${index}`,
    parentId: null,
    generation: 1,
    depth: 0,
    isLast: index === count - 1,
    childCount: 0,
    hasMoreChildren: false,
    isOpen: false,
    matched: false,
    dimmed: false,
    life: EMPTY_LIFE_DETAILS,
  }))

const noop = (): void => {}

const renderCanvas = (rows: TreeRow[]) =>
  render(
    <I18nextProvider i18n={i18n}>
      <TreeCanvas
        familyName="Family"
        rootCount={rows.length}
        rootOpen
        rows={rows}
        selectedId={null}
        direction="ltr"
        zoom={1}
        isLoading={false}
        revealId={null}
        onRevealed={noop}
        onToggleRoot={noop}
        onToggle={noop}
        onSelect={noop}
        onMenu={noop}
        onZoomIn={noop}
        onZoomOut={noop}
        onZoomReset={noop}
        onCollapseAll={noop}
        onAddFirst={noop}
      />
    </I18nextProvider>,
  )

/**
 * jsdom measures every element as 0×0, which is why `windowRange` fails toward "render
 * everything" — a real component test can never see a genuine window without faking geometry.
 * This stubs `getBoundingClientRect` for exactly the two elements `useVisibleRange` measures
 * (the scroll container, identified by its `overflow: auto` inline style, and the list, by its
 * `role="tree"`) so the outline is scrolled 3000px past a 440px viewport — a real, non-trivial
 * window with `startIndex > 0`.
 */
const stubScrolledGeometry = (rowCount: number): void => {
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (
    this: HTMLElement,
  ) {
    const base = { x: 0, left: 0, right: 0, bottom: 0, toJSON: () => ({}) }
    if (this.getAttribute('role') === 'tree') {
      return { ...base, top: -3000, y: -3000, width: 0, height: rowCount * ROW_HEIGHT } as DOMRect
    }
    if (this.style.overflow === 'auto') {
      return { ...base, top: 0, y: 0, width: 0, height: 440 } as DOMRect
    }
    return { ...base, top: 0, y: 0, width: 0, height: 0 } as DOMRect
  })
}

describe('TreeCanvas windowing', () => {
  afterEach(() => vi.restoreAllMocks())

  it('labels rendered rows with the full outline size and their true position', async () => {
    const rows = buildRows(ROW_COUNT)
    stubScrolledGeometry(ROW_COUNT)

    renderCanvas(rows)

    const rendered = await screen.findAllByRole('treeitem')

    // A genuine window: fewer rows rendered than exist, so the test is not vacuously true the
    // way it would be under jsdom's default zero-viewport ("render everything") fail-safe.
    expect(rendered.length).toBeGreaterThan(0)
    expect(rendered.length).toBeLessThan(rows.length)

    // Every rendered row must report the size of the WHOLE outline, not the window.
    rendered.forEach((row) => {
      expect(row).toHaveAttribute('aria-setsize', String(rows.length))
    })

    // windowRange(scrollTop=3000, viewportHeight=440, rowHeight=44, count=100, overscan=6):
    // firstVisible = floor(3000/44) = 68, startIndex = 68 - 6 = 62. The first rendered row's
    // position must reflect its real index in the outline, not its offset within the window.
    expect(rendered[0]).toHaveAttribute('aria-posinset', '63')
  })
})

describe('life status', () => {
  const row = (over: Partial<TreeRow> = {}): TreeRow => ({
    ...buildRows(1)[0],
    ...over,
  })

  it('labels a living member so the status does not rest on colour alone', async () => {
    renderCanvas([row({ name: 'سليمان' })])

    expect(await screen.findByLabelText(i18n.t('members.living'))).toBeInTheDocument()
  })

  it('labels a deceased member', async () => {
    renderCanvas([row({ name: 'سليمان', life: { ...EMPTY_LIFE_DETAILS, isDeceased: true } })])

    expect(await screen.findByLabelText(i18n.t('members.deceased'))).toBeInTheDocument()
  })

  it('shows the life years next to the name when a date is known', async () => {
    renderCanvas([
      row({
        name: 'سليمان',
        life: { dateOfBirth: '1920-03-14', dateOfDeath: '1995-11-02', isDeceased: true },
      }),
    ])

    expect(await screen.findByText('1920–1995')).toBeInTheDocument()
  })

  it('shows no year range when no date is known', async () => {
    // The imported tree carries names and nothing else; a bare dash would be noise on 350 rows.
    renderCanvas([row({ name: 'سليمان' })])

    await screen.findByText('سليمان')
    expect(screen.queryByText('–')).not.toBeInTheDocument()
  })
})
