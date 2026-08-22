import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { fullName, type NamedNode } from '../members/fullName'
import type { CompletenessReport, MemberRef } from './types'

interface Props {
  report: CompletenessReport
  /** The member index the lineage is composed from — see design §7. */
  byId: Map<string, NamedNode>
}

export const CompletenessSection = ({ report, byId }: Props) => {
  const { t, i18n } = useTranslation()
  const number = (value: number) => new Intl.NumberFormat(i18n.language).format(value)

  // The lineage, not the bare given name: `فارس` alone identifies nobody in this model.
  const display = (member: MemberRef) => fullName(member, byId)

  const outstanding = report.issues.filter((issue) => issue.count > 0)

  return (
    <section aria-labelledby="completeness-heading">
      <h2 id="completeness-heading">{t('reports.completeness.title')}</h2>

      <p>
        {t('reports.completeness.complete', {
          complete: number(report.completeRecords),
          total: number(report.totalMembers),
        })}
      </p>

      {outstanding.length === 0 && <p>{t('reports.completeness.nothingToFix')}</p>}

      {outstanding.map((issue) => (
        <div key={issue.code}>
          <h3>
            {t(`reports.completeness.${issue.code}`)}{' '}
            {/* The true count, never members.length: the list is capped at 50 and a client
                that reported the row count would understate the work outstanding. */}
            <span data-testid={`issue-count-${issue.code}`}>{number(issue.count)}</span>
          </h3>

          {issue.count > issue.members.length && (
            <p style={{ fontSize: 11, color: 'var(--text-3)' }}>
              {t('reports.completeness.showingSome', { shown: number(issue.members.length) })}
            </p>
          )}

          <ul style={{ listStyle: 'none', padding: 0 }}>
            {issue.members.map((member) => (
              <li key={member.id}>
                {/* Links into the tree so the worklist is actionable — TreePage reads
                    ?memberId= and preselects (design §8). */}
                <Link to={`/?memberId=${member.id}`}>{display(member)}</Link>
              </li>
            ))}
          </ul>
        </div>
      ))}
    </section>
  )
}
