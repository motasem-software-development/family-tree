import type { CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
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

  const labelFor = (code: string): string => {
    const owners = countries.filter((country) => country.dialCode === code)
    const flags = owners.map((country) => flagEmoji(country.code)).join('')
    // One owner: show its name. Several: the flags alone, or the row becomes a paragraph.
    const name = owners.length === 1 ? ` ${countryName(owners[0], i18n.language)}` : ''
    return `${flags} ${code}${name}`
  }

  return (
    <div style={{ marginBottom: 'var(--space-4)' }}>
      <label htmlFor={`${id}-local`} style={labelStyle}>
        {label}
      </label>
      <div style={{ display: 'flex', gap: 8 }}>
        <select
          id={`${id}-dial`}
          aria-label={t('members.dialCode')}
          value={dialCode}
          disabled={disabled}
          onChange={(event) => onChange(joinPhone(event.target.value, local))}
          style={{ ...controlStyle, width: 'auto', flex: '0 0 auto', minWidth: 150 }}
        >
          <option value="">—</option>
          {dialCodes.map((code) => (
            <option key={code} value={code}>
              {labelFor(code)}
            </option>
          ))}
        </select>
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
