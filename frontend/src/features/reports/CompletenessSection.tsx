import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { fullName, type NamedNode } from '../members/fullName'
import { Badge, Card, CardHead, Empty, GroupHead, Meter, Note, Row, RowList, Stack } from './reportUi'
import type { CompletenessReport, MemberRef } from './types'

interface Props {
  report: CompletenessReport
  /** The member index the lineage is composed from — see design §7. */
  byId: Map<string, NamedNode>
}

/**
 * The first of the three worklists. Everything here is a thing to go and do, so the counts wear
 * the attention tone and every name is a link — the section is a queue, not a statistic.
 */
export const CompletenessSection = ({ report, byId }: Props) => {
  const { t, i18n } = useTranslation()
  const number = (value: number) => new Intl.NumberFormat(i18n.language).format(value)

  // The lineage, not the bare given name: `فارس` alone identifies nobody in this model.
  const display = (member: MemberRef) => fullName(member, byId)

  const outstanding = report.issues.filter((issue) => issue.count > 0)
  const completeRatio =
    report.totalMembers === 0 ? 1 : report.completeRecords / report.totalMembers
  const summary = t('reports.completeness.complete', {
    complete: number(report.completeRecords),
    total: number(report.totalMembers),
  })

  return (
    <Card labelledBy="completeness-heading">
      <CardHead
        id="completeness-heading"
        title={t('reports.completeness.title')}
        caption={summary}
      />

      <Stack gap="var(--space-5)">
        <Meter ratio={completeRatio} label={summary} />

        {outstanding.length === 0 ? (
          <Empty>{t('reports.completeness.nothingToFix')}</Empty>
        ) : (
          outstanding.map((issue) => (
            <div key={issue.code}>
              <GroupHead
                title={t(`reports.completeness.${issue.code}`)}
                trailing={
                  /* The true count, never members.length: the list is capped at 50 and a client
                     that reported the row count would understate the work outstanding. */
                  <Badge tone="attention" testId={`issue-count-${issue.code}`}>
                    {number(issue.count)}
                  </Badge>
                }
              />

              <RowList columns>
                {issue.members.map((member) => (
                  <Row key={member.id} divider={false}>
                    {/* Links into the tree so the worklist is actionable — TreePage reads
                        ?memberId= and preselects (design §8). */}
                    <Link to={`/?memberId=${member.id}`}>{display(member)}</Link>
                  </Row>
                ))}
              </RowList>

              {issue.count > issue.members.length && (
                <div style={{ marginTop: 'var(--space-2)' }}>
                  <Note>
                    {t('reports.completeness.showingSome', { shown: number(issue.members.length) })}
                  </Note>
                </div>
              )}
            </div>
          ))
        )}
      </Stack>
    </Card>
  )
}
