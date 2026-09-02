import { useTranslation } from 'react-i18next'
import {
  Card,
  CardHead,
  Figure,
  FigureRow,
  GroupHead,
  Ladder,
  Note,
  SplitBar,
  Stack,
  type LadderRow,
} from './reportUi'
import type { LifeStatusReport } from './types'

/**
 * Living and deceased is a two-part whole, so it gets a split strip rather than a chart with a
 * legend — and never colour alone: each figure carries its own dot and its own word. Green for
 * living is the app's success token; deceased is muted ink, not the error red, because dying is
 * not a fault in the record.
 */
export const LifeStatusSection = ({ report }: { report: LifeStatusReport }) => {
  const { t, i18n } = useTranslation()
  const number = (value: number) => new Intl.NumberFormat(i18n.language).format(value)

  const peakBracket = Math.max(1, ...report.livingAges.map((b) => b.count))
  const ageRows: LadderRow[] = report.livingAges.map((bracket) => ({
    key: bracket.bracket,
    label: bracket.bracket,
    value: number(bracket.count),
    ratio: bracket.count / peakBracket,
  }))

  return (
    <Card labelledBy="life-status-heading">
      <CardHead id="life-status-heading" title={t('reports.lifeStatus.title')} />

      <Stack gap="var(--space-5)">
        <div>
          <SplitBar
            left={report.living}
            right={report.deceased}
            label={`${t('reports.lifeStatus.living')} ${number(report.living)} · ${t('reports.lifeStatus.deceased')} ${number(report.deceased)}`}
          />
          <FigureRow>
            <Figure
              tone="living"
              label={t('reports.lifeStatus.living')}
              value={number(report.living)}
              testId="living-count"
            />
            <Figure
              tone="deceased"
              label={t('reports.lifeStatus.deceased')}
              value={number(report.deceased)}
              testId="deceased-count"
            />
          </FigureRow>
        </div>

        <div>
          <GroupHead title={t('reports.lifeStatus.ages')} />
          <Ladder rows={ageRows} />
          {/* Disclosed rather than folded into a bracket: the histogram must not imply a
              population it did not measure (design §5). */}
          <div style={{ marginTop: 'var(--space-3)' }}>
            <Note>
              {t('reports.lifeStatus.unknownAge')}{' '}
              <strong data-testid="living-without-birth-date" style={{ color: 'var(--text-2)' }}>
                {number(report.livingWithoutBirthDate)}
              </strong>
            </Note>
          </div>
        </div>

        <div>
          <GroupHead title={t('reports.lifeStatus.longevity')} />
          {report.longevity === null ? (
            // "Not measurable", never zeros — zeros would read as a measured result.
            <p
              data-testid="longevity-unavailable"
              style={{ margin: 0, fontSize: 13, color: 'var(--text-3)' }}
            >
              {t('reports.lifeStatus.longevityUnavailable')}
            </p>
          ) : (
            // Three figures rather than one "{min} to {max}" sentence: a range written inline
            // is a bidirectional hazard — the dash between two numbers takes the paragraph's
            // direction, so 40–90 can render as 90–40 in Arabic. Separate numbers cannot.
            <FigureRow>
              <Figure
                label={t('reports.lifeStatus.longevityShortest')}
                value={number(report.longevity.minYears)}
              />
              <Figure
                label={t('reports.lifeStatus.longevityMedian')}
                value={number(report.longevity.medianYears)}
              />
              <Figure
                label={t('reports.lifeStatus.longevityLongest')}
                value={number(report.longevity.maxYears)}
              />
            </FigureRow>
          )}
        </div>
      </Stack>
    </Card>
  )
}
