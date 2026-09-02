import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { fullName, type NamedNode } from '../members/fullName'
import { Badge, Card, CardHead, Empty, GroupHead, Note, Row, RowList, Stack } from './reportUi'
import type { MemberRef, UpcomingReport } from './types'

interface Props {
  report: UpcomingReport
  byId: Map<string, NamedNode>
}

export const UpcomingSection = ({ report, byId }: Props) => {
  const { t, i18n } = useTranslation()
  const number = (value: number) => new Intl.NumberFormat(i18n.language).format(value)
  const display = (member: MemberRef) => fullName(member, byId)

  // count drives i18next pluralization directly; the other COUNTED keys in this codebase
  // (tree.resultCount, tree.membersCount, tree.generationsCount) interpolate the raw number
  // the same way rather than routing it through Intl.NumberFormat, so this follows suit for
  // consistency rather than introducing a custom i18next number formatter for these three keys.
  const when = (daysAway: number) =>
    daysAway === 0 ? t('reports.upcoming.today') : t('reports.upcoming.inDays', { count: daysAway })

  const empty = report.birthdays.length === 0 && report.anniversaries.length === 0

  return (
    <Card labelledBy="upcoming-heading">
      <CardHead id="upcoming-heading" title={t('reports.upcoming.title', { days: number(report.windowDays) })} />

      <Stack gap="var(--space-5)">
        {empty && (
          <Empty testId="upcoming-empty">
            {t('reports.upcoming.empty', { days: number(report.windowDays) })}
          </Empty>
        )}

        {report.birthdays.length > 0 && (
          <div>
            <GroupHead
              title={t('reports.upcoming.birthdays')}
              trailing={
                /* The true count, never birthdays.length: the list is capped at 50 and a client
                   that reported the row count would understate what's coming up. */
                <Badge testId="birthday-count">{number(report.birthdayCount)}</Badge>
              }
            />

            <RowList>
              {report.birthdays.map((birthday) => (
                <Row key={birthday.member.id} testId="birthday-row" trailing={when(birthday.daysAway)}>
                  <Link to={`/?memberId=${birthday.member.id}`}>{display(birthday.member)}</Link>{' '}
                  {/* The age reached on the occurrence, not today's — the server computed it. */}
                  <span style={{ color: 'var(--text-3)' }}>
                    {t('reports.upcoming.turningAge', { count: birthday.turningAge })}
                  </span>
                </Row>
              ))}
            </RowList>

            {report.birthdayCount > report.birthdays.length && (
              <div style={{ marginTop: 'var(--space-2)' }}>
                <Note>
                  {t('reports.completeness.showingSome', { shown: number(report.birthdays.length) })}
                </Note>
              </div>
            )}
          </div>
        )}

        {report.anniversaries.length > 0 && (
          <div>
            <GroupHead
              title={t('reports.upcoming.anniversaries')}
              trailing={
                /* The true count, never anniversaries.length: same cap, same rule. */
                <Badge testId="anniversary-count">{number(report.anniversaryCount)}</Badge>
              }
            />

            <RowList>
              {report.anniversaries.map((anniversary) => (
                <Row
                  key={anniversary.member.id}
                  testId="anniversary-row"
                  trailing={when(anniversary.daysAway)}
                >
                  <Link to={`/?memberId=${anniversary.member.id}`}>
                    {display(anniversary.member)}
                  </Link>{' '}
                  <span style={{ color: 'var(--text-3)' }}>
                    {t('reports.upcoming.yearsSince', { count: anniversary.years })}
                  </span>
                </Row>
              ))}
            </RowList>

            {report.anniversaryCount > report.anniversaries.length && (
              <div style={{ marginTop: 'var(--space-2)' }}>
                <Note>
                  {t('reports.completeness.showingSome', {
                    shown: number(report.anniversaries.length),
                  })}
                </Note>
              </div>
            )}
          </div>
        )}
      </Stack>
    </Card>
  )
}
