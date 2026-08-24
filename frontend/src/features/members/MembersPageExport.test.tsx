import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { I18nextProvider } from 'react-i18next'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import i18n from '../../i18n'
import { countriesApi } from '../countries/countriesApi'
import { MembersPage } from './MembersPage'
import { membersApi } from './membersApi'
import { downloadMembersXlsx } from './membersExportApi'
import type { FamilyMemberListItem } from './types'

vi.mock('./membersApi')
vi.mock('./membersExportApi', () => ({ downloadMembersXlsx: vi.fn() }))
vi.mock('../countries/countriesApi', () => ({ countriesApi: { list: vi.fn() } }))

let permissions: string[] = []
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    user: { email: 'admin@example.com', familyTreeName: 'عائلة السقا', permissions: [] },
    hasPermission: (code: string) => permissions.includes(code),
    logout: vi.fn(),
  }),
}))

const MEMBER: FamilyMemberListItem = {
  id: 'd1',
  name: 'داوود',
  parentId: null,
  version: 1,
  createdAt: '2026-08-16T12:00:00Z',
  updatedAt: '2026-08-16T12:00:00Z',
  dateOfBirth: null,
  dateOfDeath: null,
  isDeceased: false,
  nationalId: null,
  mobileNumber: null,
  whatsAppNumber: null,
  countryId: null,
  countryCode: null,
  branchId: null,
  branchName: null,
  generation: 0,
}

const renderPageAt = (path = '/members') => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[path]}>
          <MembersPage />
        </MemoryRouter>
      </QueryClientProvider>
    </I18nextProvider>,
  )
}

const exportButton = () => screen.getByRole('button', { name: i18n.t('members.export') })

beforeEach(() => {
  permissions = ['Member.View', 'Member.Create']
  vi.mocked(membersApi.list).mockResolvedValue([MEMBER])
  vi.mocked(membersApi.branches).mockResolvedValue([])
  vi.mocked(membersApi.generations).mockResolvedValue([0])
  vi.mocked(countriesApi.list).mockResolvedValue([])
  vi.mocked(downloadMembersXlsx).mockResolvedValue(undefined)
})

describe('MembersPage export', () => {
  it('offers the export to a member viewer', async () => {
    renderPageAt()

    expect(await screen.findByRole('button', { name: i18n.t('members.export') })).toBeInTheDocument()
  })

  it('hides the export without Member.View', () => {
    // The export carries exactly the data the page shows, so it takes the page's own permission.
    permissions = []
    renderPageAt()

    expect(
      screen.queryByRole('button', { name: i18n.t('members.export') }),
    ).not.toBeInTheDocument()
  })

  it('downloads with the filters currently in the URL', async () => {
    const user = userEvent.setup()
    renderPageAt('/members?status=deceased&generation=2')
    await waitFor(() => expect(exportButton()).toBeEnabled())

    await user.click(exportButton())

    expect(downloadMembersXlsx).toHaveBeenCalledWith(
      { status: 'deceased', generation: 2 },
      i18n.language,
      'عائلة السقا.xlsx',
    )
  })

  it('is disabled while the list is empty', async () => {
    // A header-only workbook is a confusing thing to hand someone who clicked Export.
    vi.mocked(membersApi.list).mockResolvedValue([])
    renderPageAt()

    await waitFor(() => expect(exportButton()).toBeDisabled())
  })

  it('reports a failed export without claiming a cause it does not know', async () => {
    vi.mocked(downloadMembersXlsx).mockRejectedValue(new Error('network'))

    const user = userEvent.setup()
    renderPageAt()
    await waitFor(() => expect(exportButton()).toBeEnabled())

    await user.click(exportButton())

    expect(
      await screen.findByText(i18n.t('errors.MEMBERS_EXPORT_FAILED')),
    ).toBeInTheDocument()
  })

  it('re-enables the button after a failure', async () => {
    vi.mocked(downloadMembersXlsx).mockRejectedValue(new Error('network'))

    const user = userEvent.setup()
    renderPageAt()
    await waitFor(() => expect(exportButton()).toBeEnabled())

    await user.click(exportButton())

    await waitFor(() => expect(exportButton()).toBeEnabled())
  })
})
