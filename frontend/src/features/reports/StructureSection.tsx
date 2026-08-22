import { useTranslation } from 'react-i18next'
import type { StructureReport } from './types'

/** A labelled figure. The building block of every count-only section. */
const Stat = ({ label, value, testId }: { label: string; value: string; testId?: string }) => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
    <span style={{ fontSize: 11, color: 'var(--text-3)' }}>{label}</span>
    <strong data-testid={testId} style={{ fontSize: 22 }}>
      {value}
    </strong>
  </div>
)

export const StructureSection = ({ report }: { report: StructureReport }) => {
  const { t, i18n } = useTranslation()
  // Arabic-Indic digits where the locale calls for them, rather than toString().
  const number = (value: number) => new Intl.NumberFormat(i18n.language).format(value)

  return (
    <section aria-labelledby="structure-heading">
      <h2 id="structure-heading">{t('reports.structure.title')}</h2>

      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 24 }}>
        <Stat
          label={t('reports.structure.totalMembers')}
          value={number(report.totalMembers)}
          testId="structure-total"
        />
        <Stat
          label={t('reports.structure.depth')}
          value={number(report.depth)}
          testId="structure-depth"
        />
        <Stat
          label={t('reports.structure.membersWithChildren')}
          value={number(report.membersWithChildren)}
        />
        <Stat label={t('reports.structure.leafMembers')} value={number(report.leafMembers)} />
        <Stat
          label={t('reports.structure.averageChildren')}
          value={number(report.averageChildrenPerParent)}
        />
      </div>

      <ul style={{ listStyle: 'none', padding: 0, marginBlockStart: 16 }}>
        {report.generations.map((generation) => (
          <li
            key={generation.generation}
            data-testid="generation-row"
            style={{ display: 'flex', justifyContent: 'space-between', gap: 12 }}
          >
            <span>{t('reports.structure.generation', { number: generation.generation })}</span>
            <span>{number(generation.count)}</span>
          </li>
        ))}
      </ul>

      <h3>{t('reports.structure.branches')}</h3>
      <ul style={{ listStyle: 'none', padding: 0 }}>
        {report.branches.map((branch) => (
          <li
            key={branch.id}
            data-testid="branch-row"
            style={{ display: 'flex', justifyContent: 'space-between', gap: 12 }}
          >
            <span>{branch.name}</span>
            <span>
              {t('reports.structure.descendants')} {number(branch.descendantCount)}
            </span>
          </li>
        ))}
      </ul>
    </section>
  )
}
