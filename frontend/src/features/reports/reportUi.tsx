import type { CSSProperties, ReactNode } from 'react'

/**
 * The pieces the five report sections are built from.
 *
 * Two ideas hold this screen together, and both are visual rather than incidental:
 *
 * 1. **Measures above, worklists below.** Structure and life status are settled facts; the
 *    other three sections are things asking to be acted on. They looked identical before —
 *    five headings in one column — so nothing on the page said which was which.
 * 2. **The ladder is the shape of the family.** Generation counts, age brackets and branch
 *    sizes are all one-series magnitude data, so they all get the same mark: a rung hanging
 *    off a spine, longest generation full width. It is the one figure on this screen that
 *    could not belong to any other product.
 *
 * Conventions kept from the chart specs the rest of the app follows: one hue per single-series
 * chart (never a value-ramp across ordered bars — length already carries the magnitude), marks
 * capped at 10px with a rounded data end, hairline recessive chrome, a 2px surface gap rather
 * than a stroke wherever two fills touch, and values in text tokens, never in the mark's colour.
 */

/** A page-level grouping label. Used exactly twice — once per zone — so it stays meaningful. */
export const ZoneLabel = ({ children }: { children: ReactNode }) => (
  <h2
    style={{
      margin: 0,
      fontSize: 11,
      fontWeight: 700,
      letterSpacing: '0.09em',
      textTransform: 'uppercase',
      color: 'var(--text-3)',
    }}
  >
    {children}
  </h2>
)

interface CardProps {
  children: ReactNode
  /** Applied to the <section>, so a section keeps its own aria-labelledby wiring. */
  labelledBy?: string
  style?: CSSProperties
}

export const Card = ({ children, labelledBy, style }: CardProps) => (
  <section
    aria-labelledby={labelledBy}
    style={{
      background: 'var(--surface)',
      border: '1px solid var(--border)',
      borderRadius: 'var(--r-lg)',
      padding: 'var(--space-5)',
      // Border only, no shadow — the members, users and roles cards are built this way.
      minWidth: 0,
      ...style,
    }}
  >
    {children}
  </section>
)

interface CardHeadProps {
  id?: string
  title: ReactNode
  /** Sits at the far end of the title row: a count, a badge, a date. */
  trailing?: ReactNode
  /** One quiet line under the title. */
  caption?: ReactNode
}

export const CardHead = ({ id, title, trailing, caption }: CardHeadProps) => (
  <header style={{ marginBottom: 'var(--space-4)' }}>
    <div style={{ display: 'flex', alignItems: 'baseline', gap: 'var(--space-3)' }}>
      <h3 id={id} style={{ margin: 0, fontSize: 15, fontWeight: 600, lineHeight: 1.4 }}>
        {title}
      </h3>
      {trailing !== undefined && (
        <div style={{ marginInlineStart: 'auto', flex: '0 0 auto' }}>{trailing}</div>
      )}
    </div>
    {caption !== undefined && (
      <p style={{ margin: '2px 0 0', fontSize: 12, color: 'var(--text-3)' }}>{caption}</p>
    )}
  </header>
)

/**
 * A sub-heading inside a card, for a list the card's own title does not already name. Set in
 * plain semibold, not a second uppercase eyebrow: ZoneLabel already owns that device, and a
 * page carrying two levels of it has a texture rather than a hierarchy — the letterspacing
 * also does nothing in Arabic, so the Latin build would have read as the more structured one.
 */
export const GroupHead = ({ title, trailing }: { title: ReactNode; trailing?: ReactNode }) => (
  <h4
    style={{
      margin: '0 0 var(--space-2)',
      display: 'flex',
      alignItems: 'baseline',
      gap: 'var(--space-2)',
      fontSize: 12,
      fontWeight: 600,
      color: 'var(--text-3)',
    }}
  >
    {title}
    {/* Beside the title, not pushed to the far edge: a count parked 700px from the thing it
        counts stops being read as part of the heading. */}
    {trailing}
  </h4>
)

/** Tabular figures, so a column of counts lines up digit over digit in both scripts. */
const figureStyle: CSSProperties = { fontVariantNumeric: 'tabular-nums' }

/**
 * A labelled figure. Deliberately not a bordered tile: these annotate the chart they sit under
 * rather than competing with it, and a row of boxed KPI cards is the reflex this page avoids.
 */
export const Figure = ({
  label,
  value,
  testId,
  tone = 'default',
}: {
  label: string
  value: string
  testId?: string
  tone?: 'default' | 'living' | 'deceased'
}) => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 1, minWidth: 0 }}>
    <span
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 6,
        fontSize: 11,
        color: 'var(--text-3)',
        whiteSpace: 'nowrap',
      }}
    >
      {tone !== 'default' && (
        <span
          aria-hidden="true"
          style={{
            width: 7,
            height: 7,
            borderRadius: '50%',
            flex: '0 0 7px',
            background: tone === 'living' ? 'var(--success)' : 'var(--text-3)',
          }}
        />
      )}
      {label}
    </span>
    <strong data-testid={testId} style={{ ...figureStyle, fontSize: 22, lineHeight: 1.25 }}>
      {value}
    </strong>
  </div>
)

/** The row of figures under a chart. Wraps rather than scrolls. */
export const FigureRow = ({ children }: { children: ReactNode }) => (
  <div
    style={{
      display: 'flex',
      flexWrap: 'wrap',
      gap: 'var(--space-6)',
      rowGap: 'var(--space-4)',
    }}
  >
    {children}
  </div>
)

/** Room reserved at the end of a ladder track for the tip label — four digits plus its gap. */
const VALUE_GUTTER = 52

export interface LadderRow {
  key: string
  label: ReactNode
  /** Already localised — the sections own their number formatting. */
  value: string
  /** 0–1, against the largest row in the same ladder. */
  ratio: number
}

/**
 * The signature mark. Rungs hang off a single hairline spine, so the block reads as the trunk
 * of the tree it is describing — eldest generation at the top, youngest at the bottom.
 *
 * Every rung is labelled with its value because there is no axis to read it off: the axis is
 * the thing that was dropped, not the labels. Bars are one hue — ordered categories still get
 * one colour when length already carries the magnitude.
 */
export const Ladder = ({
  rows,
  rowTestId,
  ariaLabel,
}: {
  rows: readonly LadderRow[]
  rowTestId?: string
  ariaLabel?: string
}) => (
  <ul
    aria-label={ariaLabel}
    style={{
      listStyle: 'none',
      margin: 0,
      padding: 0,
      // The track columns live on the list, not on each row, so every rung's bar starts at the
      // same x and the spine is one straight line. Sized per row, a ladder of names of
      // different lengths (branches) gives every rung its own indent and the spine zigzags.
      display: 'grid',
      gridTemplateColumns: 'minmax(0, max-content) minmax(80px, 1fr)',
      gap: 0,
      // Capped rather than page-wide. Stretched across 900px a rung stops reading as a bar and
      // starts reading as a rule, and its value ends up half a screen from the mark it labels.
      maxWidth: 600,
    }}
  >
    {rows.map((row, index) => (
      <li
        key={row.key}
        data-testid={rowTestId}
        style={{
          // Inherits the list's two columns rather than declaring its own, so the <li> keeps
          // its list semantics while the columns stay shared.
          display: 'grid',
          gridTemplateColumns: 'subgrid',
          gridColumn: '1 / -1',
          alignItems: 'center',
          gap: 'var(--space-3)',
        }}
      >
        <span
          style={{
            fontSize: 13,
            color: 'var(--text-2)',
            paddingBlock: 5,
            whiteSpace: 'nowrap',
            overflow: 'hidden',
            textOverflow: 'ellipsis',
          }}
        >
          {row.label}
        </span>

        {/* The spine: one hairline per rung, no row gap, so they meet as a continuous line. */}
        <span
          style={{
            display: 'flex',
            alignItems: 'center',
            paddingBlock: 5,
            paddingInlineStart: 'var(--space-3)',
            borderInlineStart: '1px solid var(--border-strong)',
          }}
        >
          <span
            className="report-rung"
            style={{
              display: 'block',
              height: 9,
              // Scaled against the track minus the value's own width, so the tip label rides
              // the end of the bar without the longest bar pushing it out of the card.
              // No percentage floor either: a 3% minimum would draw one member and forty
              // members at visibly similar lengths. The 3px floor keeps a non-zero value
              // visible without making a claim about its size.
              width: `calc((100% - ${VALUE_GUTTER}px) * ${row.ratio})`,
              minWidth: row.ratio > 0 ? 3 : 0,
              flex: '0 0 auto',
              background: 'var(--primary)',
              borderStartEndRadius: 4,
              borderEndEndRadius: 4,
              animationDelay: `${Math.min(index, 8) * 45}ms`,
            }}
          />
          {/* At the tip, not in a far column: a value parked at the end of the track sits half
              a card away from the 3px bar it is labelling. */}
          <span
            style={{
              ...figureStyle,
              marginInlineStart: 'var(--space-2)',
              fontSize: 13,
              fontWeight: 600,
              color: 'var(--text-1)',
              whiteSpace: 'nowrap',
            }}
          >
            {row.value}
          </span>
        </span>
      </li>
    ))}
  </ul>
)

/**
 * A part-to-whole strip for exactly two parts. Separated by a 2px gap in the surface colour
 * rather than a stroke, and never carrying the meaning alone — the two figures beside it are
 * labelled and dotted.
 */
export const SplitBar = ({
  left,
  right,
  label,
}: {
  left: number
  right: number
  label: string
}) => {
  const total = left + right
  if (total === 0) return null
  return (
    <div
      role="img"
      aria-label={label}
      // Same 560 measure as the ladders: a strip that runs the whole card while the chart
      // beside it stops at 560 reads as two unrelated widths rather than one page.
      style={{ display: 'flex', gap: 2, height: 10, maxWidth: 600, marginBottom: 'var(--space-4)' }}
    >
      <span
        style={{
          flex: left,
          background: 'var(--success)',
          borderStartStartRadius: 5,
          borderEndStartRadius: 5,
        }}
      />
      <span
        style={{
          flex: right,
          background: 'var(--text-3)',
          borderStartEndRadius: 5,
          borderEndEndRadius: 5,
        }}
      />
    </div>
  )
}

/** A single-value progress strip: how much of the record set is complete. */
export const Meter = ({ ratio, label }: { ratio: number; label: string }) => (
  <div
    role="img"
    aria-label={label}
    style={{
      height: 8,
      maxWidth: 600,
      borderRadius: 'var(--r-pill)',
      background: 'var(--sunken)',
      overflow: 'hidden',
    }}
  >
    <span
      style={{
        display: 'block',
        height: '100%',
        width: `${Math.round(ratio * 100)}%`,
        background: 'var(--primary)',
        borderRadius: 'var(--r-pill)',
      }}
    />
  </div>
)

type BadgeTone = 'neutral' | 'attention'

/** A count that is also a claim about outstanding work, so it carries a tone. */
export const Badge = ({
  children,
  tone = 'neutral',
  testId,
}: {
  children: ReactNode
  tone?: BadgeTone
  testId?: string
}) => (
  <span
    data-testid={testId}
    style={{
      ...figureStyle,
      display: 'inline-block',
      padding: '1px 8px',
      borderRadius: 'var(--r-pill)',
      fontSize: 12,
      fontWeight: 600,
      lineHeight: 1.6,
      background: tone === 'attention' ? 'var(--warning-subtle)' : 'var(--sunken)',
      color: tone === 'attention' ? 'var(--warning)' : 'var(--text-2)',
    }}
  >
    {children}
  </span>
)

/** 11px muted text: the disclosures and caveats this page makes a point of stating. */
export const Note = ({ children, testId }: { children: ReactNode; testId?: string }) => (
  <p data-testid={testId} style={{ margin: 0, fontSize: 11, color: 'var(--text-3)' }}>
    {children}
  </p>
)

/**
 * The container for a worklist: rows separated by dividers, never by bullets.
 *
 * `columns` is for lists whose rows are a name and nothing else. One short name per full-width
 * line leaves most of the card empty and makes a queue of four look like a queue of forty; two
 * or three columns give the same names a shape a reader can take in at once.
 */
export const RowList = ({ children, columns = false }: { children: ReactNode; columns?: boolean }) => (
  <ul
    style={{
      listStyle: 'none',
      margin: 0,
      padding: 0,
      ...(columns
        ? {
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))',
            columnGap: 'var(--space-6)',
          }
        : { maxWidth: 600 }),
    }}
  >
    {children}
  </ul>
)

/**
 * One member in a worklist. The link wraps the name and nothing else — its accessible name is
 * the composed lineage, which is how the rest of the app refers to a person.
 */
export const Row = ({
  children,
  trailing,
  testId,
  divider = true,
}: {
  children: ReactNode
  trailing?: ReactNode
  testId?: string
  /**
   * Off inside a columned list. A rule per row across three columns does not make a table —
   * the last grid row is short, so the rules end mid-air and the block reads as broken.
   */
  divider?: boolean
}) => (
  <li
    data-testid={testId}
    style={{
      display: 'flex',
      alignItems: 'baseline',
      gap: 'var(--space-3)',
      padding: divider ? '5px 0' : '4px 0',
      fontSize: 13,
      borderTop: divider ? '1px solid var(--divider)' : undefined,
    }}
  >
    <span style={{ minWidth: 0, flex: 1 }}>{children}</span>
    {trailing !== undefined && (
      <span
        style={{
          ...figureStyle,
          flex: '0 0 auto',
          fontSize: 12,
          color: 'var(--text-3)',
          whiteSpace: 'nowrap',
        }}
      >
        {trailing}
      </span>
    )}
  </li>
)

/** An empty state that reads as a settled fact rather than a missing screen. */
export const Empty = ({ children, testId }: { children: ReactNode; testId?: string }) => (
  <p
    data-testid={testId}
    style={{
      margin: 0,
      padding: 'var(--space-6) var(--space-4)',
      textAlign: 'center',
      fontSize: 13,
      color: 'var(--text-3)',
      background: 'var(--sunken)',
      borderRadius: 'var(--r-md)',
    }}
  >
    {children}
  </p>
)

/** Vertical rhythm inside a card, so sections do not each invent their own gap. */
export const Stack = ({ children, gap = 'var(--space-4)' }: { children: ReactNode; gap?: string }) => (
  <div style={{ display: 'flex', flexDirection: 'column', gap }}>{children}</div>
)
