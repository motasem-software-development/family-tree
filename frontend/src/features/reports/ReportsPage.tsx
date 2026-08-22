import { useTranslation } from 'react-i18next'
import { AppShell } from '../../app/AppShell'
import { useAuth } from '../auth/AuthContext'
import { LifeStatusSection } from './LifeStatusSection'
import { StructureSection } from './StructureSection'
import { useReportsQuery } from './useReports'

export const ReportsPage = () => {
  const { t, i18n } = useTranslation()
  const { user } = useAuth()
  const familyName = user?.familyTreeName ?? ''
  const { data, isPending, isError } = useReportsQuery()

  return (
    <AppShell
      familyName={familyName}
      statLine={t('tree.membersCount', { count: data?.structure.totalMembers ?? 0 })}
    >
      <h1>{t('reports.title')}</h1>

      {isError && <p role="alert">{t('reports.loadFailed')}</p>}

      {isPending && !isError && <p>{t('reports.loading')}</p>}

      {data !== undefined && (
        <>
          {/* The server's reference day, shown rather than re-derived: a client in another
              time zone must not disagree with the figures it is labelling (design §5). */}
          <p style={{ fontSize: 11, color: 'var(--text-3)' }}>
            {t('reports.generatedOn', {
              date: new Intl.DateTimeFormat(i18n.language).format(new Date(data.generatedOn)),
            })}
          </p>

          <div style={{ display: 'flex', flexDirection: 'column', gap: 32 }}>
            <StructureSection report={data.structure} />
            <LifeStatusSection report={data.lifeStatus} />
          </div>
        </>
      )}
    </AppShell>
  )
}
