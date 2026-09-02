import { useTranslation } from 'react-i18next'
import { Card, CardHead, Figure, FigureRow, Ladder, Stack, type LadderRow } from './reportUi'
import type { StructureReport } from './types'

/**
 * The page's opening statement. The generation ladder goes first and the totals annotate it,
 * rather than the other way round: the shape of the family is the thing a reader came for, and
 * a row of boxed figures above it would bury the one figure that is specific to this product.
 */
export const StructureSection = ({ report }: { report: StructureReport }) => {
  const { t, i18n } = useTranslation()
  // Arabic-Indic digits where the locale calls for them, rather than toString().
  const number = (value: number) => new Intl.NumberFormat(i18n.language).format(value)

  // Rungs are proportional to the largest generation, so the widest bar is always full width
  // and the comparison between generations is the one the reader is being asked to make.
  const peakGeneration = Math.max(1, ...report.generations.map((g) => g.count))
  const generationRows: LadderRow[] = report.generations.map((generation) => ({
    key: String(generation.generation),
    label: t('reports.structure.generation', { number: generation.generation }),
    value: number(generation.count),
    ratio: generation.count / peakGeneration,
  }))

  const largestBranch = Math.max(1, ...report.branches.map((b) => b.descendantCount))
  const branchRows: LadderRow[] = report.branches.map((branch) => ({
    key: branch.id,
    label: branch.name,
    value: number(branch.descendantCount),
    ratio: branch.descendantCount / largestBranch,
  }))

  return (
    <>
      <Card labelledBy="structure-heading">
        {/* No caption: the rung labels below already name each generation, and the figures
            already give the depth. A line restating either is the accessory to remove. */}
        <CardHead id="structure-heading" title={t('reports.structure.title')} />

        <Stack gap="var(--space-5)">
          <Ladder rows={generationRows} rowTestId="generation-row" />

          <div style={{ height: 1, background: 'var(--divider)' }} />

          <FigureRow>
            <Figure
              label={t('reports.structure.totalMembers')}
              value={number(report.totalMembers)}
              testId="structure-total"
            />
            <Figure
              label={t('reports.structure.depth')}
              value={number(report.depth)}
              testId="structure-depth"
            />
            <Figure
              label={t('reports.structure.membersWithChildren')}
              value={number(report.membersWithChildren)}
            />
            <Figure
              label={t('reports.structure.leafMembers')}
              value={number(report.leafMembers)}
            />
            <Figure
              label={t('reports.structure.averageChildren')}
              value={number(report.averageChildrenPerParent)}
            />
          </FigureRow>
        </Stack>
      </Card>

      {/* Branches answer a different question from generations — which line grew largest — so
          they get their own card rather than a heading buried under the structure figures. */}
      <Card labelledBy="branches-heading">
        <CardHead
          id="branches-heading"
          title={t('reports.structure.branches')}
          caption={t('reports.structure.descendants')}
        />
        <Ladder rows={branchRows} rowTestId="branch-row" />
      </Card>
    </>
  )
}
