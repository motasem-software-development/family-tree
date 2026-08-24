import type { CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import { SearchableSelect } from '../../components/SearchableSelect'
import { countryName, flagEmoji } from '../countries/flagEmoji'
import type { Country } from '../countries/types'
import { joinPhone, splitPhone } from './contactDetails'

interface PhoneInputProps {
  id: string
  label: string
  /** The stored E.164 value, or null when not recorded. */
  value: string | null
  countries: readonly Country[]
  disabled?: boolean
  onChange: (e164: string | null) => void
  labelStyle: CSSProperties
  controlStyle: CSSProperties
}

/**
 * Specification §5.2's picker: `[🇵🇸 +970 ▼] [599123456]`. The two controls are a presentation
 * detail — the value that leaves here is always one composed E.164 string, because §5.1 is
 * explicit that the dialing code is not stored separately.
 *
 * The split is recomputed from the value on every render rather than held as state: a parent
 * that replaces the value (loading a different member into the same form) must not leave a
 * stale dial code behind.
 */
export function PhoneInput({
  id,
  label,
  value,
  countries,
  disabled = false,
  onChange,
  labelStyle,
  controlStyle,
}: PhoneInputProps) {
  const { t, i18n } = useTranslation()
  const { dialCode, local } = splitPhone(value, countries)

  // Deduplicated: +1 is both US and CA, and two identical options in a select is a bug the user
  // can see. Sorted numerically so the list reads the same in both languages.
  const dialCodes = [...new Set(countries.map((country) => country.dialCode))].sort(
    (a, b) => Number(a.slice(1)) - Number(b.slice(1)),
  )

  const dialOptions = dialCodes.map((code) => {
    const owners = countries.filter((country) => country.dialCode === code)
    // One owner: show its name. Several: the code alone, or the row becomes a paragraph —
    // +1 is shared by twenty countries and +44 by four.
    const single = owners.length === 1 ? owners[0] : undefined
    const label =
      single === undefined
        ? code
        : `${flagEmoji(single.code)} ${code} ${countryName(single, i18n.language)}`

    return {
      value: code,
      label,
      // Every owner's names and ISO code, so "jersey" or "JE" still finds the +44 row even
      // though the row cannot afford to spell them all out.
      keywords: owners.flatMap((country) => [country.code, country.nameAr, country.nameEn]),
    }
  })

  return (
    <div style={{ marginBottom: 'var(--space-4)' }}>
      <label htmlFor={`${id}-local`} style={labelStyle}>
        {label}
      </label>
      <div style={{ display: 'flex', gap: 8 }}>
        <SearchableSelect
          id={`${id}-dial`}
          ariaLabel={t('members.dialCode')}
          value={dialCode}
          options={dialOptions}
          emptyLabel="—"
          placeholder={t('members.searchPlaceholder')}
          noResultsLabel={t('members.noMatches')}
          disabled={disabled}
          onChange={(code) => onChange(joinPhone(code, local))}
          controlStyle={{ ...controlStyle, width: 'auto', flex: '0 0 auto', minWidth: 170 }}
        />
        <input
          id={`${id}-local`}
          aria-label={t('members.localNumber')}
          value={local}
          disabled={disabled}
          inputMode="tel"
          autoComplete="tel-national"
          maxLength={15}
          onChange={(event) => onChange(joinPhone(dialCode, event.target.value))}
          style={{ ...controlStyle, flex: 1 }}
        />
      </div>
    </div>
  )
}
