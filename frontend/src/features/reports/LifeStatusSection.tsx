import { useTranslation } from 'react-i18next'
import type { LifeStatusReport } from './types'

export const LifeStatusSection = ({ report }: { report: LifeStatusReport }) => {
  const { t, i18n } = useTranslation()
  const number = (value: number) => new Intl.NumberFormat(i18n.language).format(value)

  return (
    <section aria-labelledby="life-status-heading">
      <h2 id="life-status-heading">{t('reports.lifeStatus.title')}</h2>

      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 24 }}>
        <div>
          <span style={{ fontSize: 11, color: 'var(--text-3)' }}>
            {t('reports.lifeStatus.living')}
          </span>
          <strong data-testid="living-count" style={{ display: 'block', fontSize: 22 }}>
            {number(report.living)}
          </strong>
        </div>
        <div>
          <span style={{ fontSize: 11, color: 'var(--text-3)' }}>
            {t('reports.lifeStatus.deceased')}
          </span>
          <strong data-testid="deceased-count" style={{ display: 'block', fontSize: 22 }}>
            {number(report.deceased)}
          </strong>
        </div>
      </div>

      <h3>{t('reports.lifeStatus.ages')}</h3>
      <ul style={{ listStyle: 'none', padding: 0 }}>
        {report.livingAges.map((bracket) => (
          <li
            key={bracket.bracket}
            style={{ display: 'flex', justifyContent: 'space-between', gap: 12 }}
          >
            <span>{bracket.bracket}</span>
            <span>{number(bracket.count)}</span>
          </li>
        ))}
      </ul>

      {/* Disclosed rather than folded into a bracket: the histogram must not imply a
          population it did not measure (design §5). */}
      <p>
        {t('reports.lifeStatus.unknownAge')}{' '}
        <strong data-testid="living-without-birth-date">
          {number(report.livingWithoutBirthDate)}
        </strong>
      </p>

      <h3>{t('reports.lifeStatus.longevity')}</h3>
      {report.longevity === null ? (
        // "Not measurable", never zeros — zeros would read as a measured result.
        <p data-testid="longevity-unavailable">{t('reports.lifeStatus.longevityUnavailable')}</p>
      ) : (
        <p>
          {t('reports.lifeStatus.longevityRange', {
            min: number(report.longevity.minYears),
            max: number(report.longevity.maxYears),
          })}
          {' · '}
          {t('reports.lifeStatus.longevityMedian', {
            years: number(report.longevity.medianYears),
          })}
        </p>
      )}
    </section>
  )
}
