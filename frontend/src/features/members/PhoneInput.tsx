import { useEffect, useRef, useState, type CSSProperties } from 'react'
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
 * The split is recomputed from the value on every render rather than held as state, so that a
 * parent replacing the value cannot leave a stale dial code behind. The one exception is a
 * code chosen before any digits exist, which the value provably cannot represent — see
 * `pendingDial` below, including how it is discarded when a different member loads.
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
  const stored = splitPhone(value, countries)

  /**
   * A dial code the user picked that the stored value cannot hold yet. `joinPhone` returns
   * null until there is a local number to compose with, so choosing the code first — the order
   * everyone actually uses — would round-trip through null and snap the picker back to "—",
   * making the code impossible to set before the digits.
   */
  const [pendingDial, setPendingDial] = useState('')
  // What we last handed the parent, so a value arriving from anywhere else can be told apart
  // from our own echo.
  const emitted = useRef<string | null>(value)

  useEffect(() => {
    // A value we did not produce means the form loaded a different member. Their number brings
    // its own dial code, and holding on to the previous member's would be a lie.
    if (value !== emitted.current) {
      emitted.current = value
      setPendingDial('')
    }
  }, [value])

  const emit = (next: string | null) => {
    emitted.current = next
    onChange(next)
  }

  const dialCode = stored.dialCode !== '' ? stored.dialCode : pendingDial
  const local = stored.local

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
      {/*
        A phone number reads left to right everywhere, dialing code first, whatever language
        surrounds it — "+20 1018124080", never the mirror image. Left to inherit the page's
        RTL, the row puts the code on the right of the digits and drops the caret on the wrong
        end of the number. The label above stays with the interface direction; only the number
        itself is pinned.
      */}
      <div dir="ltr" style={{ display: 'flex', gap: 8 }}>
        <SearchableSelect
          id={`${id}-dial`}
          ariaLabel={t('members.dialCode')}
          value={dialCode}
          options={dialOptions}
          emptyLabel="—"
          placeholder={t('members.searchPlaceholder')}
          noResultsLabel={t('members.noMatches')}
          dir="ltr"
          disabled={disabled}
          onChange={(code) => {
            setPendingDial(code)
            emit(joinPhone(code, local))
          }}
          controlStyle={{ ...controlStyle, width: 'auto', flex: '0 0 auto', minWidth: 170 }}
        />
        <input
          id={`${id}-local`}
          aria-label={t('members.localNumber')}
          value={local}
          dir="ltr"
          disabled={disabled}
          inputMode="tel"
          autoComplete="tel-national"
          // E.164 allows 15 digits, and what arrives here may also carry a '+' and the spaces
          // and dashes a pasted number is written with. Capped tight enough to keep the field
          // from becoming a text box, loose enough that a pasted number is never silently
          // clipped into a different number.
          maxLength={24}
          onChange={(event) => {
            const next = joinPhone(dialCode, event.target.value)
            // Clearing the digits empties the whole value — joinPhone cannot compose a number
            // out of a code alone — so the code has to move somewhere the value cannot reach.
            // Without this it is simply lost, the picker snaps back to "—", and every further
            // keystroke composes against an empty code and yields null again: the field stops
            // accepting input until the user re-picks a code they never changed.
            if (next === null && dialCode !== '') setPendingDial(dialCode)
            emit(next)
          }}
          style={{ ...controlStyle, flex: 1 }}
        />
      </div>
    </div>
  )
}
