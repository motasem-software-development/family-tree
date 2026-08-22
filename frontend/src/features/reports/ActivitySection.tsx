import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { fullName, type NamedNode } from '../members/fullName'
import type { ActivityEntry, ActivityReport } from './types'

interface Props {
  report: ActivityReport
  byId: Map<string, NamedNode>
}

export const ActivitySection = ({ report, byId }: Props) => {
  const { t, i18n } = useTranslation()
  const number = (value: number) => new Intl.NumberFormat(i18n.language).format(value)
  // `at` is a full ISO timestamp (DateTimeOffset) — an instant, not a calendar date — so it is
  // correctly shown in the viewer's own time zone. Unlike `generatedOn` (a bare YYYY-MM-DD
  // DateOnly, pinned to UTC elsewhere on this page), pinning this one would be wrong: it would
  // show everyone the moment as it looked in UTC instead of as it looked to them.
  const when = (iso: string) => new Intl.DateTimeFormat(i18n.language).format(new Date(iso))

  const rows = (entries: ActivityEntry[], testId: string) => (
    <ul style={{ listStyle: 'none', padding: 0 }}>
      {entries.map((entry) => (
        <li key={entry.member.id} data-testid={testId}>
          <Link to={`/?memberId=${entry.member.id}`}>{fullName(entry.member, byId)}</Link>{' '}
          <span>{when(entry.at)}</span>
        </li>
      ))}
    </ul>
  )

  const empty = report.added.length === 0 && report.edited.length === 0

  return (
    <section aria-labelledby="activity-heading">
      <h2 id="activity-heading">
        {t('reports.activity.title', { days: number(report.windowDays) })}
      </h2>

      {empty ? (
        <p>{t('reports.activity.empty', { days: number(report.windowDays) })}</p>
      ) : (
        <>
          {report.added.length > 0 && (
            <>
              <h3>
                {t('reports.activity.added')} {number(report.addedCount)}
              </h3>
              {rows(report.added, 'activity-added-row')}
            </>
          )}

          {report.edited.length > 0 && (
            <>
              <h3>
                {t('reports.activity.edited')} {number(report.editedCount)}
              </h3>
              {rows(report.edited, 'activity-edited-row')}
            </>
          )}
        </>
      )}

      {/* Stated plainly rather than left to be discovered: this reads record timestamps, so a
          deleted member leaves no trace here. The real fix is AuditLog (design §9). */}
      <p style={{ fontSize: 11, color: 'var(--text-3)' }}>{t('reports.activity.note')}</p>
    </section>
  )
}
