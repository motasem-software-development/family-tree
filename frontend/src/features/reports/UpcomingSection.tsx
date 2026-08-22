import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { fullName, type NamedNode } from '../members/fullName'
import type { MemberRef, UpcomingReport } from './types'

interface Props {
  report: UpcomingReport
  byId: Map<string, NamedNode>
}

export const UpcomingSection = ({ report, byId }: Props) => {
  const { t, i18n } = useTranslation()
  const number = (value: number) => new Intl.NumberFormat(i18n.language).format(value)
  const display = (member: MemberRef) => fullName(member, byId)

  const when = (daysAway: number) =>
    daysAway === 0
      ? t('reports.upcoming.today')
      : t('reports.upcoming.inDays', { days: number(daysAway) })

  const empty = report.birthdays.length === 0 && report.anniversaries.length === 0

  return (
    <section aria-labelledby="upcoming-heading">
      <h2 id="upcoming-heading">
        {t('reports.upcoming.title', { days: number(report.windowDays) })}
      </h2>

      {empty && (
        <p data-testid="upcoming-empty">
          {t('reports.upcoming.empty', { days: number(report.windowDays) })}
        </p>
      )}

      {report.birthdays.length > 0 && (
        <>
          <h3>{t('reports.upcoming.birthdays')}</h3>
          <ul style={{ listStyle: 'none', padding: 0 }}>
            {report.birthdays.map((birthday) => (
              <li key={birthday.member.id} data-testid="birthday-row">
                <Link to={`/?memberId=${birthday.member.id}`}>{display(birthday.member)}</Link>{' '}
                {/* The age reached on the occurrence, not today's — the server computed it. */}
                <span>
                  {t('reports.upcoming.turningAge', { age: number(birthday.turningAge) })}
                </span>{' '}
                <span>{when(birthday.daysAway)}</span>
              </li>
            ))}
          </ul>
        </>
      )}

      {report.anniversaries.length > 0 && (
        <>
          <h3>{t('reports.upcoming.anniversaries')}</h3>
          <ul style={{ listStyle: 'none', padding: 0 }}>
            {report.anniversaries.map((anniversary) => (
              <li key={anniversary.member.id} data-testid="anniversary-row">
                <Link to={`/?memberId=${anniversary.member.id}`}>
                  {display(anniversary.member)}
                </Link>{' '}
                <span>
                  {t('reports.upcoming.yearsSince', { years: number(anniversary.years) })}
                </span>{' '}
                <span>{when(anniversary.daysAway)}</span>
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  )
}
