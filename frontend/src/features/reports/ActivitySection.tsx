import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { fullName, type NamedNode } from '../members/fullName'
import { Badge, Card, CardHead, Empty, GroupHead, Note, Row, RowList, Stack } from './reportUi'
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
    <RowList>
      {entries.map((entry) => (
        <Row key={entry.member.id} testId={testId} trailing={when(entry.at)}>
          <Link to={`/?memberId=${entry.member.id}`}>{fullName(entry.member, byId)}</Link>
        </Row>
      ))}
    </RowList>
  )

  const empty = report.added.length === 0 && report.edited.length === 0

  return (
    <Card labelledBy="activity-heading">
      <CardHead
        id="activity-heading"
        title={t('reports.activity.title', { days: number(report.windowDays) })}
      />

      <Stack gap="var(--space-5)">
        {empty ? (
          <Empty>{t('reports.activity.empty', { days: number(report.windowDays) })}</Empty>
        ) : (
          <>
            {report.added.length > 0 && (
              <div>
                <GroupHead
                  title={t('reports.activity.added')}
                  trailing={<Badge>{number(report.addedCount)}</Badge>}
                />
                {rows(report.added, 'activity-added-row')}
              </div>
            )}

            {report.edited.length > 0 && (
              <div>
                <GroupHead
                  title={t('reports.activity.edited')}
                  trailing={<Badge>{number(report.editedCount)}</Badge>}
                />
                {rows(report.edited, 'activity-edited-row')}
              </div>
            )}
          </>
        )}

        {/* Stated plainly rather than left to be discovered: this reads record timestamps, so a
            deleted member leaves no trace here. The real fix is AuditLog (design §9). */}
        <Note>{t('reports.activity.note')}</Note>
      </Stack>
    </Card>
  )
}
