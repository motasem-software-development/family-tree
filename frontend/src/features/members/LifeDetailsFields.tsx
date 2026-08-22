import type { CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import {
  dateInputValue,
  fromDateInput,
  withDeathDate,
  withDeceased,
  type LifeDetails,
} from './lifeDetails'

interface LifeDetailsFieldsProps {
  /** Prefixes every id, so two of these can coexist on one page without colliding. */
  idPrefix: string
  value: LifeDetails
  onChange: (next: LifeDetails) => void
  labelStyle: CSSProperties
  controlStyle: CSSProperties
}

/**
 * The birth date / death date / deceased trio, shared by the members-list form and the tree's
 * add-and-edit dialog so the two surfaces cannot drift apart on validation or wording.
 *
 * `max` is today: the API rejects a future date with MEMBER_DATE_IN_FUTURE, and letting the
 * picker offer one only to have the save fail is a worse way to learn the same rule.
 */
export const LifeDetailsFields = ({
  idPrefix,
  value,
  onChange,
  labelStyle,
  controlStyle,
}: LifeDetailsFieldsProps) => {
  const { t } = useTranslation()
  const today = new Date().toISOString().slice(0, 10)

  return (
    <>
      <div style={{ display: 'flex', gap: 12, marginBottom: 'var(--space-4)' }}>
        <div style={{ flex: 1, minWidth: 0 }}>
          <label htmlFor={`${idPrefix}-dob`} style={labelStyle}>
            {t('members.dateOfBirth')}
          </label>
          <input
            id={`${idPrefix}-dob`}
            type="date"
            max={today}
            value={dateInputValue(value.dateOfBirth)}
            onChange={(event) =>
              onChange({ ...value, dateOfBirth: fromDateInput(event.target.value) })
            }
            style={controlStyle}
          />
        </div>
        <div style={{ flex: 1, minWidth: 0 }}>
          <label htmlFor={`${idPrefix}-dod`} style={labelStyle}>
            {t('members.dateOfDeath')}
          </label>
          <input
            id={`${idPrefix}-dod`}
            type="date"
            max={today}
            min={dateInputValue(value.dateOfBirth)}
            value={dateInputValue(value.dateOfDeath)}
            onChange={(event) =>
              onChange(withDeathDate(value, fromDateInput(event.target.value)))
            }
            style={controlStyle}
          />
        </div>
      </div>

      <div style={{ marginBottom: 'var(--space-4)' }}>
        <label
          htmlFor={`${idPrefix}-deceased`}
          style={{ ...labelStyle, display: 'flex', alignItems: 'center', gap: 8, marginBottom: 0 }}
        >
          <input
            id={`${idPrefix}-deceased`}
            type="checkbox"
            checked={value.isDeceased}
            onChange={(event) => onChange(withDeceased(value, event.target.checked))}
            style={{ width: 16, height: 16, accentColor: 'var(--primary)' }}
          />
          {t('members.markDeceased')}
        </label>
        {/* The flag exists precisely so "died, date unknown" is recordable — say so, or the
            checkbox looks redundant next to the date field. */}
        <p style={{ margin: '6px 0 0', fontSize: 12, color: 'var(--text-3)' }}>
          {t('members.markDeceasedHint')}
        </p>
      </div>
    </>
  )
}
