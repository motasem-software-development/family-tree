import { useTranslation } from 'react-i18next'

/**
 * The living/deceased indicator. A filled dot rather than a check mark: a ✓ reads as
 * "verified" or "selected", while a dot reads as state — and state is what this is.
 */
export const LifeStatusDot = ({ deceased, size = 8 }: { deceased: boolean; size?: number }) => {
  const { t } = useTranslation()

  return (
    <span
      // Colour alone never carries the meaning: the dot is labelled, so a screen reader and a
      // colour-blind reader both get the status the sighted reader gets from the hue.
      role="img"
      aria-label={t(deceased ? 'members.deceased' : 'members.living')}
      title={t(deceased ? 'members.deceased' : 'members.living')}
      style={{
        flex: `0 0 ${size}px`,
        width: size,
        height: size,
        borderRadius: '50%',
        background: deceased ? 'var(--text-3)' : 'var(--success)',
        display: 'inline-block',
      }}
    />
  )
}
