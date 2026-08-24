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
import { lifeDetailsOf, lifeYears, type LifeDetails } from './lifeDetails'
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
  /** Life years for one row, in the active language — see lifeYears for the convention. */
  const yearsOf = (m: FamilyMember): string | null => lifeYears(lifeDetailsOf(m), i18n.language)
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
      <div style={{ flex: 1, minWidth: 0, overflow: 'auto', padding: 'var(--space-8)' }}>
        <div style={{ maxWidth: 900, margin: '0 auto' }}>
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
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
              <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                <thead>
                  <tr>
                    <th style={headCellStyle}>{t('members.name')}</th>
                    <th style={headCellStyle}>{t('members.parent')}</th>
                    <th style={headCellStyle}>{t('filters.country')}</th>
                    <th style={headCellStyle}>{t('filters.branch')}</th>
                    <th style={headCellStyle} />
                  </tr>
                </thead>
                <tbody>
                  {all.map((current) => (
                    <tr key={current.id}>
                      <td style={{ ...cellStyle, fontWeight: 500 }}>
                        <span style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                          <LifeStatusDot deceased={lifeDetailsOf(current).isDeceased} />
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
                          {/* Same treatment as the tree outline, so a member reads the same way
                              whichever screen they are looked at on. */}
                          {yearsOf(current) !== null && (
                            <span
                              style={{
                                fontFamily: 'var(--mono)',
                                fontSize: 11,
                                fontWeight: 400,
                                color: 'var(--text-3)',
                              }}
                            >
                              {yearsOf(current)}
                            </span>
                          )}
                        </span>
                      </td>
                      <td style={{ ...cellStyle, color: 'var(--text-3)' }}>
                        {current.parentId === null
                          ? t('members.noParent')
                          : (byId.get(current.parentId)?.name ?? '—')}
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
                  ))}
                </tbody>
              </table>
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
            padding: 24,
          }}
        >
          <div
            role="dialog"
            aria-modal="true"
            aria-label={t('members.confirmDelete', { name: fullName(pendingDelete, byId) })}
            style={{
              width: '100%',
              maxWidth: 420,
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
