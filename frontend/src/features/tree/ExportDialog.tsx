import { useEffect, useState, type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../../services/apiClient'
import { downloadTreePdf, type ExportPage, type ExportStyle } from './exportApi'

/**
 * Which message a failed export shows.
 *
 * The backend's `reason` extension exists for exactly this decision (spec §5.3): `sheet-overflow`
 * has a remedy the user can act on — export as A4 pages — and `member-cap` and `a4-page-cap` do
 * not. Offering the A4 option for a cause A4 cannot fix sends the user round a loop that fails
 * the same way, so the two are deliberately told apart rather than collapsed into one "too large".
 * Anything else stays the generic failure: a network drop or a 500 is not a size problem and must
 * not be described as one.
 */
const messageKeyFor = (error: unknown): string => {
  if (!(error instanceof ApiError) || error.code !== 'EXPORT_TREE_TOO_LARGE') {
    return 'tree.export.failed'
  }

  return error.reason === 'sheet-overflow'
    ? 'tree.export.failedTooLargeSheet'
    : 'tree.export.failedTooLarge'
}

interface ExportDialogProps {
  /** The currently selected root, if any — narrows the export the same way the tree view is narrowed. */
  rootId?: string
  fileName: string
  onClose: () => void
}

const optionStyle = (checked: boolean): CSSProperties => ({
  display: 'flex',
  alignItems: 'center',
  gap: 9,
  padding: '9px 12px',
  border: `1px solid ${checked ? 'var(--primary)' : 'var(--border)'}`,
  borderRadius: 'var(--r-md)',
  background: checked ? 'var(--primary-subtle)' : 'var(--surface)',
  fontSize: 13,
  cursor: 'pointer',
})

export const ExportDialog = ({ rootId, fileName, onClose }: ExportDialogProps) => {
  const { t, i18n } = useTranslation()
  const [style, setStyle] = useState<ExportStyle>('xmind')
  const [page, setPage] = useState<ExportPage>('sheet')
  const [isExporting, setIsExporting] = useState(false)
  const [failureKey, setFailureKey] = useState<string | null>(null)

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !isExporting) onClose()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose, isExporting])

  const confirm = async () => {
    setIsExporting(true)
    setFailureKey(null)
    try {
      await downloadTreePdf({ rootId, style, page, language: i18n.language }, fileName)
      onClose()
    } catch (error) {
      setFailureKey(messageKeyFor(error))
    } finally {
      setIsExporting(false)
    }
  }

  return (
    <div
      style={{
        position: 'fixed',
        inset: 0,
        background: 'rgba(21,24,27,.36)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        zIndex: 400,
        // A fixed 24px gutter costs a 320px screen 15% of its width. Scales with the viewport
        // and stops at the designed 24px, so wide screens are unchanged.
        padding: 'clamp(12px, 4vw, 24px)',
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label={t('tree.export.title')}
        style={{
          width: '100%',
          maxWidth: 420,
          // Short in portrait, but two rows of options plus a failure message is taller than a
          // phone in landscape. Capped and scrolled, like the other dialogs.
          maxHeight: '100%',
          display: 'flex',
          flexDirection: 'column',
          background: 'var(--surface)',
          borderRadius: 'var(--r-lg)',
          boxShadow: 'var(--shadow-high)',
          overflow: 'hidden',
          animation: 'fadeUp var(--motion-base) var(--ease-standard)',
        }}
      >
        <div
          style={{
            padding: '22px clamp(16px, 5vw, 24px) 0',
            flex: '1 1 auto',
            minHeight: 0,
            overflowY: 'auto',
          }}
        >
          <div style={{ fontSize: 17, fontWeight: 600, lineHeight: 1.35 }}>
            {t('tree.export.title')}
          </div>

          <div style={{ marginTop: 18 }}>
            <div style={{ fontSize: 13, fontWeight: 500, marginBottom: 8 }}>
              {t('tree.export.style')}
            </div>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
              <label style={{ ...optionStyle(style === 'xmind'), flex: '1 1 140px' }}>
                <input
                  type="radio"
                  name="export-style"
                  checked={style === 'xmind'}
                  onChange={() => setStyle('xmind')}
                />
                {t('tree.export.styleXmind')}
              </label>
              <label style={{ ...optionStyle(style === 'clean'), flex: '1 1 140px' }}>
                <input
                  type="radio"
                  name="export-style"
                  checked={style === 'clean'}
                  onChange={() => setStyle('clean')}
                />
                {t('tree.export.styleClean')}
              </label>
            </div>
          </div>

          <div style={{ marginTop: 16 }}>
            <div style={{ fontSize: 13, fontWeight: 500, marginBottom: 8 }}>
              {t('tree.export.page')}
            </div>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
              <label style={{ ...optionStyle(page === 'sheet'), flex: '1 1 140px' }}>
                <input
                  type="radio"
                  name="export-page"
                  checked={page === 'sheet'}
                  onChange={() => setPage('sheet')}
                />
                {t('tree.export.pageSheet')}
              </label>
              <label style={{ ...optionStyle(page === 'a4'), flex: '1 1 140px' }}>
                <input
                  type="radio"
                  name="export-page"
                  checked={page === 'a4'}
                  onChange={() => setPage('a4')}
                />
                {t('tree.export.pageA4')}
              </label>
            </div>
          </div>

          {failureKey && (
            <div role="alert" style={{ marginTop: 16, fontSize: 13, color: 'var(--error)' }}>
              {t(failureKey)}
            </div>
          )}
        </div>

        <div
          style={{
            display: 'flex',
            flexWrap: 'wrap',
            justifyContent: 'flex-end',
            gap: 8,
            flex: '0 0 auto',
            padding: '22px clamp(16px, 5vw, 24px)',
          }}
        >
          <button
            type="button"
            onClick={onClose}
            disabled={isExporting}
            style={{
              height: 38,
              padding: '0 16px',
              border: '1px solid var(--border-strong)',
              borderRadius: 'var(--r-md)',
              background: 'var(--surface)',
              fontFamily: 'inherit',
              fontSize: 13,
              fontWeight: 500,
              cursor: isExporting ? 'not-allowed' : 'pointer',
            }}
          >
            {t('tree.export.cancel')}
          </button>
          <button
            type="button"
            onClick={() => void confirm()}
            disabled={isExporting}
            style={{
              height: 38,
              padding: '0 16px',
              border: 'none',
              borderRadius: 'var(--r-md)',
              background: 'var(--primary)',
              color: '#fff',
              fontFamily: 'inherit',
              fontSize: 13,
              fontWeight: 500,
              cursor: isExporting ? 'wait' : 'pointer',
              opacity: isExporting ? 0.7 : 1,
            }}
          >
            {isExporting ? t('tree.export.busy') : t('tree.export.confirm')}
          </button>
        </div>
      </div>
    </div>
  )
}
