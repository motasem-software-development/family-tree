import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiFetch } from '../../services/apiClient'
import { reportsApi } from './reportsApi'

vi.mock('../../services/apiClient')

describe('reportsApi', () => {
  beforeEach(() => {
    vi.mocked(apiFetch).mockReset()
    vi.mocked(apiFetch).mockResolvedValue({} as never)
  })

  it('requests the single aggregate endpoint', async () => {
    await reportsApi.get()

    expect(apiFetch).toHaveBeenCalledWith('/api/v1/reports')
  })

  // The endpoint takes no parameters by design: the windows and caps are server-side
  // constants, so there is nothing for a client to tune.
  it('sends no query string', async () => {
    await reportsApi.get()

    expect(vi.mocked(apiFetch).mock.calls[0][0]).not.toContain('?')
  })
})
