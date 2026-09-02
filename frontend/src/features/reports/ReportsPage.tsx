import { useTranslation } from 'react-i18next'
import { AppShell } from '../../app/AppShell'
import { useAuth } from '../auth/AuthContext'
import { indexById } from '../members/fullName'
import { useMembersQuery } from '../members/useMembers'
import { ActivitySection } from './ActivitySection'
import { CompletenessSection } from './CompletenessSection'
import { LifeStatusSection } from './LifeStatusSection'
import { StructureSection } from './StructureSection'
import { UpcomingSection } from './UpcomingSection'
import { Card, Note, ZoneLabel } from './reportUi'
import { useReportsQuery } from './useReports'

/**
 * The page reads in two zones, and the split is the layout's whole argument: what the family
 * *is* (structure, life status — settled measurements) sits above what it *needs* (incomplete
 * records, dates coming up, recent edits — a worklist). Five equally-weighted headings in one
 * column, which is what this was, said nothing about which of the two a reader was looking at.
 */
export const ReportsPage = () => {
  const { t, i18n } = useTranslation()
  const { user } = useAuth()
  const familyName = user?.familyTreeName ?? ''
  const { data, isPending, isError } = useReportsQuery()

  // Report rows carry (id, name, parentId); the lineage is composed here with the helper the
  // members screen already uses, so the naming rule lives in one place (design §7).
  const { data: members } = useMembersQuery()
  const byId = indexById(members ?? [])

  return (
    <AppShell
      familyName={familyName}
      statLine={t('tree.membersCount', { count: data?.structure.totalMembers ?? 0 })}
    >
      {/* AppShell lays its children out as a flex row, so the headline, the timestamp and the
          sections were becoming three columns side by side with nothing to scroll them. Wrapped
          in the same scroll container the members, users and roles screens use — this is a
          layout fix at every width, not only below the breakpoint. */}
      <div
        style={{
          flex: 1,
          minWidth: 0,
          overflow: 'auto',
          padding: 'clamp(var(--space-4), 4vw, var(--space-8))',
        }}
      >
        <div
          style={{
            maxWidth: 680,
            margin: '0 auto',
            display: 'flex',
            flexDirection: 'column',
            gap: 'var(--space-6)',
          }}
        >
          <header
            style={{
              display: 'flex',
              alignItems: 'baseline',
              flexWrap: 'wrap',
              gap: 'var(--space-3)',
            }}
          >
            <h1 style={{ margin: 0, fontSize: 26, fontWeight: 700, letterSpacing: '-0.01em' }}>
              {t('reports.title')}
            </h1>
            {data !== undefined && (
              <div style={{ marginInlineStart: 'auto' }}>
                {/* The server's reference day, shown rather than re-derived: a client in another
                    time zone must not disagree with the figures it is labelling (design §5).
                    Pinned to UTC: `generatedOn` is a YYYY-MM-DD string, which `Date` parses as
                    UTC midnight, so the formatter must stay in UTC too — otherwise a viewer west
                    of UTC sees the day before the one the server measured. Do not "fix" this
                    back to the viewer's local zone. */}
                <Note>
                  {t('reports.generatedOn', {
                    date: new Intl.DateTimeFormat(i18n.language, { timeZone: 'UTC' }).format(
                      new Date(data.generatedOn),
                    ),
                  })}
                </Note>
              </div>
            )}
          </header>

          {isError && (
            <Card>
              <p role="alert" style={{ margin: 0, fontSize: 13, color: 'var(--error)' }}>
                {t('reports.loadFailed')}
              </p>
            </Card>
          )}

          {isPending && !isError && (
            <Card>
              <p style={{ margin: 0, fontSize: 13, color: 'var(--text-3)' }}>
                {t('reports.loading')}
              </p>
            </Card>
          )}

          {data !== undefined && (
            <>
              <section
                aria-labelledby="zone-family"
                style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-4)' }}
              >
                <ZoneLabel>
                  <span id="zone-family">{t('reports.zones.family')}</span>
                </ZoneLabel>
                <StructureSection report={data.structure} />
                <LifeStatusSection report={data.lifeStatus} />
              </section>

              <section
                aria-labelledby="zone-attention"
                style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-4)' }}
              >
                <ZoneLabel>
                  <span id="zone-attention">{t('reports.zones.attention')}</span>
                </ZoneLabel>
                <CompletenessSection report={data.completeness} byId={byId} />
                <UpcomingSection report={data.upcoming} byId={byId} />
                <ActivitySection report={data.activity} byId={byId} />
              </section>
            </>
          )}
        </div>
      </div>
    </AppShell>
  )
}
