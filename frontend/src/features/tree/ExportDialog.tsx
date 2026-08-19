import { useEffect, useState, type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import { downloadTreePdf, type ExportPage, type ExportStyle } from './exportApi'

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
  const { t } = useTranslation()
  const [style, setStyle] = useState<ExportStyle>('xmind')
  const [page, setPage] = useState<ExportPage>('sheet')
  const [isExporting, setIsExporting] = useState(false)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !isExporting) onClose()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose, isExporting])

  const confirm = async () => {
    setIsExporting(true)
    setFailed(false)
    try {
      await downloadTreePdf({ rootId, style, page }, fileName)
      onClose()
    } catch {
      setFailed(true)
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
        padding: 24,
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label={t('tree.export.title')}
        style={{
          width: '100%',
          maxWidth: 420,
          background: 'var(--surface)',
          borderRadius: 'var(--r-lg)',
          boxShadow: 'var(--shadow-high)',
          overflow: 'hidden',
          animation: 'fadeUp var(--motion-base) var(--ease-standard)',
        }}
      >
        <div style={{ padding: '22px 24px 0' }}>
          <div style={{ fontSize: 17, fontWeight: 600, lineHeight: 1.35 }}>
            {t('tree.export.title')}
          </div>

          <div style={{ marginTop: 18 }}>
            <div style={{ fontSize: 13, fontWeight: 500, marginBottom: 8 }}>
              {t('tree.export.style')}
            </div>
            <div style={{ display: 'flex', gap: 8 }}>
              <label style={{ ...optionStyle(style === 'xmind'), flex: 1 }}>
                <input
                  type="radio"
                  name="export-style"
                  checked={style === 'xmind'}
                  onChange={() => setStyle('xmind')}
                />
                {t('tree.export.styleXmind')}
              </label>
              <label style={{ ...optionStyle(style === 'clean'), flex: 1 }}>
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
            <div style={{ display: 'flex', gap: 8 }}>
              <label style={{ ...optionStyle(page === 'sheet'), flex: 1 }}>
                <input
                  type="radio"
                  name="export-page"
                  checked={page === 'sheet'}
                  onChange={() => setPage('sheet')}
                />
                {t('tree.export.pageSheet')}
              </label>
              <label style={{ ...optionStyle(page === 'a4'), flex: 1 }}>
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

          {failed && (
            <div role="alert" style={{ marginTop: 16, fontSize: 13, color: 'var(--error)' }}>
              {t('tree.export.failed')}
            </div>
          )}
        </div>

        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, padding: '22px 24px' }}>
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
