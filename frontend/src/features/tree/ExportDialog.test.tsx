import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { I18nextProvider } from 'react-i18next'
import { afterEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n'
import { ApiError } from '../../services/apiClient'
import { ExportDialog } from './ExportDialog'
import { downloadTreePdf } from './exportApi'

vi.mock('./exportApi')

const renderDialog = (onClose = vi.fn()) =>
  render(
    <I18nextProvider i18n={i18n}>
      <ExportDialog fileName="tree.pdf" onClose={onClose} />
    </I18nextProvider>,
  )

const clickExport = async () => {
  const user = userEvent.setup()
  await user.click(screen.getByRole('button', { name: i18n.t('tree.export.confirm') }))
}

const failWith = (error: unknown) => {
  vi.mocked(downloadTreePdf).mockRejectedValue(error)
}

describe('ExportDialog failure messages', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  /**
   * Spec §5.3's sole justification for the `reason` extension: `sheet-overflow` has a remedy the
   * caller can act on, and `member-cap` does not. The dialog previously did `catch { setFailed
   * (true) }` and showed one generic string, so the extension had no consumer at all.
   */
  it('offers the A4 remedy when the tree overflows a single sheet', async () => {
    failWith(new ApiError('EXPORT_TREE_TOO_LARGE', 413, 'sheet-overflow'))
    renderDialog()

    await clickExport()

    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent(i18n.t('tree.export.failedTooLargeSheet')),
    )
  })

  /**
   * The A4 option cannot fix either of these, so offering it would send the user round a loop
   * that fails the same way.
   */
  it.each(['member-cap', 'a4-page-cap'])(
    'offers no remedy for %s, which A4 cannot fix',
    async (reason) => {
      failWith(new ApiError('EXPORT_TREE_TOO_LARGE', 413, reason))
      renderDialog()

      await clickExport()

      await waitFor(() =>
        expect(screen.getByRole('alert')).toHaveTextContent(i18n.t('tree.export.failedTooLarge')),
      )
      expect(screen.getByRole('alert')).not.toHaveTextContent(
        i18n.t('tree.export.failedTooLargeSheet'),
      )
    },
  )

  /** A network drop or a 500 is not a size problem and must not be described as one. */
  it('shows the generic failure for an error that is not about size', async () => {
    failWith(new Error('network down'))
    renderDialog()

    await clickExport()

    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent(i18n.t('tree.export.failed')),
    )
  })

  it('shows the generic failure for a different backend code', async () => {
    failWith(new ApiError('MEMBER_NOT_FOUND', 404))
    renderDialog()

    await clickExport()

    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent(i18n.t('tree.export.failed')),
    )
  })

  it('closes without an alert when the export succeeds', async () => {
    vi.mocked(downloadTreePdf).mockResolvedValue(undefined)
    const onClose = vi.fn()
    renderDialog(onClose)

    await clickExport()

    await waitFor(() => expect(onClose).toHaveBeenCalled())
    expect(screen.queryByRole('alert')).toBeNull()
  })
})
