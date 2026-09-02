import { useEffect, useRef, useState, type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import { useQueryClient } from '@tanstack/react-query'
import { AppShell } from '../../app/AppShell'
import { useAuth } from '../auth/AuthContext'
import { ApiError } from '../../services/apiClient'
import { countryName, flagEmoji } from '../countries/flagEmoji'
import { useCountriesQuery } from '../countries/useCountries'
import { FilterControls } from '../filters/FilterControls'
import { useMemberFilters } from '../filters/useMemberFilters'
import type { ContactDetails } from './contactDetails'
import { fullName, indexById, lineageName } from './fullName'
import { ageYears, formatLifeDate, lifeDetailsOf, type LifeDetails } from './lifeDetails'
import { LifeStatusDot } from './LifeStatusDot'
import { MemberForm } from './MemberForm'
import { downloadMembersXlsx } from './membersExportApi'
import {
  memberKeys,
  useCreateMember,
  useDeleteMember,
  useMembersQuery,
  useUpdateMember,
} from './useMembers'
import type { FamilyMember, FamilyMemberListItem } from './types'

type Editing = { mode: 'none' } | { mode: 'add' } | { mode: 'edit'; member: FamilyMember }

const codeOf = (error: unknown): string => (error instanceof ApiError ? error.code : 'UNKNOWN')

const cellStyle: CSSProperties = {
  padding: '11px 14px',
  fontSize: 14,
  textAlign: 'start',
  borderBottom: '1px solid var(--divider)',
}

const headCellStyle: CSSProperties = {
  ...cellStyle,
  fontSize: 12,
  fontWeight: 600,
  color: 'var(--text-3)',
  background: 'var(--sunken)',
  whiteSpace: 'nowrap',
}

/**
 * The columns that hold a figure rather than a word: national ID, both dates and the age. Tabular
 * numerals so the digits line up down the column, and no wrapping — a date broken across two
 * lines stops being scannable, which is the only reason to put it in a column of its own.
 */
const figureCellStyle: CSSProperties = {
  ...cellStyle,
  color: 'var(--text-3)',
  fontFamily: 'var(--mono)',
  fontVariantNumeric: 'tabular-nums',
  whiteSpace: 'nowrap',
}

/**
 * The whole row is tinted by life status: a faint green for the living, the sunken grey for the
 * dead. It replaces the Status column — the fact is worth seeing at a glance down 351 rows, and
 * a column of repeated words is a poor way to show it.
 *
 * The tint is never the only carrier of the status. The dot in the name cell is labelled for
 * screen readers, so the fact survives both a colour-blind reader and a monochrome print.
 */
const rowStyle = (deceased: boolean): CSSProperties => ({
  background: deceased ? 'var(--sunken)' : 'var(--success-subtle)',
})

const rowButtonStyle = (danger: boolean): CSSProperties => ({
  height: 'var(--control-h-sm)',
  padding: '0 12px',
  border: `1px solid ${danger ? 'var(--error)' : 'var(--border-strong)'}`,
  borderRadius: 'var(--r-md)',
  background: 'var(--surface)',
  color: danger ? 'var(--error)' : 'var(--text-1)',
  fontFamily: 'inherit',
  fontSize: 13,
  cursor: 'pointer',
})

export function MembersPage() {
  const { t, i18n } = useTranslation()
  const { user, hasPermission } = useAuth()
  const queryClient = useQueryClient()
  const { filters, activeCount, setFilter, reset } = useMemberFilters()
  const { data: members, isLoading } = useMembersQuery(filters)
  /**
   * The whole family, unfiltered. Two things need it and neither is about what is on screen:
   * the lineage index — a filtered list has holes in it, and a member whose father was filtered
   * out must not lose their father's name — and the parent picker, which has to offer every
   * member regardless of the current view. One extra cached query against a 351-row endpoint,
   * against a name that would otherwise change as the user filters.
   */
  const { data: everyone } = useMembersQuery()
  const { data: countries } = useCountriesQuery()
  const createMember = useCreateMember()
  const updateMember = useUpdateMember()
  const deleteMember = useDeleteMember()

  const [editing, setEditing] = useState<Editing>({ mode: 'none' })
  const [pendingDelete, setPendingDelete] = useState<FamilyMember | null>(null)
  const [errorCode, setErrorCode] = useState<string | null>(null)
  const [isExporting, setIsExporting] = useState(false)

  /**
   * The form renders above the list, inside the page's own scroll container. Editing a member
   * from a row far down the list would otherwise open a form the user cannot see — nothing
   * appears to happen, and the row's Edit button looks broken.
   */
  const formRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (editing.mode === 'none') return
    const form = formRef.current
    // jsdom has no layout and so no scrollIntoView; the form still opens without it.
    if (form !== null && typeof form.scrollIntoView === 'function')
      form.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }, [editing])

  const close = () => setEditing({ mode: 'none' })

  const handleCreate = (
    name: string,
    parentId: string | null,
    life: LifeDetails,
    contact: ContactDetails,
  ) => {
    setErrorCode(null)
    createMember.mutate(
      { name, parentId, life, contact },
      { onSuccess: close, onError: (error) => setErrorCode(codeOf(error)) },
    )
  }

  const handleUpdate = (
    target: FamilyMember,
    name: string,
    life: LifeDetails,
    contact: ContactDetails,
  ) => {
    setErrorCode(null)
    updateMember.mutate(
      { id: target.id, name, version: target.version, life, contact },
      {
        onSuccess: close,
        onError: (error) => {
          const code = codeOf(error)
          setErrorCode(code)
          // A CONCURRENCY_CONFLICT means the form is holding a stale version — retrying
          // against it just reproduces the same 409, so refetch and close so the next open
          // gets the current version. Any other error (e.g. a validation failure) means the
          // form is holding input the user still needs to fix — closing it would discard
          // their edit for no benefit, so leave it open.
          if (code === 'CONCURRENCY_CONFLICT') {
            void queryClient.invalidateQueries({ queryKey: memberKeys.all })
            close()
          }
        },
      },
    )
  }

  const confirmDelete = () => {
    if (pendingDelete === null) return
    setErrorCode(null)
    deleteMember.mutate(pendingDelete.id, {
      onSettled: () => setPendingDelete(null),
      onError: (error) => setErrorCode(codeOf(error)),
    })
  }

  const all = members ?? []
  const unfiltered = everyone ?? []
  // One reference day for every row, taken once per render rather than per cell: a list that
  // asked the clock 351 times could in principle straddle midnight and show two different ages
  // for two members born on the same day.
  const today = new Date()
  // Indexed once per render: every row needs to walk its own parent chain to compose the name.
  // Built from the unfiltered list — see the query above.
  const byId = indexById(unfiltered)
  const familyName = user?.familyTreeName ?? ''
  const isFiltered = activeCount > 0

  const handleExport = async () => {
    setErrorCode(null)
    setIsExporting(true)
    try {
      // The filters currently in the URL, passed through rather than re-derived — the server
      // re-runs them, so the file matches what the page is showing.
      await downloadMembersXlsx(filters, i18n.language, `${familyName}.xlsx`)
    } catch {
      // No coded errors to distinguish here: the only 400 the endpoint gives is for a status the
      // filter controls cannot produce, so anything reaching this point is a transport failure.
      setErrorCode('MEMBERS_EXPORT_FAILED')
    } finally {
      setIsExporting(false)
    }
  }


  /** The flag and the localised name, or a dash when the member has no country on file. */
  const countryCell = (member: FamilyMemberListItem): string => {
    const country = countries?.find((candidate) => candidate.id === member.countryId)
    if (country === undefined) return '—'
    return `${flagEmoji(country.code)} ${countryName(country, i18n.language)}`
  }

  return (
    <AppShell familyName={familyName} statLine={t('tree.membersCount', { count: unfiltered.length })}>
      {/* 32px of gutter on each side takes a fifth of a 320px screen. Scales with the viewport
          and stops at the designed --space-8, so wide screens are unchanged. */}
      <div
        style={{
          flex: 1,
          minWidth: 0,
          overflow: 'auto',
          padding: 'clamp(var(--space-4), 4vw, var(--space-8))',
        }}
      >
        <div style={{ maxWidth: 900, margin: '0 auto' }}>
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              flexWrap: 'wrap',
              gap: 'var(--space-4)',
              marginBottom: 'var(--space-6)',
            }}
          >
            <h1 style={{ margin: 0, fontSize: 22, fontWeight: 700 }}>{t('members.title')}</h1>
            <div style={{ display: 'flex', gap: 'var(--space-3)' }}>
            {/* Guarded by Member.View, the permission that gets you this page at all — the
                export carries exactly the data already on screen (design spec §1.4). */}
            {hasPermission('Member.View') && (
              <button
                type="button"
                onClick={() => void handleExport()}
                // Exporting zero rows produces a header-only workbook, which is a confusing
                // thing to hand someone who just clicked Export.
                disabled={isExporting || all.length === 0}
                style={{
                  height: 'var(--control-h-md)',
                  padding: '0 16px',
                  border: '1px solid var(--border-strong)',
                  borderRadius: 'var(--r-md)',
                  background: 'var(--surface)',
                  color: all.length === 0 || isExporting ? 'var(--text-4)' : 'var(--text-1)',
                  fontFamily: 'inherit',
                  fontSize: 13,
                  fontWeight: 600,
                  cursor: all.length === 0 || isExporting ? 'default' : 'pointer',
                  whiteSpace: 'nowrap',
                }}
              >
                {t(isExporting ? 'members.exporting' : 'members.export')}
              </button>
            )}
            {hasPermission('Member.Create') && editing.mode === 'none' && (
              <button
                type="button"
                onClick={() => setEditing({ mode: 'add' })}
                style={{
                  height: 'var(--control-h-md)',
                  padding: '0 16px',
                  border: 'none',
                  borderRadius: 'var(--r-md)',
                  background: 'var(--primary)',
                  color: '#fff',
                  fontFamily: 'inherit',
                  fontSize: 13,
                  fontWeight: 600,
                  cursor: 'pointer',
                }}
              >
                {t('members.add')}
              </button>
            )}
            </div>
          </div>

          {/* Error text comes from the stable server code, never from the server's message —
              the UI is bilingual and message text is not part of the contract. */}
          {errorCode !== null && (
            <p
              role="alert"
              style={{
                margin: '0 0 var(--space-5)',
                padding: '10px 12px',
                borderRadius: 'var(--r-md)',
                background: 'var(--error-subtle)',
                color: 'var(--error)',
                fontSize: 13,
              }}
            >
              {t(`errors.${errorCode}`, t('errors.UNKNOWN'))}
            </p>
          )}

          <div ref={formRef}>
            {editing.mode === 'add' && (
              <MemberForm
                parents={unfiltered}
                isSaving={createMember.isPending}
                onSubmit={handleCreate}
                onCancel={close}
              />
            )}

            {editing.mode === 'edit' && (
              <MemberForm
                // Keyed by member, so switching editors remounts the form. MemberForm seeds its
                // name, life and contact state from useState initialisers, which do not re-run
                // on a re-render: without this, clicking Edit on a second row while the first is
                // open kept the first member's values in the fields while onSubmit closed over
                // the second — and Update is replace-semantics, so Save overwrote the second
                // member's details with the first member's.
                key={editing.member.id}
                member={editing.member}
                parents={unfiltered.filter((candidate) => candidate.id !== editing.member.id)}
                isSaving={updateMember.isPending}
                onSubmit={(name, _parentId, life, contact) =>
                  handleUpdate(editing.member, name, life, contact)
                }
                onCancel={close}
              />
            )}
          </div>

          <div style={{ marginBottom: 'var(--space-5)' }}>
            <FilterControls
              filters={filters}
              activeCount={activeCount}
              onChange={setFilter}
              onReset={reset}
            />
          </div>

          {isLoading ? (
            <p style={{ color: 'var(--text-3)' }}>{t('members.loading')}</p>
          ) : all.length === 0 ? (
            <div
              style={{
                padding: 'var(--space-12) var(--space-6)',
                textAlign: 'center',
                color: 'var(--text-3)',
                background: 'var(--surface)',
                border: '1px solid var(--border)',
                borderRadius: 'var(--r-lg)',
              }}
            >
              {/* "No members yet" over a filtered-to-zero list tells the user something false.
                  The two empty states are different facts and read differently. */}
              {isFiltered ? t('filters.emptyFiltered') : t('members.empty')}
              {isFiltered && (
                <div style={{ marginTop: 'var(--space-4)' }}>
                  <button
                    type="button"
                    onClick={reset}
                    style={{
                      height: 'var(--control-h-md)',
                      padding: '0 16px',
                      border: '1px solid var(--border-strong)',
                      borderRadius: 'var(--r-md)',
                      background: 'var(--surface)',
                      color: 'var(--text-1)',
                      fontFamily: 'inherit',
                      fontSize: 13,
                      cursor: 'pointer',
                    }}
                  >
                    {t('filters.reset')}
                  </button>
                </div>
              )}
            </div>
          ) : (
            <div
              style={{
                background: 'var(--surface)',
                border: '1px solid var(--border)',
                borderRadius: 'var(--r-lg)',
                overflow: 'hidden',
              }}
            >
              {/* The one place horizontal scrolling is right rather than a failure: seven columns
                  of member data cannot be read at 320px, and folding them into stacked cards
                  would drop the column headers that say what each value is. The scroll is
                  contained by the card, so the page itself never scrolls sideways. */}
              <div style={{ overflowX: 'auto' }}>
                <table style={{ width: '100%', minWidth: 860, borderCollapse: 'collapse' }}>
                  <thead>
                    {/* Identity first (who this is), then life (when), then placement — in the
                        family and in the world. Two columns are deliberately absent: the father,
                        because the lineage already follows every name in the first column, and
                        the status, which the row's own colour now carries. The death date rides
                        with the birth date, revealed on hover — see the birth cell below. */}
                    <tr>
                      <th style={headCellStyle}>{t('members.name')}</th>
                      <th style={headCellStyle}>{t('members.nationalId')}</th>
                      <th style={headCellStyle}>{t('members.dateOfBirth')}</th>
                      <th style={headCellStyle}>{t('members.age')}</th>
                      <th style={headCellStyle}>{t('filters.country')}</th>
                      <th style={headCellStyle}>{t('filters.branch')}</th>
                      <th style={headCellStyle} />
                    </tr>
                  </thead>
                  <tbody>
                    {all.map((current) => {
                      const life = lifeDetailsOf(current)
                      const age = ageYears(life, today)

                      const died = formatLifeDate(life.dateOfDeath, i18n.language)

                      return (
                        <tr key={current.id} className="member-row" style={rowStyle(life.isDeceased)}>
                          <td style={{ ...cellStyle, fontWeight: 500 }}>
                            <span style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                              {/* The labelled, non-colour carrier of the life status the row is
                                  tinted by — see rowStyle. */}
                              <LifeStatusDot deceased={life.isDeceased} />
                              {/* Own name, then father, grandfather and great-grandfather. The
                                  lineage is muted so the row still scans by given name — it is
                                  context, not four names of equal weight. */}
                              <span>
                                {current.name}
                                {lineageName(current, byId) !== '' && (
                                  <span style={{ fontWeight: 400, color: 'var(--text-3)' }}>
                                    {' '}
                                    {lineageName(current, byId)}
                                  </span>
                                )}
                              </span>
                            </span>
                          </td>
                          <td style={figureCellStyle}>{current.nationalId ?? '—'}</td>
                          {/* Birth date, and — for a member who has died — the death date beside
                              it in the genealogy convention, revealed when the row is hovered.
                              It holds its space while hidden, so nothing shifts under the
                              pointer; see .revealed-on-hover in index.css. */}
                          <td style={figureCellStyle}>
                            {formatLifeDate(life.dateOfBirth, i18n.language) ?? '—'}
                            {died !== null && (
                              <span className="revealed-on-hover" style={{ color: 'var(--text-3)' }}>
                                {' – '}
                                {died}
                              </span>
                            )}
                          </td>
                          {/* Localised so Arabic gets Arabic-Indic numerals, and ungrouped — an
                              age is never large enough for a separator to be anything but noise. */}
                          <td style={figureCellStyle}>
                            {age === null
                              ? '—'
                              : age.toLocaleString(i18n.language, { useGrouping: false })}
                          </td>
                          <td style={{ ...cellStyle, color: 'var(--text-3)', whiteSpace: 'nowrap' }}>
                            {countryCell(current)}
                          </td>
                          <td style={{ ...cellStyle, color: 'var(--text-3)' }}>
                            {/* The root belongs to no branch; specification §21 renders that as
                                "Root" rather than as a blank cell. */}
                            {current.branchName ?? t('filters.branchRoot')}
                          </td>
                          <td style={{ ...cellStyle, whiteSpace: 'nowrap' }}>
                            <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
                              {hasPermission('Member.Edit') && (
                                <button
                                  type="button"
                                  onClick={() => setEditing({ mode: 'edit', member: current })}
                                  style={rowButtonStyle(false)}
                                >
                                  {t('members.edit')}
                                </button>
                              )}
                              {hasPermission('Member.Delete') && (
                                <button
                                  type="button"
                                  onClick={() => setPendingDelete(current)}
                                  style={rowButtonStyle(true)}
                                >
                                  {t('members.delete')}
                                </button>
                              )}
                            </div>
                          </td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      </div>

      {/* An in-app dialog rather than window.confirm: the native one cannot be styled, ignores
          the app's direction, and renders in the browser's language, not the app's. */}
      {pendingDelete !== null && (
        <div
          style={{
            position: 'fixed',
            inset: 0,
            background: 'rgba(21,24,27,.36)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            zIndex: 400,
            // A fixed 24px gutter costs a 320px screen 15% of its width. Scales with the
            // viewport and stops at the designed 24px, so wide screens are unchanged.
            padding: 'clamp(12px, 4vw, 24px)',
          }}
        >
          <div
            role="dialog"
            aria-modal="true"
            aria-label={t('members.confirmDelete', { name: fullName(pendingDelete, byId) })}
            style={{
              width: '100%',
              maxWidth: 420,
              // Never taller than the viewport, so the confirm and cancel buttons stay on
              // screen on a short phone or in landscape.
              maxHeight: '100%',
              overflowY: 'auto',
              padding: 'var(--space-6)',
              background: 'var(--surface)',
              borderRadius: 'var(--r-lg)',
              boxShadow: 'var(--shadow-high)',
              animation: 'fadeUp var(--motion-base) var(--ease-standard)',
            }}
          >
            <div style={{ fontSize: 17, fontWeight: 600, marginBottom: 8 }}>
              {t('members.confirmDelete', { name: fullName(pendingDelete, byId) })}
            </div>
            <div style={{ fontSize: 14, color: 'var(--text-2)', marginBottom: 'var(--space-6)' }}>
              {t('modal.deleteBody')}
            </div>
            <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
              <button
                type="button"
                onClick={() => setPendingDelete(null)}
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
                {t('modal.cancel')}
              </button>
              <button
                type="button"
                onClick={confirmDelete}
                disabled={deleteMember.isPending}
                style={{
                  height: 38,
                  padding: '0 16px',
                  border: 'none',
                  borderRadius: 'var(--r-md)',
                  background: 'var(--error)',
                  color: '#fff',
                  fontFamily: 'inherit',
                  fontSize: 13,
                  fontWeight: 500,
                  cursor: deleteMember.isPending ? 'wait' : 'pointer',
                  opacity: deleteMember.isPending ? 0.7 : 1,
                }}
              >
                {deleteMember.isPending ? t('members.deleting') : t('modal.confirmDelete')}
              </button>
            </div>
          </div>
        </div>
      )}
    </AppShell>
  )
}
