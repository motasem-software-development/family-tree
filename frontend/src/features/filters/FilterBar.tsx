import { useEffect, useState, type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import { SearchableSelect, type SelectOption } from '../../components/SearchableSelect'
import { countryName, flagEmoji } from '../countries/flagEmoji'
import { useCountriesQuery } from '../countries/useCountries'
import { useBranchesQuery, useGenerationsQuery } from '../members/useMembers'
import { useDebouncedValue } from '../tree/useDebouncedValue'
import type { MemberFilters, MemberStatusFilter } from './filterParams'

/** Long enough that a fast typist issues one request, short enough not to feel laggy. */
const SEARCH_DEBOUNCE_MS = 300

export type FilterLayout = 'inline' | 'stacked'

interface FilterBarProps {
  filters: MemberFilters
  activeCount: number
  onChange: <K extends keyof MemberFilters>(key: K, value: MemberFilters[K] | undefined) => void
  onReset: () => void
  layout?: FilterLayout
}

const labelStyle: CSSProperties = {
  display: 'block',
  marginBottom: 6,
  fontSize: 12,
  fontWeight: 500,
  color: 'var(--text-2)',
}

const controlStyle: CSSProperties = {
  width: '100%',
  height: 'var(--control-h-md)',
  padding: '0 12px',
  border: '1px solid var(--border-strong)',
  borderRadius: 'var(--r-md)',
  background: 'var(--surface)',
  color: 'var(--text-1)',
  fontFamily: 'inherit',
  fontSize: 14,
}

const STATUSES: readonly MemberStatusFilter[] = ['all', 'alive', 'deceased']

/**
 * The five filters of specification §15, rendered once and shared by both pages and both
 * layouts — building them twice guarantees drift (design spec §6.1).
 *
 * Presentation only: it owns no filter state. The one exception is the search box's draft,
 * explained where it is held.
 */
export function FilterBar({
  filters,
  activeCount,
  onChange,
  onReset,
  layout = 'inline',
}: FilterBarProps) {
  const { t, i18n } = useTranslation()

  const { data: branches } = useBranchesQuery(filters.rootId)
  const { data: generations } = useGenerationsQuery(filters.rootId)
  const { data: countries } = useCountriesQuery()

  /**
   * The search box holds its own draft and pushes the settled value up.
   *
   * It is deliberately not driven from `filters.search` on every render: the URL round-trip lags
   * the keystroke by the debounce interval, so feeding it back would move the cursor under the
   * user's hands. The effect below re-syncs it only when the filter changes from elsewhere —
   * Reset, or a link arriving with a term already in it.
   */
  const [draft, setDraft] = useState(filters.search ?? '')
  const settled = useDebouncedValue(draft, SEARCH_DEBOUNCE_MS)

  useEffect(() => {
    setDraft(filters.search ?? '')
  }, [filters.search])

  // Keyed on the settled value alone. The guard is what makes that safe: a re-render for any
  // other reason finds settled already equal to the filter and does nothing, so this cannot
  // loop against the effect above.
  useEffect(() => {
    const current = filters.search ?? ''
    if (settled === current) return
    onChange('search', settled === '' ? undefined : settled)
  }, [settled, filters.search, onChange])

  // Both names and the ISO code ride along as keywords, so an Arabic reader can find a country
  // by typing "japan" and an English one by typing "JP" — the same treatment ContactFields gives
  // the member form's picker.
  const countryOptions: SelectOption[] = [...(countries ?? [])]
    .sort((a, b) => countryName(a, i18n.language).localeCompare(countryName(b, i18n.language)))
    .map((country) => ({
      value: String(country.id),
      label: `${flagEmoji(country.code)} ${countryName(country, i18n.language)}`,
      keywords: [country.code, country.nameAr, country.nameEn, country.dialCode],
    }))

  const branchOptions: SelectOption[] = (branches ?? []).map((branch) => ({
    value: branch.id,
    label: branch.name,
  }))

  const field = (
    key: string,
    label: string,
    control: React.ReactNode,
    minWidth: number,
  ): React.ReactNode => (
    <div key={key} style={{ flex: layout === 'inline' ? `1 1 ${minWidth}px` : '1 1 auto' }}>
      <label htmlFor={`filter-${key}`} style={labelStyle}>
        {label}
      </label>
      {control}
    </div>
  )

  return (
    <div
      // Not a <form>: there is nothing to submit. Every control applies as it changes, so a
      // submit button would be a second way to do what already happened.
      role="group"
      aria-label={t('filters.title')}
      data-layout={layout}
      style={{
        display: 'flex',
        flexDirection: layout === 'inline' ? 'row' : 'column',
        alignItems: layout === 'inline' ? 'flex-end' : 'stretch',
        gap: 'var(--space-3)',
        flexWrap: layout === 'inline' ? 'wrap' : 'nowrap',
      }}
    >
      {field(
        'search',
        t('filters.search'),
        <input
          id="filter-search"
          type="search"
          value={draft}
          onChange={(event) => setDraft(event.target.value)}
          placeholder={t('filters.searchPlaceholder')}
          style={controlStyle}
        />,
        180,
      )}

      {field(
        'status',
        t('filters.status'),
        <select
          id="filter-status"
          value={filters.status ?? 'all'}
          onChange={(event) => {
            const value = event.target.value as MemberStatusFilter
            onChange('status', value === 'all' ? undefined : value)
          }}
          style={controlStyle}
        >
          {STATUSES.map((status) => (
            <option key={status} value={status}>
              {t(
                status === 'all'
                  ? 'filters.statusAll'
                  : status === 'alive'
                    ? 'filters.statusAlive'
                    : 'filters.statusDeceased',
              )}
            </option>
          ))}
        </select>,
        130,
      )}

      {field(
        'branch',
        t('filters.branch'),
        // Searchable rather than native: the branch list grows with the family, and the same
        // control on the country field would otherwise be two idioms side by side.
        <SearchableSelect
          id="filter-branch"
          ariaLabel={t('filters.branch')}
          value={filters.branchId ?? ''}
          options={branchOptions}
          emptyLabel={t('filters.branchAll')}
          noResultsLabel={t('filters.noResults')}
          onChange={(value) => onChange('branchId', value === '' ? undefined : value)}
          controlStyle={controlStyle}
        />,
        150,
      )}

      {field(
        'generation',
        t('filters.generation'),
        <select
          id="filter-generation"
          value={filters.generation === undefined ? '' : String(filters.generation)}
          onChange={(event) =>
            onChange(
              'generation',
              event.target.value === '' ? undefined : Number(event.target.value),
            )
          }
          style={controlStyle}
        >
          <option value="">{t('filters.generationAll')}</option>
          {(generations ?? []).map((generation) => (
            <option key={generation} value={generation}>
              {/* "0" alone reads as a missing value rather than as the root (§21). */}
              {generation === 0 ? t('filters.generationRoot') : generation}
            </option>
          ))}
        </select>,
        140,
      )}

      {field(
        'country',
        t('filters.country'),
        <SearchableSelect
          id="filter-country"
          ariaLabel={t('filters.country')}
          value={filters.countryId === undefined ? '' : String(filters.countryId)}
          options={countryOptions}
          emptyLabel={t('filters.countryAll')}
          noResultsLabel={t('filters.noResults')}
          onChange={(value) => onChange('countryId', value === '' ? undefined : Number(value))}
          controlStyle={controlStyle}
        />,
        170,
      )}

      <button
        type="button"
        onClick={onReset}
        // A live Reset over an unfiltered list is a control that does nothing.
        disabled={activeCount === 0}
        style={{
          height: 'var(--control-h-md)',
          padding: '0 16px',
          border: '1px solid var(--border-strong)',
          borderRadius: 'var(--r-md)',
          background: 'var(--surface)',
          color: activeCount === 0 ? 'var(--text-4)' : 'var(--text-1)',
          fontFamily: 'inherit',
          fontSize: 13,
          cursor: activeCount === 0 ? 'default' : 'pointer',
          whiteSpace: 'nowrap',
          alignSelf: layout === 'inline' ? 'flex-end' : 'stretch',
        }}
      >
        {t('filters.reset')}
      </button>
    </div>
  )
}
