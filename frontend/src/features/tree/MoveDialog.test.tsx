import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { I18nextProvider } from 'react-i18next'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n'
import { membersApi } from '../members/membersApi'
import type { FamilyTreeNode } from '../members/types'
import { MoveDialog } from './MoveDialog'

vi.mock('../members/membersApi')

const node = (
  id: string,
  name: string,
  generation: number,
  children: FamilyTreeNode[] = [],
  parentId: string | null = null,
): FamilyTreeNode => ({ id, name, parentId, generation, hasMoreChildren: false, children })

const SUBJECT = node('s1', 'سليمان', 1, [node('f1', 'فارس', 2, [], 's1')])

const renderDialog = (overrides: Partial<Parameters<typeof MoveDialog>[0]> = {}) => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const props = {
    member: SUBJECT,
    familyName: 'عائلة السقا',
    blockedIds: new Set(['s1', 'f1']),
    errorCode: null,
    isSaving: false,
    onCancel: vi.fn(),
    onConfirm: vi.fn(),
    ...overrides,
  }
  render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MoveDialog {...props} />
      </QueryClientProvider>
    </I18nextProvider>,
  )
  return props
}

describe('MoveDialog', () => {
  beforeEach(() => {
    vi.mocked(membersApi.search).mockResolvedValue({ total: 0, items: [] })
  })

  afterEach(() => vi.restoreAllMocks())

  it('offers the family tree itself as the first-generation target', async () => {
    const user = userEvent.setup()
    const props = renderDialog()

    await user.click(
      screen.getByRole('button', {
        name: i18n.t('move.rootOption', { family: 'عائلة السقا' }),
      }),
    )
    await user.click(screen.getByRole('button', { name: i18n.t('move.confirm') }))

    // null, not the tree's id: a first-generation member hangs off no member at all.
    expect(props.onConfirm).toHaveBeenCalledWith(null)
  })

  it('offers a searched member as a target', async () => {
    vi.mocked(membersApi.search).mockResolvedValue({
      total: 1,
      items: [{ id: 'd1', name: 'داوود', generation: 1, ancestors: [] }],
    })
    const user = userEvent.setup()
    const props = renderDialog()

    await user.type(screen.getByLabelText(i18n.t('move.searchPlaceholder')), 'داوود')
    await user.click(await screen.findByRole('button', { name: /داوود/ }))
    await user.click(screen.getByRole('button', { name: i18n.t('move.confirm') }))

    expect(props.onConfirm).toHaveBeenCalledWith('d1')
  })

  it('disables the member themselves and their descendants, with the reason', async () => {
    vi.mocked(membersApi.search).mockResolvedValue({
      total: 2,
      items: [
        { id: 's1', name: 'سليمان', generation: 1, ancestors: [] },
        { id: 'f1', name: 'فارس', generation: 2, ancestors: [{ id: 's1', name: 'سليمان' }] },
      ],
    })
    const user = userEvent.setup()
    renderDialog()

    await user.type(screen.getByLabelText(i18n.t('move.searchPlaceholder')), 'ال')

    expect(await screen.findByRole('button', { name: /سليمان/ })).toBeDisabled()
    expect(screen.getByRole('button', { name: /فارس/ })).toBeDisabled()
    expect(screen.getByText(i18n.t('move.self'))).toBeInTheDocument()
    expect(screen.getByText(i18n.t('move.descendant'))).toBeInTheDocument()
  })

  it('cannot be confirmed before a target is chosen', () => {
    renderDialog()

    expect(screen.getByRole('button', { name: i18n.t('move.confirm') })).toBeDisabled()
  })

  it('shows the translated server error rather than the raw code', () => {
    renderDialog({ errorCode: 'MOVE_CREATES_CYCLE' })

    expect(screen.getByText(i18n.t('errors.MOVE_CREATES_CYCLE'))).toBeInTheDocument()
    expect(screen.queryByText('MOVE_CREATES_CYCLE')).not.toBeInTheDocument()
  })

  it('closes on Escape, like the sibling modals', async () => {
    const user = userEvent.setup()
    const props = renderDialog()

    await user.keyboard('{Escape}')

    expect(props.onCancel).toHaveBeenCalled()
  })
})
