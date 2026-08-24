import type { CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import { countryName, flagEmoji } from '../countries/flagEmoji'
import type { Country } from '../countries/types'
import { isValidNationalId, type ContactDetails } from './contactDetails'
import { PhoneInput } from './PhoneInput'

interface ContactFieldsProps {
  idPrefix: string
  value: ContactDetails
  countries: readonly Country[]
  onChange: (next: ContactDetails) => void
  labelStyle: CSSProperties
  controlStyle: CSSProperties
}

/**
 * The contact half of the member form. Shaped like LifeDetailsFields — a controlled group that
 * owns no state of its own, so the form holds one value and there is one place a save reads
 * from.
 */
export function ContactFields({
  idPrefix,
  value,
  countries,
  onChange,
  labelStyle,
  controlStyle,
}: ContactFieldsProps) {
  const { t, i18n } = useTranslation()

  const nationalId = value.nationalId ?? ''
  // Only complain about what the user has actually typed. An empty field is "not recorded",
  // not an error, and flagging it on an untouched form is noise.
  const nationalIdInvalid = nationalId !== '' && !isValidNationalId(nationalId)

  const sameAsMobile =
    value.mobileNumber !== null && value.whatsAppNumber === value.mobileNumber

  const sorted = [...countries].sort((a, b) =>
    countryName(a, i18n.language).localeCompare(countryName(b, i18n.language), i18n.language),
  )

  return (
    <fieldset style={{ border: 'none', padding: 0, margin: '0 0 var(--space-4)' }}>
      <legend style={{ ...labelStyle, marginBottom: 'var(--space-3)' }}>
        {t('members.contactSection')}
      </legend>

      <div style={{ marginBottom: 'var(--space-4)' }}>
        <label htmlFor={`${idPrefix}-national-id`} style={labelStyle}>
          {t('members.nationalId')}
        </label>
        <input
          id={`${idPrefix}-national-id`}
          value={nationalId}
          inputMode="numeric"
          maxLength={9}
          aria-invalid={nationalIdInvalid}
          aria-describedby={`${idPrefix}-national-id-hint`}
          onChange={(event) =>
            onChange({
              ...value,
              nationalId: event.target.value === '' ? null : event.target.value,
            })
          }
          style={{
            ...controlStyle,
            borderColor: nationalIdInvalid ? 'var(--error)' : 'var(--border-strong)',
          }}
        />
        <p
          id={`${idPrefix}-national-id-hint`}
          style={{
            margin: '6px 0 0',
            fontSize: 12,
            color: nationalIdInvalid ? 'var(--error)' : 'var(--text-3)',
          }}
        >
          {nationalIdInvalid ? t('members.nationalIdInvalid') : t('members.nationalIdHint')}
        </p>
      </div>

      <div style={{ marginBottom: 'var(--space-4)' }}>
        <label htmlFor={`${idPrefix}-country`} style={labelStyle}>
          {t('members.country')}
        </label>
        <select
          id={`${idPrefix}-country`}
          value={value.countryId ?? ''}
          onChange={(event) =>
            onChange({
              ...value,
              countryId: event.target.value === '' ? null : Number(event.target.value),
            })
          }
          style={controlStyle}
        >
          <option value="">{t('members.noCountry')}</option>
          {sorted.map((country) => (
            <option key={country.id} value={country.id}>
              {flagEmoji(country.code)} {countryName(country, i18n.language)}
            </option>
          ))}
        </select>
      </div>

      <PhoneInput
        id={`${idPrefix}-mobile`}
        label={t('members.mobileNumber')}
        value={value.mobileNumber}
        countries={countries}
        onChange={(mobileNumber) =>
          onChange({
            ...value,
            mobileNumber,
            // Keep a mirrored WhatsApp number in step: the checkbox promised they are the same,
            // and letting it fall behind would save a number the user never typed.
            whatsAppNumber: sameAsMobile ? mobileNumber : value.whatsAppNumber,
          })
        }
        labelStyle={labelStyle}
        controlStyle={controlStyle}
      />

      <div style={{ marginBottom: 'var(--space-3)' }}>
        <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13 }}>
          <input
            type="checkbox"
            checked={sameAsMobile}
            onChange={(event) =>
              onChange({
                ...value,
                whatsAppNumber: event.target.checked ? value.mobileNumber : null,
              })
            }
          />
          {t('members.sameAsMobile')}
        </label>
      </div>

      <PhoneInput
        id={`${idPrefix}-whatsapp`}
        label={t('members.whatsAppNumber')}
        value={value.whatsAppNumber}
        countries={countries}
        disabled={sameAsMobile}
        onChange={(whatsAppNumber) => onChange({ ...value, whatsAppNumber })}
        labelStyle={labelStyle}
        controlStyle={controlStyle}
      />
    </fieldset>
  )
}
